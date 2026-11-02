using System.Buffers;

using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.PackStream;

/// <summary>
/// Configuration and enforcement of the string/byte-array safety limits.
/// The wire format allows lengths up to int.MaxValue; the codec caps them.
/// </summary>
public sealed class SizeLimitTests
{
    [Fact]
    public void DefaultLimits_AreSixteenMiB()
    {
        Assert.Equal(16 * 1024 * 1024, PackStreamLimits.Default.MaxStringByteLength);
        Assert.Equal(16 * 1024 * 1024, PackStreamLimits.Default.MaxBytesLength);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(-1, -1)]
    public void NegativeLimits_Throw(int maxString, int maxBytes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PackStreamLimits(maxString, maxBytes));

    [Fact]
    public void Reader_NullLimits_Throw() =>
        Assert.Throws<ArgumentNullException>(() => new PackStreamReader(Array.Empty<byte>(), null!));

    [Fact]
    public void Writer_NullLimits_Throw() =>
        Assert.Throws<ArgumentNullException>(
            () => new PackStreamWriter(new ArrayBufferWriter<byte>(), null!));

    [Fact]
    public void Reader_CustomStringLimit_AllowsEqualLength()
    {
        var limits = new PackStreamLimits(maxStringByteLength: 5, maxBytesLength: 5);
        var reader = new PackStreamReader(new byte[] { 0x85, 0x41, 0x41, 0x41, 0x41, 0x41 }, limits);

        Assert.Equal("AAAAA", reader.ReadString());
    }

    [Fact]
    public void Reader_CustomStringLimit_RejectsLongerLength()
    {
        var limits = new PackStreamLimits(maxStringByteLength: 4, maxBytesLength: 4);
        var reader = new PackStreamReader(new byte[] { 0x85, 0x41, 0x41, 0x41, 0x41, 0x41 }, limits);

        Assert.Throws<PackStreamFormatException>(() => reader.ReadString());
    }

    [Fact]
    public void Reader_TinyString_IsAlsoBoundedByLimit()
    {
        var limits = new PackStreamLimits(maxStringByteLength: 4, maxBytesLength: 4);
        var reader = new PackStreamReader(new byte[] { 0x85, 0x41, 0x41, 0x41, 0x41, 0x41 }, limits);

        Assert.Throws<PackStreamFormatException>(() => reader.ReadString());
    }

    [Fact]
    public void Reader_CustomBytesLimit_RejectsLongerLength()
    {
        var limits = new PackStreamLimits(maxStringByteLength: 4, maxBytesLength: 4);
        var reader = new PackStreamReader(new byte[] { 0xCC, 0x05, 1, 2, 3, 4, 5 }, limits);

        Assert.Throws<PackStreamFormatException>(() => reader.ReadBytes());
    }

    [Fact]
    public void Writer_CustomStringLimit_RejectsLongerValue()
    {
        var writer = new PackStreamWriter(new ArrayBufferWriter<byte>(), new PackStreamLimits(maxStringByteLength: 4));

        Assert.Throws<PackStreamFormatException>(() => writer.WriteString("AAAAA"));
    }

    [Fact]
    public void Writer_CustomBytesLimit_RejectsLongerValue()
    {
        var writer = new PackStreamWriter(new ArrayBufferWriter<byte>(), new PackStreamLimits(maxBytesLength: 4));

        Assert.Throws<PackStreamFormatException>(() => writer.WriteBytes(new byte[5]));
    }

    [Fact]
    public void Writer_RespectsMultiByteUtf8LengthAgainstLimit()
    {
        // Three 2-byte characters = 6 UTF-8 bytes, over a 5-byte limit.
        var writer = new PackStreamWriter(new ArrayBufferWriter<byte>(), new PackStreamLimits(maxStringByteLength: 5));

        Assert.Throws<PackStreamFormatException>(() => writer.WriteString("\u00E9\u00E9\u00E9"));
    }

    [Fact]
    public void Reader_OversizedDeclaredLength_FailsBeforeReadingPayload()
    {
        // Only a 5-byte header is present; the declared 16 MiB + 1 must be rejected as
        // a format error (limit) rather than a truncation error.
        var bytes = new byte[] { 0xCE, 0x01, 0x00, 0x00, 0x01 };
        var reader = new PackStreamReader(bytes);

        Assert.Throws<PackStreamFormatException>(() => reader.ReadBytes());
    }
}
