namespace BoltSharp.Handshake;

/// <summary>
/// The server side of a Bolt handshake: the concrete protocol versions this server will select
/// or offer, whether Manifest v1 is answered, and the capability bits offered in a manifest
/// handshake.
/// <para>
/// <b>Version honesty.</b> <see cref="Default"/> declares no supported version and disables
/// Manifest v1, so by default the negotiator advertises nothing and rejects every handshake.
/// This is deliberate: the B0-A compatibility target is provisional, and this layer implements
/// handshake negotiation only, not the Bolt messaging protocol. Advertising a version such as
/// Bolt 5.8 is an explicit opt-in, used by tests and by later milestones once the required
/// message-processing capabilities exist. Listing a version here is not a claim that the
/// corresponding messaging protocol is implemented.
/// </para>
/// </summary>
public sealed class BoltHandshakeOptions
{
    /// <summary>
    /// Options that declare no supported version and disable Manifest v1. A handshake against
    /// these options is always rejected with a zero-version response.
    /// </summary>
    public static BoltHandshakeOptions Default { get; } =
        new(Array.Empty<BoltProtocolVersion>(), manifestV1Enabled: false, offeredCapabilities: 0);

    public BoltHandshakeOptions(
        IEnumerable<BoltProtocolVersion> supportedVersions,
        bool manifestV1Enabled,
        ulong offeredCapabilities = 0)
    {
        ArgumentNullException.ThrowIfNull(supportedVersions);

        var distinct = new List<BoltProtocolVersion>();
        foreach (var version in supportedVersions)
        {
            if (!distinct.Contains(version))
            {
                distinct.Add(version);
            }
        }

        distinct.Sort();
        distinct.Reverse();

        SupportedVersions = distinct.AsReadOnly();
        ManifestV1Enabled = manifestV1Enabled;
        OfferedCapabilities = offeredCapabilities;
    }

    /// <summary>
    /// The concrete versions this server supports, de-duplicated and ordered by major then
    /// minor descending. May be empty.
    /// </summary>
    public IReadOnlyList<BoltProtocolVersion> SupportedVersions { get; }

    /// <summary>
    /// Whether the server answers a Manifest v1 request. Effective only when
    /// <see cref="SupportedVersions"/> is non-empty; a manifest request is otherwise ignored.
    /// </summary>
    public bool ManifestV1Enabled { get; }

    /// <summary>The capability bitmask offered in a Manifest v1 handshake.</summary>
    public ulong OfferedCapabilities { get; }

    /// <summary>
    /// Creates options from a set of supported versions, enabling Manifest v1 by default.
    /// </summary>
    public static BoltHandshakeOptions Create(
        IEnumerable<BoltProtocolVersion> supportedVersions,
        bool manifestV1Enabled = true,
        ulong offeredCapabilities = 0) =>
        new(supportedVersions, manifestV1Enabled, offeredCapabilities);
}
