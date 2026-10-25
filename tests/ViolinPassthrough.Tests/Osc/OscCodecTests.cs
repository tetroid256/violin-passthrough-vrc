using ViolinPassthrough.Osc;

namespace ViolinPassthrough.Tests.Osc;

public class OscCodecTests
{
    [Fact]
    public void Encode_BoolMessage_MatchesOscSpec()
    {
        var bytes = OscCodec.Encode(new OscMessage("/avatar/parameters/HandPassthrough", true));

        // "/avatar/parameters/HandPassthrough" は 34 文字 → NUL 込みで 36 バイト、",T" → 4 バイト。
        Assert.Equal(40, bytes.Length);
        Assert.Equal((byte)',', bytes[36]);
        Assert.Equal((byte)'T', bytes[37]);
        Assert.Equal(0, bytes[38]);
        Assert.Equal(0, bytes[39]);
    }

    [Fact]
    public void Encode_IntMessage_IsBigEndian()
    {
        var bytes = OscCodec.Encode(new OscMessage("/a", 258));

        Assert.Equal(new byte[] { (byte)'/', (byte)'a', 0, 0, (byte)',', (byte)'i', 0, 0, 0, 0, 1, 2 }, bytes);
    }

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void RoundTrip(object?[] arguments)
    {
        var message = new OscMessage("/avatar/parameters/X", arguments);

        var parsed = Assert.Single(OscCodec.Parse(OscCodec.Encode(message)));

        Assert.Equal(message.Address, parsed.Address);
        Assert.Equal(arguments, parsed.Arguments);
    }

    public static TheoryData<object?[]> RoundTripCases() => new()
    {
        new object?[] { true },
        new object?[] { false },
        new object?[] { 0 },
        new object?[] { -7 },
        new object?[] { 0.5f },
        new object?[] { "avtr_1234-abcd" },
        new object?[] { "" },
        new object?[] { "abc" },
        new object?[] { "abcd" },
        new object?[] { 1L << 40 },
        new object?[] { 3.25 },
        new object?[] { new byte[] { 1, 2, 3 } },
        new object?[] { null },
        new object?[] { 1, 2.5f, "s", true, false },
        new object?[] { },
    };

    [Fact]
    public void Parse_Bundle_FlattensNestedMessages()
    {
        var a = OscCodec.Encode(new OscMessage("/a", 1));
        var b = OscCodec.Encode(new OscMessage("/b", false));
        var inner = Bundle(b);
        var outer = Bundle(a, inner);

        var messages = OscCodec.Parse(outer);

        Assert.Equal(new[] { "/a 1", "/b False" }, messages.Select(m => m.ToString()));
    }

    [Fact]
    public void Parse_MessageWithoutTypeTags_HasNoArguments()
    {
        var parsed = Assert.Single(OscCodec.Parse(new byte[] { (byte)'/', (byte)'x', 0, 0 }));

        Assert.Equal("/x", parsed.Address);
        Assert.Empty(parsed.Arguments);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { (byte)'/', (byte)'a', 0 })]
    [InlineData(new byte[] { (byte)'/', (byte)'a', (byte)'b', (byte)'c' })]
    [InlineData(new byte[] { (byte)'x', 0, 0, 0 })]
    [InlineData(new byte[] { (byte)'/', 0, 0, 0, (byte)'i', 0, 0, 0 })]
    [InlineData(new byte[] { (byte)'/', 0, 0, 0, (byte)',', (byte)'i', 0, 0 })]
    [InlineData(new byte[] { (byte)'/', 0, 0, 0, (byte)',', (byte)'Q', 0, 0 })]
    [InlineData(new byte[] { (byte)'/', 0, 0, 0, (byte)',', (byte)'b', 0, 0, 0x7f, 0, 0, 0 })]
    [InlineData(new byte[] { (byte)'/', 0, 0, 0, (byte)',', (byte)'b', 0, 0, 0xff, 0xff, 0xff, 0xff })]
    [InlineData(new byte[] { (byte)'#', (byte)'b', (byte)'u', (byte)'n', (byte)'d', (byte)'l', (byte)'e', 0 })]
    [InlineData(new byte[] { (byte)'#', (byte)'b', (byte)'u', (byte)'n', (byte)'d', (byte)'l', (byte)'e', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 99 })]
    [InlineData(new byte[] { (byte)'#', (byte)'b', (byte)'u', (byte)'n', (byte)'d', (byte)'l', (byte)'e', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xff, 0xff, 0xff, 0xfc })]
    public void Parse_MalformedPacket_ThrowsFormatException(byte[] packet)
    {
        Assert.Throws<FormatException>(() => OscCodec.Parse(packet));
    }

    [Fact]
    public void Parse_RandomBytes_NeverThrowsAnythingButFormatException()
    {
        var random = new Random(1234);
        for (var i = 0; i < 20_000; i++)
        {
            var packet = new byte[random.Next(0, 16) * 4];
            random.NextBytes(packet);
            if (packet.Length > 0 && random.Next(2) == 0)
            {
                packet[0] = (byte)'/';
            }

            try
            {
                OscCodec.Parse(packet);
            }
            catch (FormatException)
            {
            }
        }
    }

    [Fact]
    public void Parse_DeeplyNestedBundle_IsRejected()
    {
        var packet = OscCodec.Encode(new OscMessage("/a", 1));
        for (var i = 0; i < 20; i++)
        {
            packet = Bundle(packet);
        }

        Assert.Throws<FormatException>(() => OscCodec.Parse(packet));
    }

    private static byte[] Bundle(params byte[][] elements)
    {
        using var stream = new MemoryStream();
        stream.Write("#bundle\0"u8);
        stream.Write(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });
        foreach (var element in elements)
        {
            var size = BitConverter.GetBytes(element.Length);
            Array.Reverse(size);
            stream.Write(size);
            stream.Write(element);
        }

        return stream.ToArray();
    }
}
