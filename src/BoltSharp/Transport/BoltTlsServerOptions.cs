using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace BoltSharp.Transport;

/// <summary>
/// TLS server-side configuration for the Bolt TCP transport. A TLS listener requires an
/// explicitly supplied server certificate with a usable private key; negotiation failures fail
/// closed (the connection is closed, never downgraded to plaintext).
/// </summary>
public sealed class BoltTlsServerOptions
{
    /// <summary>Creates TLS options from an explicitly supplied server certificate.</summary>
    public BoltTlsServerOptions(X509Certificate2 serverCertificate)
    {
        ServerCertificate = serverCertificate ?? throw new ArgumentNullException(nameof(serverCertificate));
    }

    /// <summary>
    /// The server certificate presented during the TLS handshake. Must carry a private key;
    /// validated at configuration time.
    /// </summary>
    public X509Certificate2 ServerCertificate { get; }

    /// <summary>
    /// Whether a client certificate is requested during the TLS handshake. B3-B authentication
    /// is always enforced independently of TLS client certificates; the default (false) matches
    /// the accepted Bolt behavior where credentials arrive in LOGON.
    /// </summary>
    public bool ClientCertificateRequired { get; init; }

    /// <summary>
    /// The enabled TLS protocol versions. Defaults to TLS 1.2 and TLS 1.3 only; deprecated
    /// SSL/TLS versions cannot be enabled through this surface.
    /// </summary>
    public SslProtocols EnabledProtocols { get; init; } = SslProtocols.Tls12 | SslProtocols.Tls13;

    /// <summary>Validates the TLS configuration deterministically; throws when unusable.</summary>
    public void Validate()
    {
        if (ServerCertificate is null)
        {
            throw new InvalidOperationException(
                "A TLS listener requires an explicitly configured server certificate.");
        }

        if (!ServerCertificate.HasPrivateKey)
        {
            throw new InvalidOperationException(
                "The configured TLS server certificate does not have a usable private key.");
        }

        if (EnabledProtocols == SslProtocols.None)
        {
            throw new InvalidOperationException(
                "At least one TLS protocol version must be enabled (TLS 1.2 or newer).");
        }

        // Defensive: reject configurations that would enable deprecated protocols even if a
        // future framework widens the enum handling.
        if (EnabledProtocols < SslProtocols.Tls12)
        {
            throw new InvalidOperationException(
                "Deprecated SSL/TLS protocol versions must not be enabled.");
        }
    }
}
