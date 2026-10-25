using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Valve.VR;
using ViolinPassthrough.Anchor;
using ViolinPassthrough.Logging;

namespace ViolinPassthrough.App;

[Flags]
public enum AppEvents
{
    None = 0,

    /// <summary>SteamVR から終了を求められた。</summary>
    QuitRequested = 1 << 0,

    /// <summary>トラッキングデバイスの接続やロールが変わった。</summary>
    DevicesChanged = 1 << 1,
}

/// <summary>メインループから見た OpenVR ランタイム。テストでは偽物に差し替える。</summary>
public interface IAppLifecycle
{
    /// <summary>溜まっている VREvent をすべて処理する。</summary>
    AppEvents PollEvents();

    /// <summary>今のトラッキング情報（standing 空間）。デバイス追従のアンカーがあるときだけ毎フレーム呼ぶ。</summary>
    TrackingSnapshot GetTrackingSnapshot();
}

/// <summary>
/// OpenVR の初期化、VREvent の処理、終了処理、マニフェスト登録。
/// OpenVR を呼ぶのは、このクラスと MaskOverlay だけにする。
/// </summary>
public sealed class AppLifecycle : IAppLifecycle, IDisposable
{
    /// <summary>manifest.vrmanifest の app_key。</summary>
    public const string AppKey = "local.violinpassthrough";

    public const string ManifestFileName = "manifest.vrmanifest";

    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ServerCheckInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SerialRefreshInterval = TimeSpan.FromSeconds(10);
    private static readonly uint EventSize = (uint)Marshal.SizeOf<VREvent_t>();

    private readonly Stopwatch _sinceServerCheck = Stopwatch.StartNew();
    private readonly Stopwatch _sinceSerialRefresh = new();
    private readonly TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    private readonly DevicePose[] _devicePoses = new DevicePose[OpenVR.k_unMaxTrackedDeviceCount];
    private IReadOnlyDictionary<string, uint>? _serials;
    private string? _loggedDevices;
    private bool _initialized;

    /// <summary>
    /// SteamVR の起動を待ってから、Overlay アプリとして初期化する。
    /// SteamVR が起動していなければ、起動するまで待機して再試行する（本アプリから SteamVR は起動しない）。
    /// </summary>
    /// <returns>初期化できたら true。キャンセルされたら false。</returns>
    public bool WaitForRuntimeAndInit(CancellationToken cancellationToken)
    {
        var loggedWaiting = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (TryInit(out var error))
            {
                _initialized = true;
                Log.Info("OpenVR initialized as an overlay application.");
                _serials = ReadSerials(OpenVR.System);
                return true;
            }

            if (!loggedWaiting)
            {
                Log.Info($"Waiting for SteamVR ({error}). Retrying every {RetryInterval.TotalSeconds:0} s.");
                loggedWaiting = true;
            }
            else
            {
                Log.Debug($"SteamVR not ready yet ({error}).");
            }

            if (cancellationToken.WaitHandle.WaitOne(RetryInterval))
            {
                break;
            }
        }

        return false;
    }

    public AppEvents PollEvents()
    {
        var system = OpenVR.System;
        if (!_initialized || system is null)
        {
            return AppEvents.None;
        }

        var result = AppEvents.None;
        var ev = new VREvent_t();
        while (system.PollNextEvent(ref ev, EventSize))
        {
            switch ((EVREventType)ev.eventType)
            {
                case EVREventType.VREvent_Quit:
                case EVREventType.VREvent_DriverRequestedQuit:
                    Log.Info($"Received {(EVREventType)ev.eventType}.");
                    result |= AppEvents.QuitRequested;
                    break;

                case EVREventType.VREvent_TrackedDeviceActivated:
                case EVREventType.VREvent_TrackedDeviceDeactivated:
                case EVREventType.VREvent_TrackedDeviceRoleChanged:
                    result |= AppEvents.DevicesChanged;
                    _serials = null;
                    break;
            }
        }

        if (_serials is null)
        {
            // 接続が変わったら一覧を取り直す（トラッカーのシリアル番号を調べられるように、ログにも出す）。
            _serials = ReadSerials(system);
            _sinceSerialRefresh.Restart();
        }

        if (_sinceServerCheck.Elapsed >= ServerCheckInterval)
        {
            _sinceServerCheck.Restart();
            if (!IsServerRunning())
            {
                // SteamVR が落ちたときは VREvent_Quit が来ないので、プロセスの有無で判断する。
                Log.Warn("SteamVR server (vrserver) is no longer running.");
                result |= AppEvents.QuitRequested;
            }
        }

        return result;
    }

    public TrackingSnapshot GetTrackingSnapshot()
    {
        var system = OpenVR.System;
        if (!_initialized || system is null)
        {
            return new TrackingSnapshot(
                TrackingUniverse.Standing,
                Array.Empty<DevicePose>(),
                TrackingSnapshot.InvalidDeviceIndex,
                TrackingSnapshot.InvalidDeviceIndex,
                new Dictionary<string, uint>());
        }

        system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0, _poses);
        // 毎フレーム呼ぶので配列は使い回す（スナップショットは次のフレームまでしか使わない）。
        for (var i = 0; i < _poses.Length; i++)
        {
            _devicePoses[i] = new DevicePose(_poses[i].bDeviceIsConnected, _poses[i].bPoseIsValid, FromHmdMatrix(_poses[i].mDeviceToAbsoluteTracking));
        }

        // シリアル番号はデバイスの接続が変わったときと、念のため一定間隔で取り直す。
        if (_serials is null || _sinceSerialRefresh.Elapsed >= SerialRefreshInterval)
        {
            _serials = ReadSerials(system);
            _sinceSerialRefresh.Restart();
        }

        return new TrackingSnapshot(
            TrackingUniverse.Standing,
            _devicePoses,
            system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand),
            system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand),
            _serials);
    }

    /// <summary>
    /// マニフェストを SteamVR に登録し、初回は自動起動を有効にする。
    /// 登録済みなら何もしない（ユーザーが SteamVR の設定で自動起動を切っていたら、その設定を尊重する）。
    /// 別の場所から登録されていたら（フォルダを移動した場合）、古い登録を消して登録し直す。
    /// </summary>
    public void RegisterManifest(string manifestPath, string executablePath)
    {
        var apps = OpenVR.Applications;
        if (!_initialized || apps is null)
        {
            Log.Error("Cannot register the manifest: IVRApplications is not available.");
            return;
        }

        if (!File.Exists(manifestPath))
        {
            Log.Error($"Manifest not found: {manifestPath}");
            return;
        }

        if (apps.IsApplicationInstalled(AppKey))
        {
            var registeredBinary = GetRegisteredBinaryPath(apps);
            if (registeredBinary is null || SamePath(registeredBinary, executablePath))
            {
                Log.Info($"Already registered with SteamVR (auto launch: {apps.GetApplicationAutoLaunch(AppKey)}).");
                return;
            }

            Log.Info($"Registered from another location ({registeredBinary}); re-registering from {executablePath}.");
            var oldManifest = Path.Combine(Path.GetDirectoryName(registeredBinary) ?? string.Empty, ManifestFileName);
            CheckApplications(apps, apps.RemoveApplicationManifest(oldManifest), $"RemoveApplicationManifest({oldManifest})");
        }

        if (!CheckApplications(apps, apps.AddApplicationManifest(manifestPath, false), "AddApplicationManifest"))
        {
            return;
        }

        if (CheckApplications(apps, apps.SetApplicationAutoLaunch(AppKey, true), "SetApplicationAutoLaunch"))
        {
            Log.Info($"Registered {manifestPath} with SteamVR and enabled auto launch.");
        }
    }

    /// <summary>自動起動を止め、マニフェストの登録を消す（--unregister）。</summary>
    /// <returns>すべて成功したら true。</returns>
    public bool UnregisterManifest(string manifestPath)
    {
        var apps = OpenVR.Applications;
        if (!_initialized || apps is null)
        {
            Log.Error("Cannot unregister the manifest: IVRApplications is not available.");
            return false;
        }

        var ok = true;

        var manifests = new List<string> { manifestPath };
        if (apps.IsApplicationInstalled(AppKey))
        {
            ok &= CheckApplications(apps, apps.SetApplicationAutoLaunch(AppKey, false), "SetApplicationAutoLaunch(false)");

            // 別の場所から登録されている場合は、そちらのマニフェストも消す。
            if (GetRegisteredBinaryPath(apps) is { } registered)
            {
                var registeredManifest = Path.Combine(Path.GetDirectoryName(registered) ?? string.Empty, ManifestFileName);
                if (!SamePath(registeredManifest, manifestPath))
                {
                    manifests.Add(registeredManifest);
                }
            }
        }

        foreach (var manifest in manifests)
        {
            if (CheckApplications(apps, apps.RemoveApplicationManifest(manifest), $"RemoveApplicationManifest({manifest})"))
            {
                Log.Info($"Unregistered {manifest} from SteamVR.");
            }
            else
            {
                ok = false;
            }
        }

        return ok;
    }

    /// <summary>VREvent_Quit への応答。この後すぐにオーバーレイを破棄して終了すること。</summary>
    public void AcknowledgeQuit()
    {
        if (_initialized && OpenVR.System is { } system)
        {
            system.AcknowledgeQuit_Exiting();
            Log.Info("Acknowledged quit.");
        }
    }

    public void Dispose()
    {
        if (!_initialized)
        {
            return;
        }

        _initialized = false;
        OpenVR.Shutdown();
        Log.Info("OpenVR shut down.");
    }

    private static bool TryInit(out string error)
    {
        // Background で接続を試すと、SteamVR が起動していないときに SteamVR を起動せずに失敗する。
        // 起動していることを確かめてから、Overlay として初期化し直す。
        var initError = EVRInitError.None;
        OpenVR.Init(ref initError, EVRApplicationType.VRApplication_Background);
        if (initError != EVRInitError.None)
        {
            error = Describe(initError);
            return false;
        }

        OpenVR.Shutdown();

        initError = EVRInitError.None;
        OpenVR.Init(ref initError, EVRApplicationType.VRApplication_Overlay);
        if (initError != EVRInitError.None)
        {
            error = Describe(initError);
            OpenVR.Shutdown();
            return false;
        }

        if (OpenVR.Overlay is null)
        {
            error = "IVROverlay is not available";
            OpenVR.Shutdown();
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string Describe(EVRInitError error) =>
        $"{OpenVR.GetStringForHmdError(error)} ({error})";

    internal static Pose3x4 FromHmdMatrix(HmdMatrix34_t m) => new(
        m.m0, m.m1, m.m2, m.m3,
        m.m4, m.m5, m.m6, m.m7,
        m.m8, m.m9, m.m10, m.m11);

    private Dictionary<string, uint> ReadSerials(CVRSystem system)
    {
        var serials = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var descriptions = new List<string>();
        var buffer = new StringBuilder(256);
        for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
        {
            if (!system.IsTrackedDeviceConnected(i))
            {
                continue;
            }

            var error = ETrackedPropertyError.TrackedProp_Success;
            buffer.Clear();
            system.GetStringTrackedDeviceProperty(i, ETrackedDeviceProperty.Prop_SerialNumber_String, buffer, (uint)buffer.Capacity, ref error);
            if (error == ETrackedPropertyError.TrackedProp_Success && buffer.Length > 0)
            {
                var serial = buffer.ToString();
                serials.TryAdd(serial, i);
                descriptions.Add($"#{i} {system.GetTrackedDeviceClass(i)} \"{serial}\"");
            }
        }

        // 一覧が変わったときだけ出す（device アンカーの serial に使う値）。
        var text = string.Join(", ", descriptions);
        if (text != _loggedDevices)
        {
            Log.Info($"Tracked devices: {(text.Length == 0 ? "none" : text)}");
            _loggedDevices = text;
        }

        return serials;
    }

    private static bool CheckApplications(CVRApplications apps, EVRApplicationError error, string operation)
    {
        if (error == EVRApplicationError.None)
        {
            return true;
        }

        Log.Error($"{operation} failed: {apps.GetApplicationsErrorNameFromEnum(error)} ({(int)error})");
        return false;
    }

    private static string? GetRegisteredBinaryPath(CVRApplications apps)
    {
        var buffer = new StringBuilder(2048);
        var error = EVRApplicationError.None;
        apps.GetApplicationPropertyString(AppKey, EVRApplicationProperty.BinaryPath_String, buffer, (uint)buffer.Capacity, ref error);
        if (error != EVRApplicationError.None)
        {
            Log.Debug($"Could not read the registered binary path: {apps.GetApplicationsErrorNameFromEnum(error)}");
            return null;
        }

        var path = buffer.ToString();
        return path.Length == 0 ? null : path;
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsServerRunning()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        var processes = Process.GetProcessesByName("vrserver");
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }
}
