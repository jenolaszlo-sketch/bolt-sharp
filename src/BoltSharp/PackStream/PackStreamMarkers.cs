namespace BoltSharp.PackStream;

/// <summary>
/// PackStream 1.0 marker bytes for the scalar types covered by B1-A and B1-B.
/// See the PackStream specification, "Data types".
/// </summary>
internal static class PackStreamMarkers
{
    internal const byte Null = 0xC0;
    internal const byte Float64 = 0xC1;
    internal const byte False = 0xC2;
    internal const byte True = 0xC3;
    internal const byte Int8 = 0xC8;
    internal const byte Int16 = 0xC9;
    internal const byte Int32 = 0xCA;
    internal const byte Int64 = 0xCB;

    // Bytes: no tiny representation; 8/16/32-bit unsigned lengths.
    internal const byte Bytes8 = 0xCC;
    internal const byte Bytes16 = 0xCD;
    internal const byte Bytes32 = 0xCE;

    // Strings: TINY_STRING (0x80-0x8F) plus 8/16/32-bit unsigned lengths.
    internal const byte TinyStringBase = 0x80;
    internal const byte String8 = 0xD0;
    internal const byte String16 = 0xD1;
    internal const byte String32 = 0xD2;

    // Tiny integers occupy the single-byte ranges 0x00-0x7F (0..127) and
    // 0xF0-0xFF (-16..-1); the marker byte is the two's-complement value.
    internal const byte TinyIntegerMax = 0x7F;
    internal const byte TinyIntegerMin = 0xF0;

    internal static bool IsTinyInteger(byte marker) =>
        marker <= TinyIntegerMax || marker >= TinyIntegerMin;

    // TINY_STRING markers are 0x80-0x8F; the low nibble is the byte length.
    internal static bool IsTinyString(byte marker) =>
        (marker & 0xF0) == TinyStringBase;
}
