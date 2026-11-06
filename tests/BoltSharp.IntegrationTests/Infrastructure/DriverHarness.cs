using Neo4j.Driver;

namespace BoltSharp.IntegrationTests.Infrastructure;

/// <summary>
/// Builds the real, pinned official <c>Neo4j.Driver</c> for direct loopback Bolt connections.
/// Test-only; credentials are never logged or persisted.
/// </summary>
internal static class DriverHarness
{
    internal const string ServerUser = "neo4j";
    internal const string ServerPassword = "integration-secret";

    internal static readonly TimeSpan DefaultBound = TimeSpan.FromSeconds(20);

    internal static Uri DirectBoltUri(int port) => new($"bolt://127.0.0.1:{port}");

    internal static IDriver CreateDriver(int port, IAuthToken? authToken = null)
    {
        var token = authToken ?? AuthTokens.Basic(ServerUser, ServerPassword);
        return GraphDatabase.Driver(
            DirectBoltUri(port),
            token,
            o => o
                .WithEncryptionLevel(EncryptionLevel.None)
                .WithConnectionTimeout(TimeSpan.FromSeconds(10)));
    }

    internal static async Task<T> Bounded<T>(Task<T> task, TimeSpan? bound = null) =>
        await task.WaitAsync(bound ?? DefaultBound).ConfigureAwait(false);

    internal static async Task Bounded(Task task, TimeSpan? bound = null) =>
        await task.WaitAsync(bound ?? DefaultBound).ConfigureAwait(false);
}
