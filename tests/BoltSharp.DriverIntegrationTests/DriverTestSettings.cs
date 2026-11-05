using Neo4j.Driver;

namespace BoltSharp.DriverIntegrationTests;

/// <summary>
/// Configuration for the gated official-driver integration scaffold (B2-C-01).
/// <para>
/// No production credentials are hardcoded. Every value comes from an environment variable with a
/// loopback-safe default, so the scaffold can be pointed at a future B5 TCP/TLS listener without
/// editing code. The default is a <b>direct</b> Bolt URI; routing schemes (<c>neo4j://</c>,
/// <c>neo4j+s://</c>) are rejected because BoltSharp has no routing/cluster semantics.
/// </para>
/// </summary>
internal sealed class DriverTestSettings
{
    internal const string DirectBoltUriDefault = "bolt://127.0.0.1:7687";

    internal DriverTestSettings()
    {
        Uri = new Uri(Environment.GetEnvironmentVariable("BOLTSHARP_DRIVER_URI") ?? DirectBoltUriDefault);
        User = Environment.GetEnvironmentVariable("BOLTSHARP_DRIVER_USER") ?? string.Empty;
        Password = Environment.GetEnvironmentVariable("BOLTSHARP_DRIVER_PASSWORD") ?? string.Empty;
        Encryption = Environment.GetEnvironmentVariable("BOLTSHARP_DRIVER_ENCRYPTION") ?? "none";
        ConnectionTimeoutSeconds =
            int.TryParse(Environment.GetEnvironmentVariable("BOLTSHARP_DRIVER_TIMEOUT_SECONDS"), out var timeout)
                ? timeout
                : 5;
        ExpectedProtocolVersion =
            Environment.GetEnvironmentVariable("BOLTSHARP_DRIVER_EXPECTED_PROTOCOL") ?? "5.8";
    }

    /// <summary>Direct Bolt endpoint. Defaults to loopback.</summary>
    internal Uri Uri { get; }

    /// <summary>Basic-auth principal, or empty for no authentication.</summary>
    internal string User { get; }

    /// <summary>Basic-auth password. Never logged.</summary>
    internal string Password { get; }

    /// <summary>Declared encryption mode (<c>none</c>, <c>required</c>); informational for the run.</summary>
    internal string Encryption { get; }

    /// <summary>Connection timeout in seconds.</summary>
    internal int ConnectionTimeoutSeconds { get; }

    /// <summary>Provisionally expected negotiated Bolt revision (B0-A remains provisional).</summary>
    internal string ExpectedProtocolVersion { get; }

    /// <summary>True when the configured URI uses a routing (cluster-discovery) scheme.</summary>
    internal bool UsesRoutingScheme => Uri.Scheme.StartsWith("neo4j", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the configured URI requests TLS (<c>+s</c> / <c>+ssc</c>).</summary>
    internal bool UsesTls => Uri.Scheme.Contains("+s", StringComparison.OrdinalIgnoreCase);

    /// <summary>Driver auth token derived from the configured credentials (never logged).</summary>
    internal IAuthToken AuthToken =>
        string.IsNullOrEmpty(User) ? AuthTokens.None : AuthTokens.Basic(User, Password);
}
