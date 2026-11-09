using System.Net;

using BoltSharp.Framing;
using BoltSharp.Handshake;
using BoltSharp.Sessions;

namespace BoltSharp.Transport;

/// <summary>
/// Options for the bounded Bolt TCP/TLS transport. Defaults are the secure ones: a plaintext
/// listener binds to the IPv4 loopback address only, TLS is off until explicitly configured, and
/// every timeout and limit is finite.
/// </summary>
public sealed class BoltTcpServerOptions
{
    /// <summary>The address to bind. Defaults to IPv4 loopback; a plaintext listener bound to a
    /// non-loopback address is refused at startup (secure default, see <see cref="Validate"/>).</summary>
    public IPAddress BindAddress { get; init; } = IPAddress.Loopback;

    /// <summary>The port to bind. Defaults to 0 (an ephemeral port, reported via the bound
    /// endpoint) so tests never collide.</summary>
    public int Port { get; init; }

    /// <summary>The listener backlog passed to the socket layer.</summary>
    public int Backlog { get; init; } = 128;

    /// <summary>Maximum number of concurrently active connections. Excess connections are closed
    /// immediately upon acceptance.</summary>
    public int MaxConnections { get; init; } = 100;

    /// <summary>
    /// TLS server configuration, or <see langword="null"/> for a plaintext listener (loopback
    /// only). There is no STARTTLS and no plaintext/TLS detection: the mode is configuration.
    /// </summary>
    public BoltTlsServerOptions? Tls { get; init; }

    /// <summary>
    /// The Bolt handshake configuration. Callers must explicitly opt in to the supported Bolt
    /// revision(s) — <see cref="BoltSharp.Handshake.BoltHandshakeOptions.Default"/> advertises nothing
    /// and rejects every handshake. For this milestone the bounded surface is Bolt 5.8.
    /// </summary>
    public BoltHandshakeOptions HandshakeOptions { get; init; } =
        BoltHandshakeOptions.Create(new[] { new BoltProtocolVersion(5, 8) });

    /// <summary>Chunked-framing limits applied to every connection (receiver-side bounds).</summary>
    public BoltFramingLimits FramingLimits { get; init; } = BoltFramingLimits.Default;

    /// <summary>
    /// The credential verifier handed to each per-connection B3-B session. When
    /// <see langword="null"/> (the default), B3-B's accepted fail-closed behavior applies and
    /// every LOGON is rejected. Tests inject an explicit verifier.
    /// </summary>
    public Sessions.IBoltCredentialVerifier? CredentialVerifier { get; init; }

    /// <summary>
    /// Factory for the per-connection operation provider handed to the B3-B session. Defaults to
    /// the accepted B3-C reference backend over its default graph.
    /// </summary>
    public Func<IBoltOperationProvider>? OperationProviderFactory { get; init; } =
        () => new Backend.ReferenceOperationProvider();

    /// <summary>Bounded time to complete the Bolt handshake after connection acceptance.</summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Bounded time to complete the TLS authentication before any Bolt bytes.</summary>
    public TimeSpan TlsHandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Bounded time to read one complete Bolt message (including NOOP waits between
    /// messages). A read that exceeds the bound closes the connection.</summary>
    public TimeSpan MessageReadTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Bounded time to write one complete framed response. A slow client that stops
    /// reading causes the connection to be closed rather than unbounded buffering.</summary>
    public TimeSpan WriteTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Bounded wait for active connection tasks during shutdown.</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Validates the whole configuration deterministically; throws when unusable or
    /// insecure (plaintext non-loopback binding).</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(BindAddress);
        ArgumentOutOfRangeException.ThrowIfNegative(Port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Port, 65535);
        ArgumentOutOfRangeException.ThrowIfNegative(Backlog);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConnections, 1);
        ThrowIfNonPositive(HandshakeTimeout, nameof(HandshakeTimeout));
        ThrowIfNonPositive(TlsHandshakeTimeout, nameof(TlsHandshakeTimeout));
        ThrowIfNonPositive(MessageReadTimeout, nameof(MessageReadTimeout));
        ThrowIfNonPositive(WriteTimeout, nameof(WriteTimeout));
        ThrowIfNonPositive(ShutdownTimeout, nameof(ShutdownTimeout));
        Tls?.Validate();

        // Secure default: a plaintext listener must never accidentally bind a non-loopback
        // address. A TLS listener may bind any explicitly configured address.
        if (Tls is null && !IsLoopback(BindAddress))
        {
            throw new InvalidOperationException(
                "A plaintext Bolt listener must bind to a loopback address. Configure TLS " +
                "(BoltTlsServerOptions) or bind to the loopback interface.");
        }

        static void ThrowIfNonPositive(TimeSpan value, string name) =>
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);

        static bool IsLoopback(IPAddress address) =>
            IPAddress.Loopback.Equals(address) || IPAddress.IPv6Loopback.Equals(address);
    }
}
