using System.Net;
using System.Net.Sockets;
using ViolinPassthrough.Osc;

namespace ViolinPassthrough.Tests.Osc;

/// <summary>実際の UDP ソケットでの送受信。</summary>
public class OscServiceTests
{
    [Fact]
    public void Fallback_ReceivesOnlyRelevantMessagesAndSends()
    {
        using var vrchat = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var vrchatPort = ((IPEndPoint)vrchat.Client.LocalEndPoint!).Port;
        var listenPort = FreeUdpPort();
        var settings = TestConfigs.Default.Osc with
        {
            UseOscQuery = false,
            FallbackListenPort = listenPort,
            VrchatSendPort = vrchatPort,
        };

        using var service = OscService.Start(settings);
        Assert.False(service.UsingOscQuery);
        Assert.Equal(listenPort, service.ListenPort);

        using (var sender = new UdpClient())
        {
            var target = new IPEndPoint(IPAddress.Loopback, listenPort);
            foreach (var message in new[]
            {
                new OscMessage("/avatar/parameters/VelocityX", 0.3f),
                new OscMessage("/avatar/parameters/HandPassthrough", true),
            })
            {
                var bytes = OscCodec.Encode(message);
                sender.Send(bytes, bytes.Length, target);
            }

            sender.Send(new byte[] { 1, 2, 3 }, 3, target);
        }

        var received = WaitForMessage(service);
        Assert.Equal("/avatar/parameters/HandPassthrough True", received.ToString());
        Thread.Sleep(100);
        Assert.False(service.TryReceive(out _));

        service.Send(new OscMessage("/avatar/parameters/HandPassthroughPreset", 1));
        vrchat.Client.ReceiveTimeout = 2000;
        var remote = new IPEndPoint(IPAddress.Any, 0);
        var sent = Assert.Single(OscCodec.Parse(vrchat.Receive(ref remote)));
        Assert.Equal("/avatar/parameters/HandPassthroughPreset 1", sent.ToString());
    }

    [Fact]
    public void Fallback_PortInUse_DoesNotThrow()
    {
        using var occupied = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)occupied.Client.LocalEndPoint!).Port;

        using var service = OscService.Start(TestConfigs.Default.Osc with { UseOscQuery = false, FallbackListenPort = port });

        Assert.Null(service.ListenPort);
        service.Send(new OscMessage("/a", 1));
    }

    [Fact]
    public async Task OscQuery_StartsOrFallsBack_AndReceives()
    {
        // OSCQuery（mDNS / HTTP）が使えない環境でも、例外を出さずにフォールバックして受信できること。
        var settings = TestConfigs.Default.Osc with { FallbackListenPort = FreeUdpPort() };
        var log = new System.Collections.Concurrent.ConcurrentQueue<string>();
        ViolinPassthrough.Logging.Log.AddSink(log.Enqueue);

        using var service = OscService.Start(settings);

        Assert.NotNull(service.ListenPort);
        if (OperatingSystem.IsWindows())
        {
            // Windows（実機と同じ環境）では OSCQuery で起動できること。
            Assert.True(service.UsingOscQuery, "OSCQuery did not start on Windows:\n" + string.Join("\n", log.Where(l => l.Contains("OSC"))));
            Assert.NotEqual(settings.FallbackListenPort, service.ListenPort);

            // VRChat が読む HOST_INFO とエンドポイントの木。
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
            var baseUrl = $"http://127.0.0.1:{service.HttpPort}";
            using var hostInfo = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync(baseUrl + "/?HOST_INFO"));
            Assert.Equal(service.ListenPort, hostInfo.RootElement.GetProperty("OSC_PORT").GetInt32());
            Assert.Equal("127.0.0.1", hostInfo.RootElement.GetProperty("OSC_IP").GetString());
            foreach (var (path, type) in new[]
            {
                ("/avatar/change", "s"),
                ("/avatar/parameters/HandPassthrough", "T"),
                ("/avatar/parameters/HandPassthroughPreset", "i"),
                ("/avatar/parameters/HandPassthroughDebug", "T"),
            })
            {
                using var node = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync(baseUrl + path));
                Assert.Equal(type, node.RootElement.GetProperty("TYPE").GetString());
            }
        }

        using var sender = new UdpClient();
        var bytes = OscCodec.Encode(new OscMessage("/avatar/change", "avtr_x"));
        sender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, service.ListenPort!.Value));
        Assert.Equal("/avatar/change avtr_x", WaitForMessage(service).ToString());
    }

    private static OscMessage WaitForMessage(OscService service)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (service.TryReceive(out var message))
            {
                return message;
            }

            Thread.Sleep(10);
        }

        throw new TimeoutException("No OSC message received.");
    }

    private static int FreeUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }
}
