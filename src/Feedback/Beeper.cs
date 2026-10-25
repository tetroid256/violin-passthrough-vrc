using System.Runtime.InteropServices;

namespace ViolinPassthrough.Feedback;

public enum BeepKind
{
    /// <summary>カウントダウンの1秒ごと。</summary>
    Tick,

    /// <summary>成功。</summary>
    Success,

    /// <summary>失敗・取り消し。</summary>
    Error,
}

/// <summary>ビープ音。テストでは偽物に差し替える。</summary>
public interface IBeeper
{
    void Beep(BeepKind kind);
}

/// <summary>
/// Windows の Beep（PC の既定の音声出力に出る。VD 経由でヘッドセットで聞こえる）。
/// 鳴っている間メインループを止めないように、別スレッドで鳴らす。
/// </summary>
public sealed partial class WindowsBeeper : IBeeper
{
    private readonly object _gate = new();

    public void Beep(BeepKind kind)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var (frequency, duration) = kind switch
        {
            BeepKind.Success => (1320u, 250u),
            BeepKind.Error => (330u, 450u),
            _ => (880u, 90u),
        };

        ThreadPool.QueueUserWorkItem(_ =>
        {
            // 重なったら順番に鳴らす。
            lock (_gate)
            {
                BeepNative(frequency, duration);
            }
        });
    }

    [LibraryImport("kernel32.dll", EntryPoint = "Beep")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BeepNative(uint frequency, uint duration);
}
