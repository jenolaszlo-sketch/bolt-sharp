// Q-B5B-05-PROBE-01 — official-driver behavior assessment.
//
// The genuine, pinned Neo4j.Driver 6.4.0 is driven against the real BoltSharp listener through
// the producer-owned recording proxy, so the physical connection identity and the exact wire
// sequence are observable. The question Q-B5B-05 asks is answered by observation only: does the
// driver initiate RESET on the same physical connection after a recoverable FAILURE, and if not,
// what does it do instead?
//
// No public driver RESET API is invented or assumed. Where the driver performs recovery
// automatically, the actual wire sequence is recorded. Where a raw protocol path is needed to
// establish server-side RESET handling, the raw-probe scenarios (P-01..P-05) provide that
// evidence separately and it is never presented as official-driver qualification.
using System.Net;

using BoltSharp.Q5B05.Probe.Infra;

using Neo4j.Driver;

using Xunit;

namespace BoltSharp.Q5B05.Probe;

public sealed class DriverResetBehavior
{
    private const string User = "probe-owner-user";
    private const string Secret = "probe-owner-secret";
    private static readonly TimeSpan DriverBound = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan WaitBound = TimeSpan.FromSeconds(10);

    private static async Task<T> DriverBounded<T>(Task<T> task) =>
        await task.WaitAsync(DriverBound);

    private static async Task DriverBounded(Task task) =>
        await task.WaitAsync(DriverBound);

    private static ProxiedConnection? SingleOrNull(RecordingProxy proxy) =>
        proxy.ConnectionCount == 1 ? proxy.Connections()[0] : null;

    private static string DescribeTranscript(ProxiedConnection? connection) =>
        connection is null ? "<not a single proxied connection>" : connection.Sequence();

    private static string WireSummary(RecordingProxy proxy)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var connection in proxy.Connections())
        {
            builder.AppendLine($"physical connection {connection.ConnectionId} ({connection.Index}): " +
                               connection.Sequence());
        }

        return builder.ToString();
    }

    private static string DescribeException(Exception exception)
    {
        var code = exception switch
        {
            Neo4jException neo => $" code={neo.Code} gqlStatus={neo.GqlStatus} " +
                                 $"gqlClassification={neo.GqlClassification} retriable={neo.IsRetriable}",
            _ => string.Empty,
        };
        return $"{exception.GetType().Name}:{code} {exception.Message}".Trim();
    }

    // ---- D-01: the exact Q-B5B-05 IGNORED scenario through the driver -----

    [Fact]
    public async Task D01_Driver_PipelinedRunPull_FailureThenIgnored_RecordsActualWireSequence()
    {
        var providers = new ProbeProviderLog();
        var (server, trace) = await ProbeHost.StartServerAsync(providers);
        await using var _server = server;
        var proxy = RecordingProxy.Start(server.BoundEndPoint!);

        var driver = GraphDatabase.Driver(
            new Uri($"bolt://{proxy.HostPort}"),
            AuthTokens.Basic(User, Secret),
            o => o.WithConnectionTimeout(TimeSpan.FromSeconds(10)));

        string? observed = null;
        try
        {
            await DriverBounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            try
            {
                await DriverBounded(session.RunAsync("MATCH (n) DETACH DELETE n"));
            }
            catch (Exception ex)
            {
                observed = DescribeException(ex);
            }
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await proxy.WaitForConnectionCountAsync(1, WaitBound);
        var refused = SingleOrNull(proxy);

        var document = new System.Text.StringBuilder();
        document.AppendLine("D-01 official driver: unsupported query pipelined with its PULL. The " +
                            "driver's observed classification and the actual wire sequence are " +
                            "recorded; the server answered FAILURE(50N42) then the byte-exact " +
                            "zero-field IGNORED (B0 7E).");
        document.AppendLine($"driver exception: {observed}");
        document.AppendLine($"physical connections observed: {proxy.ConnectionCount}");
        document.AppendLine("proxy transcripts:");
        document.AppendLine(WireSummary(proxy));
        document.AppendLine("server: " + ProbeHost.Summary(server));
        document.AppendLine("RESET messages observed server-side: " +
                            trace.CountProcessedSignatures("0x0F"));
        document.AppendLine("per-connection providers: " + string.Join(", ",
            providers.Providers.Select((p, i) => $"#{i + 1} prepare={p.PrepareCount} " +
                                                 $"success={p.SuccessCount} " +
                                                 $"refused={p.RefusalCount}")));
        document.AppendLine("trace:");
        document.AppendLine(trace.Render());
        ProbeHost.WriteEvidence("d01-driver-failure-ignored-sequence.txt", document.ToString());

        Assert.NotNull(observed);
        Assert.NotNull(refused);
        Assert.True(refused!.CountServer("FAILURE") == 1, "exactly one FAILURE crossed the wire");
        Assert.Contains("B07E", refused!.Messages
            .Where(m => !m.ClientToServer && m.Name == "IGNORED")
            .Select(m => m.Hex));
        Assert.True(refused!.CountClient("RUN") == 1, "exactly one RUN");
        Assert.True(refused!.CountClient("PULL") == 1, "exactly one PULL");
        Assert.True(providers.Providers.Count == 1, "exactly one physical connection");
        Assert.Equal(1, providers.Single?.RefusalCount);
    }

    // ---- D-02: recovery attempt after the recoverable failure ------------

    [Fact]
    public async Task D02_Driver_RecoveryAfterRecoverableFailure_RecordsConnectionDisposition()
    {
        var providers = new ProbeProviderLog();
        var (server, trace) = await ProbeHost.StartServerAsync(providers);
        await using var _server = server;
        var proxy = RecordingProxy.Start(server.BoundEndPoint!);

        var driver = GraphDatabase.Driver(
            new Uri($"bolt://{proxy.HostPort}"),
            AuthTokens.Basic(User, Secret),
            o => o.WithConnectionTimeout(TimeSpan.FromSeconds(10)));

        string? observed = null;
        long? recovered = null;
        try
        {
            await DriverBounded(driver.VerifyConnectivityAsync());
            await using (var session = driver.AsyncSession())
            {
                try
                {
                    await DriverBounded(session.RunAsync("MATCH (n) DETACH DELETE n"));
                }
                catch (Exception ex)
                {
                    observed = DescribeException(ex);
                }
            }

            await using (var session = driver.AsyncSession())
            {
                var cursor = await DriverBounded(session.RunAsync("RETURN 42 AS answer"));
                var record = await DriverBounded(cursor.SingleAsync());
                recovered = record["answer"].As<long>();
                await DriverBounded(cursor.ConsumeAsync());
            }
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await proxy.WaitForConnectionCountAsync(2, WaitBound);
        var refused = proxy.Connections()
            .FirstOrDefault(c => c.CountServer("FAILURE") > 0);
        var recovery = proxy.Connections()
            .FirstOrDefault(c => c.CountClient("RUN") > 0 && c.ConnectionId != refused?.ConnectionId);
        var sameConnectionRecovery = refused is not null && recovery is not null
                                     && refused.ConnectionId == recovery.ConnectionId;
        var resetOnRecovery = recovery is not null
                              && recovery.Messages.Any(m => m.ClientToServer && m.Name == "RESET");

        var document = new System.Text.StringBuilder();
        document.AppendLine("D-02 official driver: a recoverable FAILURE, then a subsequent " +
                            "operation through a new session of the same driver instance. The " +
                            "physical connection disposition is recorded from the producer proxy.");
        document.AppendLine($"driver failure classification: {observed}");
        document.AppendLine($"recovered value: {recovered}");
        document.AppendLine($"physical connections observed: {proxy.ConnectionCount}");
        document.AppendLine($"recovery on the SAME physical connection: {sameConnectionRecovery}");
        document.AppendLine($"RESET observed on the recovery connection: {resetOnRecovery}");
        document.AppendLine($"RESET messages observed server-side: " +
                            $"{trace.CountProcessedSignatures("0x0F")}");
        document.AppendLine("proxy transcripts:");
        document.AppendLine(WireSummary(proxy));
        document.AppendLine("per-connection providers: " + string.Join(", ",
            providers.Providers.Select((p, i) => $"#{i + 1} prepare={p.PrepareCount} " +
                                                 $"success={p.SuccessCount}")));
        document.AppendLine("server: " + ProbeHost.Summary(server));
        document.AppendLine("trace:");
        document.AppendLine(trace.Render());
        ProbeHost.WriteEvidence("d02-driver-recovery-disposition.txt", document.ToString());

        Assert.NotNull(observed);
        Assert.Equal(42L, recovered);
        Assert.NotNull(refused);
        Assert.NotNull(recovery);
    }

    // ---- D-03: a retriable provider failure -----------------------------

    [Fact]
    public async Task D03_Driver_ProviderFailure_Retriable_RecordsWhetherResetIsSent()
    {
        var providers = new ProbeProviderLog();
        var (server, trace) = await ProbeHost.StartServerAsync(providers);
        await using var _server = server;
        var proxy = RecordingProxy.Start(server.BoundEndPoint!);

        var driver = GraphDatabase.Driver(
            new Uri($"bolt://{proxy.HostPort}"),
            AuthTokens.Basic(User, Secret),
            o => o
                .WithConnectionTimeout(TimeSpan.FromSeconds(10))
                .WithMaxTransactionRetryTime(TimeSpan.FromSeconds(5)));

        string? observed = null;
        long recovered = -1;
        try
        {
            // The first physical connection establishes its own provider instance.
            await DriverBounded(driver.VerifyConnectivityAsync());
            var first = providers.Providers.Count == 1 ? providers.Providers[0] : null;
            Assert.NotNull(first);
            first!.ThrowOnNextPrepare = true;

            // Auto-commit read: the driver reaches the provider directly, so the recoverable
            // 50N00 Neo.DatabaseError.General.UnknownError FAILURE is produced. The configured
            // retry time gives the driver the opportunity to recover; the wire sequence is
            // recorded verbatim so the recovery mechanism is observable rather than assumed.
            await using (var session = driver.AsyncSession())
            {
                try
                {
                    var cursor = await DriverBounded(session.RunAsync("RETURN 42 AS answer"));
                    var record = await DriverBounded(cursor.SingleAsync());
                    recovered = record["answer"].As<long>();
                    await DriverBounded(cursor.ConsumeAsync());
                }
                catch (Exception ex)
                {
                    observed = DescribeException(ex);
                }
            }
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await Task.Delay(TimeSpan.FromMilliseconds(200));
        var document = new System.Text.StringBuilder();
        document.AppendLine("D-03 official driver: the provider Prepare of the first physical " +
                            "connection throws, producing the recoverable 50N00 " +
                            "Neo.DatabaseError.General.UnknownError FAILURE, which the driver " +
                            "classifies as a retriable database error. The retry policy is " +
                            "enabled so the driver has the opportunity to recover; the wire " +
                            "sequence is recorded verbatim.");
        document.AppendLine($"driver outcome: {observed ?? ("completed, value=" + recovered)}");
        document.AppendLine($"physical connections observed by the proxy: {proxy.ConnectionCount}");
        document.AppendLine($"RESET messages observed server-side: " +
                            $"{trace.CountProcessedSignatures("0x0F")}");
        document.AppendLine("proxy transcripts:");
        document.AppendLine(WireSummary(proxy));
        document.AppendLine("per-connection providers: " + string.Join(", ",
            providers.Providers.Select((p, i) => $"#{i + 1} prepare={p.PrepareCount} " +
                                                 $"success={p.SuccessCount}")));
        document.AppendLine("server: " + ProbeHost.Summary(server));
        document.AppendLine("trace:");
        document.AppendLine(trace.Render());
        ProbeHost.WriteEvidence("d03-driver-retriable-provider-failure.txt", document.ToString());

        Assert.NotNull(observed);
        Assert.True(proxy.ConnectionCount >= 1, "at least one physical connection was observed");
        Assert.True(providers.Providers.Count >= 1);
    }

    // ---- D-04: pooling interaction --------------------------------------

    [Fact]
    public async Task D04_Driver_PoolingSequence_RecordsPhysicalConnectionsAndResets()
    {
        var providers = new ProbeProviderLog();
        var (server, trace) = await ProbeHost.StartServerAsync(providers);
        await using var _server = server;
        var proxy = RecordingProxy.Start(server.BoundEndPoint!);

        var driver = GraphDatabase.Driver(
            new Uri($"bolt://{proxy.HostPort}"),
            AuthTokens.Basic(User, Secret),
            o => o.WithConnectionTimeout(TimeSpan.FromSeconds(10)));

        var results = new List<long>();
        for (var session_index = 0; session_index < 3; session_index++)
        {
            await using var session = driver.AsyncSession();
            var cursor = await DriverBounded(session.RunAsync("RETURN 42 AS answer"));
            var record = await DriverBounded(cursor.SingleAsync());
            var value = record["answer"].As<long>();
            await DriverBounded(cursor.ConsumeAsync());
            results.Add(value);
        }

        await driver.DisposeAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        var document = new System.Text.StringBuilder();
        document.AppendLine("D-04 official driver: three sequential sessions of one driver " +
                            "instance, recording the number of distinct physical connections " +
                            "and the number of RESET messages observed server-side.");
        document.AppendLine($"values: {string.Join(", ", results)}");
        document.AppendLine($"physical connections observed by the proxy: {proxy.ConnectionCount}");
        document.AppendLine($"RESET messages observed server-side: " +
                            $"{trace.CountProcessedSignatures("0x0F")}");
        document.AppendLine("proxy transcripts:");
        document.AppendLine(WireSummary(proxy));
        document.AppendLine("per-connection providers: " + providers.Providers.Count);
        document.AppendLine("server: " + ProbeHost.Summary(server));
        document.AppendLine("trace:");
        document.AppendLine(trace.Render());
        ProbeHost.WriteEvidence("d04-driver-pooling.txt", document.ToString());

        Assert.Equal(new long[] { 42L, 42L, 42L }, results.ToArray());
        Assert.True(providers.Providers.Count == 1, "exactly one physical connection");
        Assert.Equal(3, providers.Providers[0].SuccessCount);
    }
}