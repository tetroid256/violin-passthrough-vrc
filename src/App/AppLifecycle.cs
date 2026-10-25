using System.Runtime.InteropServices;
using Valve.VR;
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
}

/// <summary>
/// OpenVR の初期化、VREvent の処理、終了処理。
/// OpenVR を呼ぶのは、このクラスと MaskOverlay だけにする。
/// </summary>
public sealed class AppLifecycle : IAppLifecycle, IDisposable
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(3);
    private static readonly uint EventSize = (uint)Marshal.SizeOf<VREvent_t>();

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
                    break;
            }
        }

        return result;
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
}
