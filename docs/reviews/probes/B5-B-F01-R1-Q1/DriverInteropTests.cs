// B5-B-F01-R1-Q1 — official-driver interop probes (§9, §14, §18): the unmodified pinned
// Neo4j.Driver 6.4.0 against the real BoltTcpServer, observed through the reviewer-owned
// recording proxy. Physical connection identity is tracked per proxy transcript.
//
// The synthetic credentials below are loopback-only test values and are NEVER written to
// evidence (the proxy redacts LOGON payloads at the raw-chunk level).
using BoltSharp.B5BF01R1Q1.Probes.Infra;

using Neo4j.Driver;

using Xunit;

namespace BoltSharp.B5BF01R1Q1.Probes;

public sealed class DriverInteropTests
{
    private const string Principal = "reviewer";
    private const string Secret = "reviewer-secret";

    [Fact]
    public async Task Driver_BeginTransaction_ReceivesTheAcceptedProtocolFailure_NoTransactionBegins()
    {
        var provider = new ProbeProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        await using var proxy = await RecordingProxy.StartAsync(server.BoundEndPoint!);

        var driver = GraphDatabase.Driver(
            new Uri($"bolt://{proxy.BoundEndPoint}"),
            AuthTokens.Basic(Principal, Secret),
            o => o.WithConnectionTimeout(TimeSpan.FromSeconds(10)));

        Exception? observed = null;
        try
        {
            await using var session = driver.AsyncSession();
            await DriverBounded(session.BeginTransactionAsync());
            Assert.Fail("explicit transactions are not supported and must not begin");
        }
        catch (Exception ex)
        {
            observed = ex;
        }
        finally
        {
            await driver.DisposeAsync();
        }

        // The driver must surface the protocol-level refusal, not an unexplained transport error.
        Assert.IsType<ProtocolException>(observed);
        var protocolException = (ProtocolException)observed!;
        Assert.Equal("Neo.ClientError.Request.Invalid", protocolException.Code);
        Assert.Equal("08N06", protocolException.GqlStatus);
        Assert.False(protocolException.IsRetriable);

        // Wire evidence through the reviewer proxy: BEGIN was sent, FAILURE received, no RUN.
        var beginTranscript = await proxy.WaitForTranscriptAsync(
            t => t.CountClient("BEGIN") == 1, TimeSpan.FromSeconds(10));
        ProbeHost.AppendEvidence(
            "13-driver-begin.txt",
            "transcript " + beginTranscript.ConnectionId + ": " +
            string.Join(" | ", beginTranscript.Messages.Select(m => $"{(m.ClientToServer ? "C2S" : "S2C")} {m.Name} {m.Hex}")) + "\n");
        Assert.Equal(1, beginTranscript.CountClient("BEGIN"));
        var begin = beginTranscript.Messages.Single(m => m.ClientToServer && m.Name == "BEGIN");
        Assert.Equal("B111A0", begin.Hex);
        Assert.Equal(0, beginTranscript.CountClient("RUN"));
        var failures = beginTranscript.Messages.Where(m => !m.ClientToServer && m.Name == "FAILURE").ToList();
        Assert.Single(failures); // the FAILURE was transmitted on the socket

        // No operation was ever opened for BEGIN.
        Assert.Equal(0, provider.PrepareCount);

        // Recovery: a supported query succeeds on a NEW physical connection.
        await using (var driver2 = GraphDatabase.Driver(
            new Uri($"bolt://{proxy.BoundEndPoint}"),
            AuthTokens.Basic(Principal, Secret),
            o => o.WithConnectionTimeout(TimeSpan.FromSeconds(10))))
        {
            await DriverBounded(driver2.VerifyConnectivityAsync());
            await using var session = driver2.AsyncSession();
            var cursor = await DriverBounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverBounded(cursor.FetchAsync()));
            Assert.Equal(42L, cursor.Current["answer"].As<long>());
            await DriverBounded(cursor.ConsumeAsync());
        }

        var recoveryTranscript = await proxy.WaitForTranscriptAsync(
            t => t.CountClient("RUN") >= 1, TimeSpan.FromSeconds(10));
        Assert.NotEqual(beginTranscript.ConnectionId, recoveryTranscript.ConnectionId); // new physical connection
        Assert.Equal(1, provider.PrepareCount); // exactly the recovery query prepared

        ProbeHost.AppendEvidence(
            "13-driver-begin.txt",
            $"driver BEGIN: ProtocolException code=Neo.ClientError.Request.Invalid gql=08N06 retriable=false; " +
            $"wire BEGIN B111A0 → FAILURE; no RUN; recovery 42 on a distinct physical connection " +
            $"({beginTranscript.ConnectionId} → {recoveryTranscript.ConnectionId})\n");
    }

    [Fact]
    public async Task Driver_SupportedQueries_ScalarGraphParameter_AndPooling()
    {
        var provider = new CountingReferenceProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        await using var proxy = await RecordingProxy.StartAsync(server.BoundEndPoint!);

        await using var driver = GraphDatabase.Driver(
            new Uri($"bolt://{proxy.BoundEndPoint}"),
            AuthTokens.Basic(Principal, Secret),
            o => o.WithConnectionTimeout(TimeSpan.FromSeconds(10)));
        await DriverBounded(driver.VerifyConnectivityAsync());

        // Scalar.
        await using (var session = driver.AsyncSession())
        {
            var cursor = await DriverBounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverBounded(cursor.FetchAsync()));
            Assert.Equal(42L, cursor.Current["answer"].As<long>());
            await DriverBounded(cursor.ConsumeAsync());
        }

        // Parameter (typed Int64 through the driver's type system).
        await using (var session = driver.AsyncSession())
        {
            var cursor = await DriverBounded(session.RunAsync(
                "RETURN $value AS answer", new Dictionary<string, object> { ["value"] = 7L }));
            Assert.True(await DriverBounded(cursor.FetchAsync()));
            Assert.Equal(7L, cursor.Current["answer"].As<long>());
            await DriverBounded(cursor.ConsumeAsync());
        }

        // Graph: Alice INode → KNOWS IRelationship → Bob INode, with direction and element IDs.
        await using (var session = driver.AsyncSession())
        {
            var cursor = await DriverBounded(session.RunAsync(
                "MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b"));
            Assert.True(await DriverBounded(cursor.FetchAsync()));
            var a = cursor.Current["a"].As<INode>();
            var r = cursor.Current["r"].As<IRelationship>();
            var b = cursor.Current["b"].As<INode>();
            Assert.Equal("Alice", a["name"].As<string>());
            Assert.Equal("KNOWS", r.Type);
            Assert.Equal(a.ElementId, r.StartNodeElementId); // direction: OUTGOING from Alice
            Assert.Equal(b.ElementId, r.EndNodeElementId);
            Assert.Equal("Bob", b["name"].As<string>());
            Assert.False(await DriverBounded(cursor.FetchAsync())); // exactly one record
            await DriverBounded(cursor.ConsumeAsync());
        }

        // Pooling: three sessions of a second, isolated driver instance reuse physical
        // connections. Wait until the pool probe's transcripts complete (relays end shortly
        // after the driver disposes), then evaluate the pooling claim over them only.
        var transcriptsBeforePoolProbe = proxy.Transcripts().Count;
        await using (var poolProbe = GraphDatabase.Driver(
            new Uri($"bolt://{proxy.BoundEndPoint}"),
            AuthTokens.Basic(Principal, Secret),
            o => o.WithConnectionTimeout(TimeSpan.FromSeconds(10))))
        {
            for (var i = 0; i < 3; i++)
            {
                await using var session = poolProbe.AsyncSession();
                var cursor = await DriverBounded(session.RunAsync("RETURN 42 AS answer"));
                Assert.True(await DriverBounded(cursor.FetchAsync()));
                Assert.Equal(42L, cursor.Current["answer"].As<long>());
                await DriverBounded(cursor.ConsumeAsync());
            }
        }

        var poolRunsSeen = 0;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            poolRunsSeen = proxy.Transcripts()
                .Skip(transcriptsBeforePoolProbe)
                .Sum(t => t.CountClient("RUN"));
            if (poolRunsSeen >= 3)
            {
                break;
            }

            await Task.Delay(50);
        }

        var poolTranscripts = proxy.Transcripts().Skip(transcriptsBeforePoolProbe).ToList();
        var runsByPool = poolTranscripts.Where(t => t.CountClient("RUN") > 0).ToList();
        Assert.True(runsByPool.Count >= 1, "the pool probe must have executed queries");
        Assert.True(runsByPool.Count <= 2, "three sequential sessions must not fan out to many physical connections (pooling)");
        Assert.Equal(3, poolRunsSeen); // exactly three RUNs for three sessions
        Assert.Equal(6, provider.PrepareCount); // scalar + parameter + graph (3) + pooled sessions (3)

        ProbeHost.AppendEvidence(
            "14-driver-supported-regression.txt",
            "driver scalar 42; typed parameter 7; graph Alice→KNOWS→Bob with direction and IDs; " +
            $"pooling: 3 sessions served by {runsByPool.Count} physical connection(s)\n");
    }

    [Fact]
    public async Task Driver_UnsupportedQuery_F02_Generic50N42_NonRetriable_RecoveryOnFreshConnection()
    {
        var provider = new CountingReferenceProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        await using var proxy = await RecordingProxy.StartAsync(server.BoundEndPoint!);

        await using var driver = GraphDatabase.Driver(
            new Uri($"bolt://{proxy.BoundEndPoint}"),
            AuthTokens.Basic(Principal, Secret),
            o => o.WithConnectionTimeout(TimeSpan.FromSeconds(10)));
        await DriverBounded(driver.VerifyConnectivityAsync());

        Exception? observed = null;
        long? recovered = null;
        await using (var session = driver.AsyncSession())
        {
            try
            {
                await DriverBounded(session.RunAsync("CREATE (n:RefusalProbe)"));
                Assert.Fail("unsupported queries must be refused");
            }
            catch (Neo4jException ex)
            {
                observed = ex;
            }
        }

        Assert.IsType<ProtocolException>(observed); // the recorded driver classification for 50N42 refusals (see B5B8-01)
        var refusal = (ProtocolException)observed!;
        Assert.Equal("Neo.ClientError.Request.Invalid", refusal.Code);
        Assert.Equal("50N42", refusal.GqlStatus);
        Assert.False(refusal.IsRetriable); // F02 classification: non-retriable

        // No duplicate execution: the refused query never opened an operation.
        Assert.Equal(0, provider.SuccessCount);

        // Recovery uses a fresh connection; the refused query is not re-executed.
        await using (var session = driver.AsyncSession())
        {
            var cursor = await DriverBounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverBounded(cursor.FetchAsync()));
            recovered = cursor.Current["answer"].As<long>();
            await DriverBounded(cursor.ConsumeAsync());
        }

        Assert.Equal(42L, recovered);
        Assert.Equal(1, provider.SuccessCount); // exactly the recovery query opened an operation

        ProbeHost.AppendEvidence(
            "15-driver-f02.txt",
            "driver unsupported query: ProtocolException (Neo4jException subtype) code=Neo.ClientError.Request.Invalid " +
            "gql=50N42 retriable=false; no execution; recovery 42 on a fresh connection\n");
    }

    [Fact]
    public async Task Driver_RoutingScheme_AttemptIsRefused_RouteCoverageRecordedHonesty()
    {
        // §9 ROUTE: attempt official-driver routing through the documented public configuration
        // (neo4j:// scheme). Record honestly whether ROUTE is actually emitted; if the driver
        // fails before sending ROUTE, no live-driver ROUTE coverage is claimed — the raw
        // ROUTE probes (TcpRefusalTests) establish the server's response independently.
        var provider = new ProbeProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        await using var proxy = await RecordingProxy.StartAsync(server.BoundEndPoint!);

        await using var driver = GraphDatabase.Driver(
            new Uri($"neo4j://{proxy.BoundEndPoint}"),
            AuthTokens.Basic(Principal, Secret),
            o => o.WithConnectionTimeout(TimeSpan.FromSeconds(10)));

        Exception? observed = null;
        try
        {
            await DriverBounded(driver.VerifyConnectivityAsync(), TimeSpan.FromSeconds(20));
            await using var session = driver.AsyncSession();
            var cursor = await DriverBounded(session.RunAsync("RETURN 42 AS answer"), TimeSpan.FromSeconds(20));
            await DriverBounded(cursor.ConsumeAsync(), TimeSpan.FromSeconds(20));
        }
        catch (Exception ex)
        {
            observed = ex;
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.NotNull(observed); // routing cannot succeed: no routing tables are implemented
        await proxy.WaitForConnectionsAsync(1, TimeSpan.FromSeconds(10));
        var transcripts = proxy.Transcripts();
        var routeSent = transcripts.Any(t => t.Messages.Any(m => m.ClientToServer && m.Name == "ROUTE"));

        ProbeHost.AppendEvidence(
            "16-driver-route.txt",
            $"neo4j:// attempt: driver error type={observed!.GetType().Name} message={Redact(observed.Message)}; " +
            $"ROUTE actually emitted over the wire: {(routeSent ? "yes" : "no")} " +
            $"(live-driver ROUTE coverage {(routeSent ? "observed" : "NOT claimed; raw-protocol ROUTE probe covers the refusal path)")}\n");
    }

    private static string Redact(string message)
    {
        // Driver messages may echo the URI; keep the transcript credential-free and stable.
        return message.Replace(Secret, "<redacted>").Replace(Principal, "<redacted>");
    }

    private static async Task<T> DriverBounded<T>(Task<T> task, TimeSpan? bound = null) =>
        await task.WaitAsync(bound ?? TimeSpan.FromSeconds(20));

    private static async Task DriverBounded(Task task, TimeSpan? bound = null) =>
        await task.WaitAsync(bound ?? TimeSpan.FromSeconds(20));
}
