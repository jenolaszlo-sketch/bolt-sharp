using System.Buffers;

using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.PackStream;

internal static class PackStreamTestHelpers
{
    internal static byte[] Encode(Action<PackStreamWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        write(new PackStreamWriter(buffer));
        return buffer.WrittenSpan.ToArray();
    }

    internal static T Decode<T>(byte[] bytes, Func<PackStreamReader, T> read) =>
        read(new PackStreamReader(bytes));

    internal static void AssertThrows<TException>(byte[] bytes, Action<PackStreamReader> read)
        where TException : Exception
    {
        var reader = new PackStreamReader(bytes);
        Assert.Throws<TException>(() => read(reader));
    }
}
