namespace BoltSharp.Handshake;

/// <summary>
/// Bounds applied by the Bolt handshake layer so that attacker-controlled lengths cannot drive
/// unbounded allocation or I/O.
/// <para>
/// The legacy handshake is a fixed 20 bytes of input, and a Manifest v1 selection adds at most
/// 14 bytes, so the handshake byte budget is small. The manifest version list is the only
/// count-driven allocation in the layer; <see cref="MaxManifestVersions"/> bounds it. A VarInt
/// is independently bounded to ten bytes by <see cref="BoltVarInt.MaxEncodedLength"/>.
/// </para>
/// </summary>
public sealed class BoltHandshakeLimits
{
    /// <summary>The default maximum number of version ranges accepted in a manifest offer (256).</summary>
    public const int DefaultMaxManifestVersions = 256;

    /// <summary>The default maximum number of handshake bytes read in one operation (4,096).</summary>
    public const int DefaultMaxHandshakeBytes = 4_096;

    /// <summary>A shared instance using the default limits.</summary>
    public static BoltHandshakeLimits Default { get; } = new();

    public BoltHandshakeLimits(
        int maxManifestVersions = DefaultMaxManifestVersions,
        int maxHandshakeBytes = DefaultMaxHandshakeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxManifestVersions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHandshakeBytes);

        MaxManifestVersions = maxManifestVersions;
        MaxHandshakeBytes = maxHandshakeBytes;
    }

    /// <summary>
    /// Maximum number of version ranges accepted in a manifest offer before the declared count
    /// is rejected (no allocation is made for a rejected count).
    /// </summary>
    public int MaxManifestVersions { get; }

    /// <summary>
    /// Maximum number of bytes consumed by one handshake read operation before it fails with
    /// <see cref="BoltHandshakeFormatException"/>.
    /// </summary>
    public int MaxHandshakeBytes { get; }
}
