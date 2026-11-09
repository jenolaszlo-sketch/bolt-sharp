using BoltSharp.DriverInteropTests.Infrastructure;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.DriverInteropTests.Scenarios;

/// <summary>
/// B5-B6 — cursor completion, bounded pagination through the driver's fetch size, DISCARD on
/// partial consumption, result retirement and connection reuse. Successful exhaustion and reuse
/// are mandatory for the B5-B success claim.
/// </summary>
[Collection("B5-B")]
public sealed class CursorLifecycleTests
{
    [Fact]
    [Trait("Task", "B5-B6")]
    public async Task B5B6_01_MultipleRecords_AllDeliveredInOnePull()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(tap.HostPort);
        var names = new List<string>();
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            var cursor = await DriverFactory.Bounded(
                session.RunAsync("MATCH (n:Person) RETURN n.name AS name ORDER BY name"));
            while (await DriverFactory.Bounded(cursor.FetchAsync()))
            {
                names.Add(cursor.Current["name"].As<string>());
            }

            await DriverFactory.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.Equal(new[] { "Alice", "Bob" }, names);

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var connection = tap.SingleConnection();
        var transcript = WireTranscript.Build(connection);
        Assert.Equal(1, transcript.CountClient("PULL"));
        Assert.Equal(2, transcript.CountServer("RECORD"));
        // Both records arrive in the single default-size pass; the terminal summary is SUCCESS {}
        // with no has_more field.
        Assert.DoesNotContain(
            transcript.ServerMessages,
            m => m.Description.StartsWith("SUCCESS {has_more", StringComparison.Ordinal));
        Assert.Equal("B170A0", transcript.ServerMessages.Last().Hex);

        EvidenceWriter.Write("b5b6-01-multiple-records", new
        {
            scenario = "B5B6-01",
            title = "Two-record result fully delivered and exhausted",
            uri_scheme = "bolt",
            driver_visible = new { field = "name", values = names },
            transcript = transcript.ToDocument(connection),
            counters = EvidenceWriter.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B6")]
    public async Task B5B6_02_FiniteFetchSize_DrivesHasMorePagination()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(tap.HostPort);
        var names = new List<string>();
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using (var session = driver.AsyncSession(o => o.WithFetchSize(1)))
            {
                var cursor = await DriverFactory.Bounded(session.RunAsync("MATCH (n:Person) RETURN n.name"));
                while (await DriverFactory.Bounded(cursor.FetchAsync()))
                {
                    names.Add(cursor.Current["n.name"].As<string>());
                }

                await DriverFactory.Bounded(cursor.ConsumeAsync());
            }

            // Connection reuse after clean exhaustion (same driver, new session).
            await using (var session = driver.AsyncSession())
            {
                var cursor = await DriverFactory.Bounded(session.RunAsync("RETURN 42 AS answer"));
                Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
                Assert.Equal(42, cursor.Current["answer"].As<long>());
                await DriverFactory.Bounded(cursor.ConsumeAsync());
            }
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.Equal(new[] { "Alice", "Bob" }, names);

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var connection = tap.SingleConnection();
        var transcript = WireTranscript.Build(connection);

        // Fetch size 1: the driver issues one PULL per record; the server answers the first with
        // has_more=true and the second with the terminal SUCCESS {}.
        var pulls = transcript.ClientMessages.Where(m => m.Name == "PULL").ToArray();
        Assert.Equal(3, pulls.Length);
        Assert.Equal("PULL n=1", pulls[0].Description);
        Assert.Equal("PULL n=1", pulls[1].Description);
        Assert.Equal("PULL n=1000", pulls[2].Description);
        var hasMore = Assert.Single(
            transcript.ServerMessages,
            m => m.Description.StartsWith("SUCCESS {has_more=true}", StringComparison.Ordinal));
        Assert.Equal("B170A1886861735F6D6F7265C3", hasMore.Hex);

        // Server-side: one connection, no failures, no duplicate execution.
        var counters = server.Counters();
        Assert.Equal(1, counters.Accepted);
        Assert.Equal(0, counters.Failed);

        EvidenceWriter.Write("b5b6-02-fetch-size-pagination", new
        {
            scenario = "B5B6-02",
            title = "Driver fetch size 1 drives bounded has_more pagination and reuse",
            uri_scheme = "bolt",
            driver_visible = new { values = names, reuse_query_value = 42 },
            pull_sizes = pulls.Select(p => p.Description),
            transcript = transcript.ToDocument(connection),
            counters = EvidenceWriter.CounterDocument(counters),
        });
    }

    [Fact]
    [Trait("Task", "B5-B6")]
    public async Task B5B6_03_DiscardOnPartialConsumption_RetiresResult_ConnectionReusable()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(tap.HostPort);
        string first = string.Empty;
        long afterDiscard = -1;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession(o => o.WithFetchSize(1));
            var cursor = await DriverFactory.Bounded(session.RunAsync("MATCH (n:Person) RETURN n.name"));
            Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
            first = cursor.Current["n.name"].As<string>();

            // Consume the partially fetched result: the driver discards the remaining records.
            await DriverFactory.Bounded(cursor.ConsumeAsync());

            // The same session/connection remains usable.
            var next = await DriverFactory.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverFactory.Bounded(next.FetchAsync()));
            afterDiscard = next.Current["answer"].As<long>();
            await DriverFactory.Bounded(next.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.Equal("Alice", first);
        Assert.Equal(42, afterDiscard);

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var connection = tap.SingleConnection();
        var transcript = WireTranscript.Build(connection);

        // DISCARD n=-1 drains the remainder; the server's terminal SUCCESS {} (no has_more) is
        // accepted as exhaustion and the connection continues.
        var discard = Assert.Single(transcript.ClientMessages, m => m.Name == "DISCARD");
        Assert.Equal("DISCARD n=-1", discard.Description);
        Assert.Equal("B12FA1816EFF", discard.Hex);
        Assert.Equal(2, transcript.CountClient("RUN"));
        Assert.Equal(0, transcript.CountServer("FAILURE"));
        Assert.Equal(1, server.Counters().Accepted);

        EvidenceWriter.Write("b5b6-03-discard-partial-consumption", new
        {
            scenario = "B5B6-03",
            title = "DISCARD on partial consumption; terminal SUCCESS {}; connection reused",
            uri_scheme = "bolt",
            driver_visible = new { first_value = first, post_discard_value = afterDiscard },
            discard_hex = discard.Hex,
            transcript = transcript.ToDocument(connection),
            counters = EvidenceWriter.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B6")]
    public async Task B5B6_04_SessionDisposalWithOutstandingResult_RetiresOperation()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(tap.HostPort);
        long afterDisposal = -1;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            var session = driver.AsyncSession(o => o.WithFetchSize(1));
            _ = await DriverFactory.Bounded(session.RunAsync("MATCH (n:Person) RETURN n.name"));
            // Dispose the session without consuming the result.
            await DriverFactory.Bounded(session.DisposeAsync().AsTask());

            await using (var next = driver.AsyncSession())
            {
                var cursor = await DriverFactory.Bounded(next.RunAsync("RETURN 42 AS answer"));
                Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
                afterDisposal = cursor.Current["answer"].As<long>();
                await DriverFactory.Bounded(cursor.ConsumeAsync());
            }
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.Equal(42, afterDisposal);

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var connection = tap.SingleConnection();
        var transcript = WireTranscript.Build(connection);

        // The driver terminated the outstanding result deterministically (DISCARD + RESET) and the
        // server retired the operation; the next query reused the same connection.
        Assert.Contains(
            transcript.ClientMessages,
            m => m.Name is "DISCARD" or "RESET");
        Assert.Equal(2, transcript.CountClient("RUN"));
        Assert.Equal(1, transcript.CountClient("HELLO"));
        var counters = server.Counters();
        Assert.Equal(1, counters.Accepted);
        Assert.Equal(0, counters.Failed);
        Assert.Equal(0, counters.Active);

        EvidenceWriter.Write("b5b6-04-session-disposal-outstanding", new
        {
            scenario = "B5B6-04",
            title = "Session disposal with an outstanding result retires the operation safely",
            uri_scheme = "bolt",
            driver_visible = new { post_disposal_value = afterDisposal },
            client_sequence = transcript.ClientMessages.Select(m => m.Name),
            transcript = transcript.ToDocument(connection),
            counters = EvidenceWriter.CounterDocument(counters),
        });
    }

    [Fact]
    [Trait("Task", "B5-B6")]
    public async Task B5B6_05_RepeatedSupportedQueries_SingleConnection_NoRetryOrDuplicate()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(tap.HostPort);
        var values = new List<long>();
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            for (var i = 0; i < 3; i++)
            {
                await using var session = driver.AsyncSession();
                var cursor = await DriverFactory.Bounded(session.RunAsync("RETURN 42 AS answer"));
                Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
                values.Add(cursor.Current["answer"].As<long>());
                await DriverFactory.Bounded(cursor.ConsumeAsync());
            }
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.Equal(new long[] { 42, 42, 42 }, values);

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var connection = tap.SingleConnection();
        var transcript = WireTranscript.Build(connection);

        // Pooled reuse: one connection, one initialisation, three distinct executions, no retries.
        Assert.Equal(1, transcript.CountClient("HELLO"));
        Assert.Equal(1, transcript.CountClient("LOGON"));
        Assert.Equal(3, transcript.CountClient("RUN"));
        Assert.Equal(3, transcript.CountClient("PULL"));
        Assert.Equal(1, transcript.CountClient("GOODBYE"));
        Assert.Equal(0, transcript.CountServer("FAILURE"));
        Assert.Equal(0, transcript.CountServer("IGNORED"));

        var counters = server.Counters();
        Assert.Equal(1, counters.Accepted);
        Assert.Equal(1, counters.Completed);
        Assert.Equal(0, counters.Failed);

        EvidenceWriter.Write("b5b6-05-repeated-queries-single-connection", new
        {
            scenario = "B5B6-05",
            title = "Three sequential sessions reuse one pooled connection with no retries",
            uri_scheme = "bolt",
            driver_visible = new { values },
            counts = new
            {
                hello = transcript.CountClient("HELLO"),
                logon = transcript.CountClient("LOGON"),
                run = transcript.CountClient("RUN"),
                pull = transcript.CountClient("PULL"),
                goodbye = transcript.CountClient("GOODBYE"),
            },
            transcript = transcript.ToDocument(connection),
            counters = EvidenceWriter.CounterDocument(counters),
        });
    }
}
