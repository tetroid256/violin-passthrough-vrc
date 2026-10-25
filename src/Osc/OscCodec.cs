using System.Buffers.Binary;
using System.Text;

namespace ViolinPassthrough.Osc;

/// <summary>OSC メッセージ。引数は bool / int / float / string / long / double / byte[] / null。</summary>
public sealed record OscMessage(string Address, IReadOnlyList<object?> Arguments)
{
    public OscMessage(string address, params object?[] arguments)
        : this(address, (IReadOnlyList<object?>)arguments)
    {
    }

    public override string ToString() =>
        Arguments.Count == 0 ? Address : $"{Address} {string.Join(" ", Arguments.Select(a => a ?? "nil"))}";
}

/// <summary>
/// OSC 1.0 の最小限のエンコーダ／デコーダ。VRChat とのやり取りに必要な型だけを扱う。
/// </summary>
public static class OscCodec
{
    private const int MaxBundleDepth = 8;
    private static readonly byte[] BundleHeader = "#bundle\0"u8.ToArray();

    /// <summary>パケット（メッセージまたはバンドル）を読む。</summary>
    /// <exception cref="FormatException">壊れたパケット。</exception>
    public static List<OscMessage> Parse(ReadOnlySpan<byte> packet)
    {
        var result = new List<OscMessage>();
        ParseElement(packet, result, 0);
        return result;
    }

    public static byte[] Encode(OscMessage message)
    {
        using var stream = new MemoryStream();
        WriteString(stream, message.Address);

        var tags = new StringBuilder(",");
        foreach (var argument in message.Arguments)
        {
            tags.Append(argument switch
            {
                true => 'T',
                false => 'F',
                int => 'i',
                float => 'f',
                string => 's',
                long => 'h',
                double => 'd',
                byte[] => 'b',
                null => 'N',
                _ => throw new ArgumentException($"Unsupported OSC argument type {argument.GetType()}."),
            });
        }

        WriteString(stream, tags.ToString());

        Span<byte> buffer = stackalloc byte[8];
        foreach (var argument in message.Arguments)
        {
            switch (argument)
            {
                case int i:
                    BinaryPrimitives.WriteInt32BigEndian(buffer, i);
                    stream.Write(buffer[..4]);
                    break;
                case float f:
                    BinaryPrimitives.WriteSingleBigEndian(buffer, f);
                    stream.Write(buffer[..4]);
                    break;
                case string s:
                    WriteString(stream, s);
                    break;
                case long l:
                    BinaryPrimitives.WriteInt64BigEndian(buffer, l);
                    stream.Write(buffer);
                    break;
                case double d:
                    BinaryPrimitives.WriteDoubleBigEndian(buffer, d);
                    stream.Write(buffer);
                    break;
                case byte[] blob:
                    BinaryPrimitives.WriteInt32BigEndian(buffer, blob.Length);
                    stream.Write(buffer[..4]);
                    stream.Write(blob);
                    Pad(stream);
                    break;
            }
        }

        return stream.ToArray();
    }

    private static void ParseElement(ReadOnlySpan<byte> data, List<OscMessage> result, int depth)
    {
        if (data.Length == 0 || data.Length % 4 != 0)
        {
            throw new FormatException($"OSC packet size must be a positive multiple of 4 (got {data.Length}).");
        }

        if (data.StartsWith(BundleHeader))
        {
            if (depth >= MaxBundleDepth)
            {
                throw new FormatException("OSC bundles are nested too deeply.");
            }

            // "#bundle\0" + タイムタグ（8 バイト）の後に、[サイズ, 要素] が続く。
            var offset = 16;
            if (data.Length < offset)
            {
                throw new FormatException("OSC bundle is truncated.");
            }

            while (offset < data.Length)
            {
                var size = ReadInt32(data, ref offset);
                if (size <= 0 || size > data.Length - offset)
                {
                    throw new FormatException($"Invalid OSC bundle element size {size}.");
                }

                ParseElement(data.Slice(offset, size), result, depth + 1);
                offset += size;
            }

            return;
        }

        result.Add(ParseMessage(data));
    }

    private static OscMessage ParseMessage(ReadOnlySpan<byte> data)
    {
        var offset = 0;
        var address = ReadString(data, ref offset);
        if (!address.StartsWith('/'))
        {
            throw new FormatException($"OSC address must start with '/' (got \"{address}\").");
        }

        // 型タグが無い古い形式は、引数なしとして扱う。
        if (offset >= data.Length)
        {
            return new OscMessage(address, Array.Empty<object?>());
        }

        var tags = ReadString(data, ref offset);
        if (!tags.StartsWith(','))
        {
            throw new FormatException("OSC type tag string must start with ','.");
        }

        var arguments = new List<object?>(tags.Length - 1);
        foreach (var tag in tags.AsSpan(1))
        {
            arguments.Add(tag switch
            {
                'T' => true,
                'F' => false,
                'N' or 'I' => null,
                'i' => ReadInt32(data, ref offset),
                'f' => BinaryPrimitives.ReadSingleBigEndian(Take(data, ref offset, 4)),
                's' or 'S' => ReadString(data, ref offset),
                'h' => BinaryPrimitives.ReadInt64BigEndian(Take(data, ref offset, 8)),
                't' => (long)BinaryPrimitives.ReadUInt64BigEndian(Take(data, ref offset, 8)),
                'd' => BinaryPrimitives.ReadDoubleBigEndian(Take(data, ref offset, 8)),
                'c' or 'r' or 'm' => ReadInt32(data, ref offset),
                'b' => ReadBlob(data, ref offset),
                _ => throw new FormatException($"Unsupported OSC type tag '{tag}'."),
            });
        }

        return new OscMessage(address, arguments);
    }

    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> data, ref int offset, int count)
    {
        if (count < 0 || offset > data.Length - count)
        {
            throw new FormatException("OSC message is truncated.");
        }

        var slice = data.Slice(offset, count);
        offset += count;
        return slice;
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, ref int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(Take(data, ref offset, 4));

    private static string ReadString(ReadOnlySpan<byte> data, ref int offset)
    {
        var rest = data[offset..];
        var end = rest.IndexOf((byte)0);
        if (end < 0)
        {
            throw new FormatException("OSC string is not terminated.");
        }

        var value = Encoding.UTF8.GetString(rest[..end]);
        // 終端の NUL を含めて 4 バイト境界まで進める。
        Take(data, ref offset, (end + 4) & ~3);
        return value;
    }

    private static byte[] ReadBlob(ReadOnlySpan<byte> data, ref int offset)
    {
        var size = ReadInt32(data, ref offset);
        var blob = Take(data, ref offset, size).ToArray();
        Take(data, ref offset, (4 - size % 4) % 4);
        return blob;
    }

    private static void WriteString(Stream stream, string value)
    {
        stream.Write(Encoding.UTF8.GetBytes(value));
        stream.WriteByte(0);
        Pad(stream);
    }

    private static void Pad(Stream stream)
    {
        while (stream.Length % 4 != 0)
        {
            stream.WriteByte(0);
        }
    }
}
