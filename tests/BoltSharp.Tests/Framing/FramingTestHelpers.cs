using System.Buffers;

using BoltSharp.Framing;

namespace BoltSharp.Tests.Framing;

internal static class FramingTestHelpers
{
    /// <summary>Encodes one message with the production writer and returns the wire bytes.</summary>
    internal static byte[] Encode(ReadOnlySpan<byte> message)
    {
        var buffer = new ArrayBufferWriter<byte>();
        new BoltMessageWriter(buffer).WriteMessage(message);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>A deterministic, position-dependent byte pattern of the requested length.</summary>
    internal static byte[] CreatePattern(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)((i * 31) + 7);
        }

        return data;
    }

    internal static byte[] Concat(params byte[][] parts)
    {
        var total = 0;
        foreach (var part in parts)
        {
            total += part.Length;
        }

        var result = new byte[total];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}
