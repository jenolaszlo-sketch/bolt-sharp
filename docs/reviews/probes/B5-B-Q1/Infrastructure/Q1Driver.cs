using System.Reflection;
using System.Security.Cryptography.X509Certificates;

using Neo4j.Driver;

namespace BoltSharp.B5BQ1.Probes.Infrastructure;

/// <summary>
/// Reviewer driver construction through the documented public API only. No transport replacement,
/// no fake connection, no protocol interception. Every driver operation runs under a bounded wait;
/// a hang fails the probe rather than waiting forever.
/// </summary>
internal static class Q1Driver
{
    internal static string PackageVersion { get; } =
        typeof(GraphDatabase).Assembly.GetName().Version?.ToString() ?? "unknown";

    internal static string InformationalVersion { get; } =
        typeof(GraphDatabase).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    internal static TimeSpan Bound { get; } = TimeSpan.FromSeconds(30);

    internal static IDriver Plaintext(string hostPort, IAuthToken? token = null, Action<ConfigBuilder>? configure = null) =>
        Build($"bolt://{hostPort}", token, configure);

    internal static IDriver SelfSignedTls(string hostPort, IAuthToken? token = null, Action<ConfigBuilder>? configure = null) =>
        Build($"bolt+ssc://{hostPort}", token, configure);

    internal static IDriver SystemTrustTls(string hostPort, IAuthToken? token = null, Action<ConfigBuilder>? configure = null) =>
        Build($"bolt+s://{hostPort}", token, configure);

    internal static IDriver RoutingScheme(string hostPort, IAuthToken? token = null, Action<ConfigBuilder>? configure = null) =>
        Build($"neo4j://{hostPort}", token, configure);

    internal static IDriver TrustListTls(string hostPort, X509Certificate2 trustedCa, IAuthToken? token = null) =>
        Build($"bolt://{hostPort}", token, o =>
        {
            o.WithEncryptionLevel(EncryptionLevel.Encrypted);
            o.WithCertificateTrustRule(CertificateTrustRule.TrustList, new[] { trustedCa });
        });

    internal static IAuthToken DefaultToken() =>
        AuthTokens.Basic(Q1Credentials.User, Q1Credentials.Password);

    internal static IAuthToken WrongToken() =>
        AuthTokens.Basic(Q1Credentials.User, "definitely-wrong-" + Guid.NewGuid().ToString("N"));

    private static IDriver Build(string uri, IAuthToken? token, Action<ConfigBuilder>? configure) =>
        GraphDatabase.Driver(
            new Uri(uri),
            token ?? DefaultToken(),
            o =>
            {
                o.WithConnectionTimeout(TimeSpan.FromSeconds(10));
                o.WithConnectionAcquisitionTimeout(TimeSpan.FromSeconds(10));
                configure?.Invoke(o);
            });

    internal static async Task<T> Bounded<T>(Task<T> task, TimeSpan? bound = null) =>
        await task.WaitAsync(bound ?? Bound);

    internal static async Task Bounded(Task task, TimeSpan? bound = null) =>
        await task.WaitAsync(bound ?? Bound);

    internal static async Task Bounded(ValueTask task, TimeSpan? bound = null)
    {
        var completion = new TaskCompletionSource();
        _ = task.AsTask().ContinueWith(
            t =>
            {
                if (t.IsFaulted)
                {
                    completion.SetException(t.Exception!.InnerExceptions);
                }
                else if (t.IsCanceled)
                {
                    completion.SetCanceled();
                }
                else
                {
                    completion.SetResult();
                }
            });
        await completion.Task.WaitAsync(bound ?? Bound);
    }

    /// <summary>Classification of any exception observed through the driver's public API.</summary>
    internal static object ExceptionDocument(Exception exception)
    {
        var neo4jException = exception as Neo4jException;
        var document = new Dictionary<string, object?>
        {
            ["type"] = exception.GetType().FullName,
            ["message"] = exception.Message,
            ["neo4j_code"] = neo4jException?.Code,
            ["gql_status"] = neo4jException?.GqlStatus,
            ["gql_classification"] = neo4jException?.GqlClassification,
            ["is_retriable"] = neo4jException?.IsRetriable,
        };
        if (exception.InnerException is not null)
        {
            document["inner_type"] = exception.InnerException.GetType().FullName;
            document["inner_message"] = exception.InnerException.Message;
        }

        return document;
    }
}
