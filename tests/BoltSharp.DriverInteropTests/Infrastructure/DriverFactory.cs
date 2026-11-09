using System.Reflection;
using System.Security.Cryptography.X509Certificates;

using Neo4j.Driver;

namespace BoltSharp.DriverInteropTests.Infrastructure;

/// <summary>
/// Constructs the genuine, pinned official <c>Neo4j.Driver</c> through its documented public API.
/// No transport replacement, no internal fake connection, no protocol interception: every
/// scenario drives the real driver against the real B5-A server.
/// </summary>
internal static class DriverFactory
{
    /// <summary>The exact pinned package identity recorded for this milestone.</summary>
    internal const string PackageIdentity = "Neo4j.Driver 6.4.0 (exact pin)";

    internal static string PackageVersion { get; } =
        typeof(GraphDatabase).Assembly.GetName().Version?.ToString() ?? "unknown";

    internal static string InformationalVersion { get; } =
        typeof(GraphDatabase).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    internal static TimeSpan DefaultBound { get; } = TimeSpan.FromSeconds(20);

    /// <summary>A direct plaintext bolt:// driver (scheme default: no TLS).</summary>
    internal static IDriver Plaintext(string hostPort, IAuthToken? token = null, Action<ConfigBuilder>? configure = null) =>
        Build($"bolt://{hostPort}", token, configure);

    /// <summary>A direct bolt+ssc:// driver (TLS required, any server certificate accepted).</summary>
    internal static IDriver SelfSignedTls(string hostPort, IAuthToken? token = null, Action<ConfigBuilder>? configure = null) =>
        Build($"bolt+ssc://{hostPort}", token, configure);

    /// <summary>A direct bolt+s:// driver (TLS required, system trust store validation).</summary>
    internal static IDriver SystemTrustTls(string hostPort, IAuthToken? token = null, Action<ConfigBuilder>? configure = null) =>
        Build($"bolt+s://{hostPort}", token, configure);

    /// <summary>A routing-scheme driver (neo4j://); expected to fail against BoltSharp.</summary>
    internal static IDriver RoutingScheme(string hostPort, IAuthToken? token = null, Action<ConfigBuilder>? configure = null) =>
        Build($"neo4j://{hostPort}", token, configure);

    /// <summary>
    /// A plain bolt:// URI with TLS enabled through the documented public configuration and the
    /// server certificate validated against an explicit in-memory trust list (an isolated,
    /// reproducible CA-trust arrangement that never touches a machine trust store).
    /// </summary>
    internal static IDriver TrustListTls(string hostPort, X509Certificate2 trustedCa, IAuthToken? token = null) =>
        Build($"bolt://{hostPort}", token, o => o
            .WithEncryptionLevel(EncryptionLevel.Encrypted)
            .WithCertificateTrustRule(CertificateTrustRule.TrustList, new[] { trustedCa }));

    internal static IAuthToken DefaultToken() => AuthTokens.Basic(TestCredentials.User, TestCredentials.Password);

    private static IDriver Build(string uri, IAuthToken? token, Action<ConfigBuilder>? configure) =>
        GraphDatabase.Driver(
            new Uri(uri),
            token ?? DefaultToken(),
            o =>
            {
                o.WithConnectionTimeout(TimeSpan.FromSeconds(10));
                configure?.Invoke(o);
            });

    /// <summary>Bounds any driver operation; a hang is a test failure, never an unbounded wait.</summary>
    internal static async Task<T> Bounded<T>(Task<T> task, TimeSpan? bound = null) =>
        await task.WaitAsync(bound ?? DefaultBound).ConfigureAwait(false);

    internal static async Task Bounded(Task task, TimeSpan? bound = null) =>
        await task.WaitAsync(bound ?? DefaultBound).ConfigureAwait(false);
}
