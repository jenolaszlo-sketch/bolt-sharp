using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.PackStream;

/// <summary>
/// Malformed-input behaviour: truncated values throw <see cref="PackStreamTruncatedException"/>
/// and unsupported markers throw <see cref="PackStreamFormatException"/>.
/// </summary>
public sealed class ScalarErrorTests
{
    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xC8 })]
    [InlineData(new byte[] { 0xC9, 0x00 })]
    [InlineData(new byte[] { 0xCA, 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0xCB, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    public void ReadInteger_Truncated_Throws(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamTruncatedException>(bytes, r => r.ReadInteger());

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xC1 })]
    [InlineData(new byte[] { 0xC1, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    public void ReadFloat_Truncated_Throws(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamTruncatedException>(bytes, r => r.ReadFloat());

    [Fact]
    public void ReadNull_OnEmptyInput_Throws() =>
        PackStreamTestHelpers.AssertThrows<PackStreamTruncatedException>(Array.Empty<byte>(), r => r.ReadNull());

    [Fact]
    public void ReadBoolean_OnEmptyInput_Throws() =>
        PackStreamTestHelpers.AssertThrows<PackStreamTruncatedException>(Array.Empty<byte>(), r => r.ReadBoolean());

    [Theory]
    // Null, Float, Boolean markers and container/string markers are not integers.
    [InlineData(new byte[] { 0xC0 })]
    [InlineData(new byte[] { 0xC1 })]
    [InlineData(new byte[] { 0xC2 })]
    [InlineData(new byte[] { 0xC3 })]
    [InlineData(new byte[] { 0x80 })]
    [InlineData(new byte[] { 0x90 })]
    [InlineData(new byte[] { 0xA0 })]
    [InlineData(new byte[] { 0xB0 })]
    [InlineData(new byte[] { 0xC4 })]
    [InlineData(new byte[] { 0xCC })]
    [InlineData(new byte[] { 0xD0 })]
    [InlineData(new byte[] { 0xD4 })]
    [InlineData(new byte[] { 0xD8 })]
    [InlineData(new byte[] { 0xE0 })]
    public void ReadInteger_UnsupportedMarker_ThrowsFormat(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadInteger());

    [Theory]
    [InlineData(new byte[] { 0xC0 })]
    [InlineData(new byte[] { 0x01 })]
    [InlineData(new byte[] { 0xC1 })]
    public void ReadBoolean_UnsupportedMarker_ThrowsFormat(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadBoolean());

    [Theory]
    [InlineData(new byte[] { 0xC3 })]
    [InlineData(new byte[] { 0x00 })]
    public void ReadNull_UnsupportedMarker_ThrowsFormat(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadNull());

    [Theory]
    [InlineData(new byte[] { 0xC0 })]
    [InlineData(new byte[] { 0x00 })]
    [InlineData(new byte[] { 0xC8, 0x2A })]
    public void ReadFloat_UnsupportedMarker_ThrowsFormat(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadFloat());

    [Fact]
    public void UnsupportedMarker_DoesNotAdvanceReader()
    {
        // The marker is consumed, but the failure happens before any payload read,
        // so a truncated payload after an invalid marker is never reached.
        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(
            new byte[] { 0xC1 },
            r => r.ReadInteger());
    }
}
