using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ViolinPassthrough.Anchor;
using ViolinPassthrough.Calibration;
using ViolinPassthrough.Logging;

namespace ViolinPassthrough.Config;

/// <summary>メインループから見た設定の供給元。テストでは偽物に差し替える。</summary>
public interface IConfigSource
{
    /// <summary>最後に読み込めた正常な設定。</summary>
    ResolvedConfig Current { get; }

    /// <summary>変更があれば読み込む。新しい正常な設定を読み込んだら true。</summary>
    bool TryReload(out ResolvedConfig? config);

    /// <summary>不正な設定を読んだ回数。増えたら通知表示でエラーを知らせる。</summary>
    int InvalidCount { get; }

    /// <summary>キャリブレーションの結果を設定ファイルに書き込む。反映はホットリロードで行う。</summary>
    bool TryApplyCalibration(CalibrationResult result, out string error);
}

/// <summary>
/// 設定ファイルの読み込み、ホットリロード、検証。
/// 変更の検出は FileSystemWatcher と更新日時のポーリングの両方で行う（どちらかが取りこぼしても1秒以内に反映するため）。
/// 読み込み自体はメインループから <see cref="TryReload"/> で行うので、スレッドをまたがない。
/// </summary>
public sealed partial class ConfigStore : IConfigSource, IDisposable
{
    public const string DefaultConfigResourceName = "ViolinPassthrough.config.default.json";

    /// <summary>最後の変更から読み込むまでの待ち時間。保存途中のファイルを読まないため。</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(200);

    /// <summary>更新日時を確認する間隔。</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private bool _dirty;
    // 時計の巻き戻しの影響を受けないように、単調増加のタイムスタンプで測る。
    private long _lastChange;
    private long? _lastPoll;
    private (DateTime WriteTimeUtc, long Length)? _loadedStamp;
    private bool _missingLogged;

    public ConfigStore(string path, TimeProvider? timeProvider = null)
    {
        FilePath = Path.GetFullPath(path);
        _time = timeProvider ?? TimeProvider.System;
        Current = LoadDefault();
    }

    /// <summary>%APPDATA%\ViolinPassthrough\config.json</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "ViolinPassthrough",
        "config.json");

    public string FilePath { get; }

    /// <summary>最後に読み込めた正常な設定。</summary>
    public ResolvedConfig Current { get; private set; }

    public int InvalidCount { get; private set; }

    /// <summary>キャリブレーションで書き換える前の内容を残すファイル。</summary>
    public string BackupPath => FilePath + ".bak";

    /// <summary>同梱の既定の設定（config.default.json）。</summary>
    public static string ReadDefaultConfigText()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(DefaultConfigResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {DefaultConfigResourceName} not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 設定ファイルがなければ既定の設定をコピーし、読み込む。読み込めなければ既定の設定を使う。
    /// </summary>
    public void Initialize()
    {
        if (!File.Exists(FilePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, ReadDefaultConfigText());
            Log.Info($"Created {FilePath} from the default config.");
        }

        if (!TryLoadFile(out var config))
        {
            Log.Warn("Using the built-in default config until the config file is fixed.");
            return;
        }

        Current = config!;
        Log.Info($"Loaded config from {FilePath}.");
    }

    /// <summary>ファイルの変更監視を始める。</summary>
    public void StartWatching()
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        var watcher = new FileSystemWatcher(directory)
        {
            Filter = Path.GetFileName(FilePath),
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
        };
        // エディタによっては一時ファイルに書いてから名前を変えるので、Renamed も見る。
        watcher.Changed += (_, _) => MarkDirty();
        watcher.Created += (_, _) => MarkDirty();
        watcher.Renamed += (_, _) => MarkDirty();
        watcher.Error += (_, e) =>
        {
            Log.Warn($"Config watcher error: {e.GetException().Message}. Falling back to polling.");
            MarkDirty();
        };
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
        Log.Debug($"Watching {FilePath}.");
    }

    /// <summary>
    /// ファイルが変わっていれば読み込む。メインループから毎フレーム呼ぶ。
    /// </summary>
    /// <returns>新しい正常な設定を読み込んだら true。不正な設定なら false を返し、<see cref="Current"/> は前回のまま。</returns>
    public bool TryReload(out ResolvedConfig? config)
    {
        config = null;
        var now = _time.GetTimestamp();

        if (_lastPoll is not { } lastPoll || _time.GetElapsedTime(lastPoll, now) >= PollInterval)
        {
            _lastPoll = now;
            bool dirty;
            lock (_gate)
            {
                dirty = _dirty;
            }

            // すでに変更を検知していれば、待ち時間を数え直さない。
            if (!dirty && !Equals(GetStamp(), _loadedStamp))
            {
                MarkDirty();
            }
        }

        lock (_gate)
        {
            if (!_dirty || _time.GetElapsedTime(_lastChange, now) < Debounce)
            {
                return false;
            }

            _dirty = false;
        }

        if (!TryLoadFile(out config))
        {
            return false;
        }

        Current = config!;
        Log.Info("Config reloaded.");
        return true;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private void MarkDirty()
    {
        lock (_gate)
        {
            _dirty = true;
            _lastChange = _time.GetTimestamp();
        }
    }

    private bool TryLoadFile(out ResolvedConfig? config)
    {
        config = null;
        var stamp = GetStamp();
        if (stamp is null)
        {
            if (!_missingLogged)
            {
                Log.Warn($"Config file {FilePath} not found. Keeping the current config.");
                _missingLogged = true;
            }

            _loadedStamp = null;
            return false;
        }

        _missingLogged = false;
        string text;
        try
        {
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 保存中でロックされているなど。少し待ってから読み直す。
            Log.Debug($"Config file is busy ({ex.Message}). Retrying.");
            MarkDirty();
            return false;
        }

        // 中身の読み込みに成功したら、正常かどうかに関わらず同じ版は読み直さない。
        _loadedStamp = stamp;

        ConfigParseResult result;
        try
        {
            result = ConfigValidator.Parse(text);
        }
        catch (Exception ex)
        {
            // 検証の漏れで例外が出ても落とさない。
            Log.Error($"Unexpected error while reading {FilePath}; keeping the previous config", ex);
            return false;
        }

        if (!result.Success)
        {
            InvalidCount++;
            Log.Warn($"Config file {FilePath} is invalid; keeping the previous config:{Environment.NewLine}  - " +
                string.Join(Environment.NewLine + "  - ", result.Errors));
            return false;
        }

        config = result.Config;
        return true;
    }

    /// <summary>
    /// キャリブレーションの結果を設定ファイルに書き込む（DESIGN.md §15.2）。
    /// 領域の position / rotationEulerDeg / widthMeters と、auto.leftHandPosition / auto.headUp を書き換える。
    /// 書く前に元のファイルを .bak に残し、一時ファイルに書いてから置き換える。JSON のコメントは消える。
    /// </summary>
    public bool TryApplyCalibration(CalibrationResult result, out string error)
    {
        try
        {
            string text;
            using (var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                text = reader.ReadToEnd();
            }

            var json = UpdateCalibrationJson(text, result, out error);
            if (json is null)
            {
                return false;
            }

            var check = ConfigValidator.Parse(json);
            if (!check.Success)
            {
                error = "RESULT IS INVALID";
                Log.Warn("Calibration result would make the config invalid: " + string.Join("; ", check.Errors));
                return false;
            }

            File.Copy(FilePath, BackupPath, overwrite: true);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
            MarkDirty();
            Log.Info($"Calibration written to {FilePath} (backup: {BackupPath}).");
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error("Failed to write the calibration", ex);
            error = "CANNOT WRITE CONFIG";
            return false;
        }
    }

    /// <summary>設定の JSON にキャリブレーションの結果を書き込んだ文字列。書けなければ null。</summary>
    internal static string? UpdateCalibrationJson(string text, CalibrationResult result, out string error)
    {
        var root = JsonNode.Parse(
            text,
            new JsonNodeOptions { PropertyNameCaseInsensitive = true },
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject;
        if (root?["regions"] is not JsonArray regions)
        {
            error = "CONFIG HAS NO REGIONS";
            return null;
        }

        var region = regions.OfType<JsonObject>().FirstOrDefault(r =>
            r["id"] is JsonValue id && id.TryGetValue<string>(out var value) && value == result.RegionId);
        if (region is null)
        {
            error = "REGION NOT FOUND";
            return null;
        }

        region["position"] = Vector(result.Position, 3);
        region["rotationEulerDeg"] = Vector(result.RotationEulerDeg, 1);
        region["widthMeters"] = Round(result.WidthMeters, 3);

        if (root["auto"] is not JsonObject auto)
        {
            auto = new JsonObject();
            root["auto"] = auto;
        }

        auto["leftHandPosition"] = Vector(result.LeftHandPosition, 3);
        auto["headUp"] = Vector(result.HeadUp, 3);

        var json = root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        // 数値だけの配列は1行にまとめる（手で編集しやすいように）。
        json = NumberArrayPattern().Replace(json, m =>
            "[" + string.Join(", ", m.Groups[1].Value.Split(',').Select(v => v.Trim())) + "]");

        error = string.Empty;
        return json + Environment.NewLine;
    }

    private static JsonArray Vector(Vec3 v, int digits) =>
        new(Round(v.X, digits), Round(v.Y, digits), Round(v.Z, digits));

    // -0 を 0 にしてから丸める（"-0" と書かないため）。
    private static JsonNode Round(double value, int digits) => JsonValue.Create(Math.Round(value, digits) + 0.0);

    [GeneratedRegex(@"\[\s*(-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?(?:\s*,\s*-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?)*)\s*\]")]
    private static partial Regex NumberArrayPattern();

    private (DateTime WriteTimeUtc, long Length)? GetStamp()
    {
        try
        {
            var info = new FileInfo(FilePath);
            return info.Exists ? (info.LastWriteTimeUtc, info.Length) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static ResolvedConfig LoadDefault()
    {
        var result = ConfigValidator.Parse(ReadDefaultConfigText());
        return result.Config
            ?? throw new InvalidOperationException("The built-in default config is invalid: " + string.Join("; ", result.Errors));
    }
}
