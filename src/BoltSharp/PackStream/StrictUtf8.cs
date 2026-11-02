using System.Text;

namespace BoltSharp.PackStream;

/// <summary>
/// Strict UTF-8 support. Decoding throws instead of substituting replacement characters
/// for malformed byte sequences, and <see cref="ValidateUtf16"/> rejects invalid UTF-16
/// (unpaired surrogates) before encoding.
/// </summary>
internal static class StrictUtf8
{
    internal static readonly UTF8Encoding Instance =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Throws <see cref="PackStreamFormatException"/> if <paramref name="value"/> contains an
    /// unpaired surrogate. .NET's UTF-8 encoder replaces such characters by default, so this
    /// check is performed explicitly before encoding.
    /// </summary>
    internal static void ValidateUtf16(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                {
                    throw new PackStreamFormatException(
                        $"The string contains an unpaired high surrogate at index {i}.");
                }

                i++; // Consume the matching low surrogate.
            }
            else if (char.IsLowSurrogate(c))
            {
                throw new PackStreamFormatException(
                    $"The string contains an unpaired low surrogate at index {i}.");
            }
        }
    }
}
