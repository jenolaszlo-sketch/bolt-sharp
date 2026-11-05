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

    /// <summary>Only these direct Bolt schemes are accepted; routing schemes are rejected.</summary>
    private static readonly string[] DirectSchemes = { "bolt", "bolt+s", "bolt+ssc" };

    internal DriverTestSettings()
    {
        Uri = ValidateUri(Environment.GetEnvironmentVariable("BOLTSHARP_DRIVER_URI") ?? DirectBoltUriDefault);
        var environment = ReadEnvironment();
        User = environment.User;
        Password = environment.Password;
        Encryption = environment.Encryption;
        ConnectionTimeoutSeconds = environment.ConnectionTimeoutSeconds;
        ExpectedProtocolVersion = environment.ExpectedProtocolVersion;
    }

    /// <summary>
    /// Creates settings bound to an explicit direct-Bolt endpoint. Added by B0-A-TRACE-01-RECOVERY
    /// for the handshake-probe listener, whose port is OS-assigned and therefore cannot come from
    /// the environment. Validation is identical to the environment path, and the driver construction
    /// call remains the single factory inside <see cref="CreateDriver"/> (B2-C-R1, finding Q1-F01).
    /// </summary>
    internal DriverTestSettings(Uri endpoint)
    {
        Uri = ValidateUri(endpoint.ToString());
        var environment = ReadEnvironment();
        User = environment.User;
        Password = environment.Password;
        Encryption = environment.Encryption;
        ConnectionTimeoutSeconds = environment.ConnectionTimeoutSeconds;
        ExpectedProtocolVersion = environment.ExpectedProtocolVersion;
    }

    private static (string User, string Password, string Encryption, int ConnectionTimeoutSeconds, string ExpectedProtocolVersion) ReadEnvironment()
    {
        var connectionTimeoutSeconds =
            int.TryParse(Environment.GetEnvironmentVariable("BOLTSHARP_DRIVER_TIMEOUT_SECONDS"), out var timeout)
                ? timeout
                : 5;

        return (
            User: Environment.GetEnvironmentVariable("BOLTSHARP_DRIVER_USER") ?? string.Empty,
            Password: Environment.GetEnvironmentVariable("BOLTSHARP_DRIVER_PASSWORD") ?? string.Empty,
            Encryption: Environment.GetEnvironmentVariable("BOLTSHARP_DRIVER_ENCRYPTION") ?? "none",
            ConnectionTimeoutSeconds: connectionTimeoutSeconds,
            ExpectedProtocolVersion:
                Environment.GetEnvironmentVariable("BOLTSHARP_DRIVER_EXPECTED_PROTOCOL") ?? "5.8");
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

    /// <summary>
    /// The single driver-construction path. Because <see cref="Uri"/> was validated at
    /// construction, every path that builds a driver from these settings is constrained to a
    /// direct Bolt endpoint (B2-C-R1, finding Q1-F01).
    /// </summary>
    internal IDriver CreateDriver(IAuthToken? authToken = null) =>
        GraphDatabase.Driver(
            Uri,
            authToken ?? AuthToken,
            o => o.WithConnectionTimeout(TimeSpan.FromSeconds(ConnectionTimeoutSeconds)));

    /// <summary>
    /// Validates a direct Bolt URI (B2-C-R1, finding Q1-F01). Accepts only <c>bolt</c>,
    /// <c>bolt+s</c> and <c>bolt+ssc</c>; rejects routing (<c>neo4j</c>), other unsupported or
    /// malformed schemes, embedded credentials and unexpected URI components. A routing URI is
    /// never silently transformed into a direct URI.
    /// </summary>
    internal static Uri ValidateUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException(
                $"The driver URI '{value}' is not a valid absolute URI.", nameof(value));
        }

        if (Array.IndexOf(DirectSchemes, uri.Scheme) < 0)
        {
            throw new ArgumentException(
                $"The driver URI scheme '{uri.Scheme}' is not supported. Use a direct Bolt scheme " +
                "(bolt://, bolt+s://, bolt+ssc://); routing schemes (neo4j://, neo4j+s://, " +
                "neo4j+ssc://) are not supported by BoltSharp.",
                nameof(value));
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            throw new ArgumentException("The driver URI must specify a host.", nameof(value));
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("The driver URI must not embed credentials.", nameof(value));
        }

        if (uri.AbsolutePath.Length > 1)
        {
            throw new ArgumentException("The driver URI must not contain a path.", nameof(value));
        }

        if (!string.IsNullOrEmpty(uri.Query))
        {
            throw new ArgumentException("The driver URI must not contain a query string.", nameof(value));
        }

        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("The driver URI must not contain a fragment.", nameof(value));
        }

        return uri;
    }
}
