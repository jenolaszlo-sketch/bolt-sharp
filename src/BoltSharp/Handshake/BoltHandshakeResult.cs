namespace BoltSharp.Handshake;

/// <summary>How a Bolt handshake was negotiated.</summary>
public enum BoltHandshakeMode
{
    /// <summary>The legacy four-proposal handshake was used.</summary>
    Legacy,

    /// <summary>The Bolt Manifest v1 handshake was used.</summary>
    ManifestV1,
}

/// <summary>
/// The validated outcome of a Bolt handshake. An instance is only produced after the client's
/// handshake has been fully read and validated, so a returned result never carries partially
/// validated state.
/// <para>
/// <b>Ownership and closure.</b> The negotiator never disposes the supplied stream; the caller
/// owns its lifetime. When <see cref="ShouldClose"/> is <see langword="true"/> (a legacy
/// zero-version rejection) the negotiator has already written the zero response and the caller
/// must close the connection. In the manifest case an invalid or unoffered selection raises an
/// exception instead of returning a result, and the caller must close the connection.
/// </para>
/// </summary>
public sealed class BoltHandshakeResult
{
    private BoltHandshakeResult(
        BoltHandshakeMode mode,
        bool accepted,
        bool shouldClose,
        BoltProtocolVersion? selectedVersion,
        ulong negotiatedCapabilities)
    {
        Mode = mode;
        Accepted = accepted;
        ShouldClose = shouldClose;
        SelectedVersion = selectedVersion;
        NegotiatedCapabilities = negotiatedCapabilities;
    }

    /// <summary>The handshake scheme that was used.</summary>
    public BoltHandshakeMode Mode { get; }

    /// <summary>Whether a protocol version was negotiated.</summary>
    public bool Accepted { get; }

    /// <summary>Whether the caller must close the connection after this result.</summary>
    public bool ShouldClose { get; }

    /// <summary>The selected concrete version, or <see langword="null"/> on rejection.</summary>
    public BoltProtocolVersion? SelectedVersion { get; }

    /// <summary>
    /// The negotiated capability bitmask. Zero for the legacy handshake and for a manifest
    /// handshake when the client selected no capability bits.
    /// </summary>
    public ulong NegotiatedCapabilities { get; }

    internal static BoltHandshakeResult AcceptedLegacy(BoltProtocolVersion version)
    {
        GuardNegotiatedVersion(version);
        return new(BoltHandshakeMode.Legacy, accepted: true, shouldClose: false, version, negotiatedCapabilities: 0);
    }

    internal static BoltHandshakeResult AcceptedManifest(BoltProtocolVersion version, ulong capabilities)
    {
        GuardNegotiatedVersion(version);
        return new(BoltHandshakeMode.ManifestV1, accepted: true, shouldClose: false, version, capabilities);
    }

    internal static BoltHandshakeResult Rejected() =>
        new(BoltHandshakeMode.Legacy, accepted: false, shouldClose: true, selectedVersion: null, negotiatedCapabilities: 0);

    /// <summary>
    /// Fail-closed guard: protocol zero is the "no version" sentinel and must never be published
    /// as accepted state (B2-B-R1, finding B2BQ1-F01).
    /// </summary>
    private static void GuardNegotiatedVersion(BoltProtocolVersion version)
    {
        if (version == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                "Protocol zero is the 'no version' sentinel and cannot be a negotiated version.");
        }
    }
}
