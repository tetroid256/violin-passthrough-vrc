using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VRC.OSCQuery;
using ViolinPassthrough.Config;
using Log = ViolinPassthrough.Logging.Log;

namespace ViolinPassthrough.Osc;

/// <summary>メインループから見た OSC の送受信口。テストでは偽物に差し替える。</summary>
public interface IOscEndpoint : IDisposable
{
    /// <summary>受信したメッセージを1つ取り出す。メインループのスレッドから呼ぶ。</summary>
    bool TryReceive([NotNullWhen(true)] out OscMessage? message);

    void Send(OscMessage message);
}

/// <summary>
/// OSCQuery でサービスを広告し、UDP で受信する。VRChat へは UDP で送る。
/// OSCQuery が使えない（無効、または起動に失敗した）ときは fallbackListenPort で待ち受ける。
/// </summary>
public sealed class OscService : IOscEndpoint
{
    public const string ServiceNamePrefix = "ViolinPassthrough";
    private const int MaxQueuedMessages = 256;

    private readonly ConcurrentQueue<OscMessage> _queue = new();
    private readonly IReadOnlySet<string> _acceptedAddresses;
    private UdpClient? _receiver;
    private UdpClient? _sender;
    private IPEndPoint? _sendTo;
    private OSCQueryService? _oscQuery;
    private MeaModDiscovery? _discovery;
    private string _serviceName = ServiceNamePrefix;
    private Thread? _receiveThread;
    private volatile bool _disposed;

    private OscService(OscBindings bindings)
    {
        _acceptedAddresses = bindings.ReceiveAddresses;
    }

    /// <summary>受信しているポート。受信できていなければ null。</summary>
    public int? ListenPort { get; private set; }

    public bool UsingOscQuery { get; private set; }

    /// <summary>OSCQuery の HTTP サーバーのポート（テスト用）。</summary>
    internal int? HttpPort { get; private set; }

    public static OscService Start(OscSettings settings)
    {
        var service = new OscService(new OscBindings(settings));
        service.StartReceiver(settings);
        service.StartSender(settings);
        return service;
    }

    public bool TryReceive([NotNullWhen(true)] out OscMessage? message) => _queue.TryDequeue(out message);

    public void Send(OscMessage message)
    {
        if (_sender is null || _sendTo is null || _disposed)
        {
            return;
        }

        try
        {
            var bytes = OscCodec.Encode(message);
            _sender.Send(bytes, bytes.Length, _sendTo);
            Log.Debug($"OSC sent: {message}");
        }
        catch (SocketException ex)
        {
            // VRChat が起動していないときなど。
            Log.Debug($"OSC send failed ({ex.SocketErrorCode}): {message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopOscQuery();

        _receiver?.Dispose();
        _sender?.Dispose();
        _receiveThread?.Join(TimeSpan.FromSeconds(1));
        Log.Info("OSC stopped.");
    }

    private void StartReceiver(OscSettings settings)
    {
        if (settings.UseOscQuery && TryStartWithOscQuery(settings))
        {
            return;
        }

        try
        {
            _receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, settings.FallbackListenPort));
            ListenPort = settings.FallbackListenPort;
            Log.Info($"OSC listening on 127.0.0.1:{ListenPort} (fallback). Point VRChat's OSC output to this port.");
            StartReceiveThread();
        }
        catch (SocketException ex)
        {
            Log.Error($"Cannot listen for OSC on port {settings.FallbackListenPort} ({ex.SocketErrorCode}). OSC toggles will not work.");
            _receiver?.Dispose();
            _receiver = null;
        }
    }

    private bool TryStartWithOscQuery(OscSettings settings)
    {
        try
        {
            // 先に UDP ソケットを空いているポートで開いてから、そのポートを広告する（ポートの取り合いを避ける）。
            _receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var udpPort = ((IPEndPoint)_receiver.Client.LocalEndPoint!).Port;
            var tcpPort = Extensions.GetAvailableTcpPort();

            // 起動ごとに名前を変える。古いポートの広告が VRChat に残っていても、新しい方と混ざらないように。
            _serviceName = $"{ServiceNamePrefix}-{Random.Shared.Next(0x10000):x4}";

            // Builder の WithDefaults は途中で失敗すると後始末できないので、手順を分けて呼ぶ。
            // ポートなどを設定しただけの Build() は、サーバーを起動せずにサービスを返す。
            var logger = new OscQueryLogger();
            _oscQuery = new OSCQueryServiceBuilder()
                .WithServiceName(_serviceName)
                .WithTcpPort(tcpPort)
                .WithUdpPort(udpPort)
                .WithLogger(logger)
                .Build();

            // VRChat は /avatar を持つサービスにアバターの値を送ってくる。広告する前に登録しておく。
            _oscQuery.AddEndpoint<string>(OscBindings.AvatarChangeAddress, Attributes.AccessValues.WriteOnly, null, "Avatar change");
            _oscQuery.AddEndpoint<bool>(OscBindings.ParameterPrefix + settings.EnabledParameter, Attributes.AccessValues.WriteOnly, null, "Passthrough on/off");
            _oscQuery.AddEndpoint<int>(OscBindings.ParameterPrefix + settings.PresetParameter, Attributes.AccessValues.WriteOnly, null, "Passthrough preset index");
            _oscQuery.AddEndpoint<bool>(OscBindings.ParameterPrefix + settings.DebugParameter, Attributes.AccessValues.WriteOnly, null, "Passthrough debug display");
            _oscQuery.AddEndpoint<bool>(OscBindings.ParameterPrefix + settings.AutoParameter, Attributes.AccessValues.WriteOnly, null, "Passthrough auto mode");
            _oscQuery.AddEndpoint<bool>(OscBindings.ParameterPrefix + settings.CalibrateParameter, Attributes.AccessValues.WriteOnly, null, "Passthrough calibration");

            _oscQuery.StartHttpServer();
            // 広告する前に、HTTP サーバーが応答するかを確かめる（使えないサービスを広告しないため）。
            if (!IsHttpServerReachable(tcpPort))
            {
                throw new InvalidOperationException($"OSCQuery HTTP server on port {tcpPort} is not reachable");
            }

            HttpPort = tcpPort;
            ListenPort = udpPort;
            _discovery = new MeaModDiscovery(logger);
            _oscQuery.SetDiscovery(_discovery);
            _oscQuery.AdvertiseOSCQueryService(_serviceName, tcpPort);
            _oscQuery.AdvertiseOSCService(_serviceName, udpPort);

            UsingOscQuery = true;
            Log.Info($"OSCQuery advertised as \"{_serviceName}\" (HTTP {tcpPort}, OSC UDP 127.0.0.1:{udpPort}).");
            StartReceiveThread();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"OSCQuery could not start ({ex.Message}). Falling back to port {settings.FallbackListenPort}.");
            StopOscQuery();
            _receiver?.Dispose();
            _receiver = null;
            ListenPort = null;
            HttpPort = null;
            return false;
        }
    }

    /// <summary>広告を取り消して（mDNS の goodbye を送って）から、HTTP サーバーと mDNS を止める。</summary>
    private void StopOscQuery()
    {
        if (_discovery is not null && UsingOscQuery && HttpPort is { } tcpPort && ListenPort is { } udpPort)
        {
            try
            {
                _discovery.Unadvertise(new OSCQueryServiceProfile(_serviceName, IPAddress.Loopback, tcpPort, OSCQueryServiceProfile.ServiceType.OSCQuery));
                _discovery.Unadvertise(new OSCQueryServiceProfile(_serviceName, IPAddress.Loopback, udpPort, OSCQueryServiceProfile.ServiceType.OSC));
            }
            catch (Exception ex)
            {
                Log.Debug($"OSCQuery unadvertise failed: {ex.Message}");
            }
        }

        try
        {
            _oscQuery?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to stop OSCQuery: {ex.Message}");
        }

        _oscQuery = null;
        _discovery = null;
        UsingOscQuery = false;
    }

    private static bool IsHttpServerReachable(int tcpPort)
    {
        try
        {
            using var handler = new HttpClientHandler { UseProxy = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
            using var response = http.GetAsync($"http://127.0.0.1:{tcpPort}/?HOST_INFO").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Debug($"OSCQuery self-check failed: {ex.Message}");
            return false;
        }
    }

    private void StartSender(OscSettings settings)
    {
        try
        {
            var address = IPAddress.TryParse(settings.VrchatSendHost, out var parsed)
                ? parsed
                : Dns.GetHostAddresses(settings.VrchatSendHost).First(a => a.AddressFamily == AddressFamily.InterNetwork);
            _sendTo = new IPEndPoint(address, settings.VrchatSendPort);
            _sender = new UdpClient(address.AddressFamily);
            Log.Info($"OSC sending to {_sendTo}.");
        }
        catch (Exception ex) when (ex is SocketException or InvalidOperationException or ArgumentException)
        {
            Log.Error($"Cannot send OSC to {settings.VrchatSendHost}:{settings.VrchatSendPort} ({ex.Message}).");
            _sender?.Dispose();
            _sender = null;
            _sendTo = null;
        }
    }

    private void StartReceiveThread()
    {
        _receiveThread = new Thread(ReceiveLoop)
        {
            IsBackground = true,
            Name = "OSC receive",
        };
        _receiveThread.Start();
    }

    private void ReceiveLoop()
    {
        var receiver = _receiver!;
        while (!_disposed)
        {
            byte[] data;
            try
            {
                var remote = new IPEndPoint(IPAddress.Any, 0);
                data = receiver.Receive(ref remote);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset && !_disposed)
            {
                continue;
            }
            catch (Exception ex) when (ex is ObjectDisposedException or SocketException && _disposed)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error("OSC receive failed", ex);
                Thread.Sleep(100);
                continue;
            }

            List<OscMessage> messages;
            try
            {
                messages = OscCodec.Parse(data);
            }
            catch (FormatException ex)
            {
                Log.Debug($"Ignored malformed OSC packet ({data.Length} bytes): {ex.Message}");
                continue;
            }

            foreach (var message in messages)
            {
                // VRChat はアバターの全パラメータを送ってくるので、使うものだけ残す。
                if (!_acceptedAddresses.Contains(message.Address))
                {
                    continue;
                }

                Log.Debug($"OSC received: {message}");
                _queue.Enqueue(message);
                while (_queue.Count > MaxQueuedMessages && _queue.TryDequeue(out _))
                {
                }
            }
        }
    }

    /// <summary>OSCQuery ライブラリのログを本アプリのログに流す。</summary>
    private sealed class OscQueryLogger : ILogger<OSCQueryService>
    {
        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => logLevel != Microsoft.Extensions.Logging.LogLevel.None;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = "OSCQuery: " + formatter(state, exception);
            if (exception is not null)
            {
                message += $" ({exception.Message})";
            }

            switch (logLevel)
            {
                case Microsoft.Extensions.Logging.LogLevel.Warning:
                    ViolinPassthrough.Logging.Log.Warn(message);
                    break;
                case Microsoft.Extensions.Logging.LogLevel.Error:
                case Microsoft.Extensions.Logging.LogLevel.Critical:
                    ViolinPassthrough.Logging.Log.Error(message);
                    break;
                default:
                    ViolinPassthrough.Logging.Log.Debug(message);
                    break;
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
