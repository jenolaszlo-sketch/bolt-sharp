using BoltSharp.B5BQ1.Probes.Infrastructure;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.B5BQ1.Probes.Scenarios;

/// <summary>
/// Q1-F: cursor completion, pagination and pooled connection reuse. The pooled-connection oracle
/// is the reviewer's per-connection provider log: one physical server connection produces exactly
/// one provider instance.
/// </summary>
[Collection("Q1")]
public sealed class Q1CursorPoolingProbes
{
    [Fact]
    public async Task Q1F01_MultiRecordResult_FullConsumption_TerminalSuccessEmpty()
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        await using var tap = await Q1WireTap.StartAsync(server.Endpoint);

        var driver = Q1Driver.Plaintext(tap.HostPort);
        var names = new List<string>();
        try
        {
            await using var session = driver.AsyncSession();
            var cursor = await Q1Driver.Bounded(
                session.RunAsync("MATCH (n:Person) RETURN n.name AS name ORDER BY name"));
            while (await Q1Driver.Bounded(cursor.FetchAsync()))
            {
                names.Add(cursor.Current["name"].As<string>());
            }

            await Q1Driver.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.Equal(new[] { "Alice", "Bob" }, names);

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var (serverMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ServerToClient);
        var records = serverMessages.Where(m => m.Name == "RECORD").ToArray();
        Assert.Equal(2, records.Length);
        // Records contain the ordered names; the terminal summary is SUCCESS {} (B170A0, no has_more).
        var terminal = serverMessages.Last(m => m.Name == "SUCCESS");
        Assert.Equal("B170A0", terminal.Hex);
        Assert.DoesNotContain("has_more", terminal.Description);

        // No extra PULL after terminal completion (client sent exactly one PULL for this query).
        var (clientMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ClientToServer);
        Assert.Equal(1, clientMessages.Count(m => m.Name == "PULL"));

        Q1Evidence.Write("q1f01-multi-records", new
        {
            probe = "Q1-F01",
            names,
            record_count = records.Length,
            terminal_hex = terminal.Hex,
            pull_count = clientMessages.Count(m => m.Name == "PULL"),
            server_messages = Q1Evidence.MessagesDocument(serverMessages),
        });
    }

    [Fact]
    public async Task Q1F02_FetchSizeOne_HasMorePagination_RecordsNotDuplicatedOrOmitted()
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        await using var tap = await Q1WireTap.StartAsync(server.Endpoint);

        var driver = Q1Driver.Plaintext(tap.HostPort);
        var names = new List<string>();
        try
        {
            await using var session = driver.AsyncSession(o => o.WithFetchSize(1));
            var cursor = await Q1Driver.Bounded(
                session.RunAsync("MATCH (n:Person) RETURN n.name AS name ORDER BY name"));
            while (await Q1Driver.Bounded(cursor.FetchAsync()))
            {
                names.Add(cursor.Current["name"].As<string>());
            }

            await Q1Driver.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        // Every record exactly once, in order.
        Assert.Equal(new[] { "Alice", "Bob" }, names);

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var (clientMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ClientToServer);
        var (serverMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ServerToClient);

        // has_more:true was sent (PULL n=1 answered with has_more=true first), then terminal SUCCESS {}.
        var pulls = clientMessages.Where(m => m.Name == "PULL").ToArray();
        Assert.Equal(2, pulls.Length);
        Assert.Contains("n=1", pulls[0].Description);
        var hasMore = serverMessages.Where(m => m.Name == "SUCCESS" && m.Description.Contains("has_more=true")).ToArray();
        Assert.Single(hasMore);
        Assert.Equal("B170A1" /* map of one */, hasMore[0].Hex[..6]);
        Assert.Contains("has_more=true", hasMore[0].Description);
        Assert.Equal("B170A0", serverMessages.Last(m => m.Name == "SUCCESS").Hex);

        // Exactly 2 records total: no duplication, no omission.
        Assert.Equal(2, serverMessages.Count(m => m.Name == "RECORD"));

        Q1Evidence.Write("q1f02-has-more-pagination", new
        {
            probe = "Q1-F02",
            names,
            pull_count = pulls.Length,
            has_more_count = hasMore.Length,
            server_messages = Q1Evidence.MessagesDocument(serverMessages),
        });
    }

    [Fact]
    public async Task Q1F03_PartialConsumption_Discard_ConnectionRemainsUsable()
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        await using var tap = await Q1WireTap.StartAsync(server.Endpoint);
        var providers = new Q1ProviderLog();

        var driver = Q1Driver.Plaintext(tap.HostPort);
        try
        {
            await using var session = driver.AsyncSession(o => o.WithFetchSize(1));
            var cursor = await Q1Driver.Bounded(
                session.RunAsync("MATCH (n:Person) RETURN n.name AS name ORDER BY name"));
            Assert.True(await Q1Driver.Bounded(cursor.FetchAsync()));
            // Consume the outstanding result after partial fetch: the driver issues DISCARD.
            await Q1Driver.Bounded(cursor.ConsumeAsync());

            // The same session/connection continues with the next query.
            var cursor2 = await Q1Driver.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await Q1Driver.Bounded(cursor2.FetchAsync()));
            Assert.Equal(42L, cursor2.Current["answer"].As<long>());
            await Q1Driver.Bounded(cursor2.ConsumeAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var (clientMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ClientToServer);
        var discard = clientMessages.FirstOrDefault(m => m.Name == "DISCARD");
        Assert.NotNull(discard);

        Q1Evidence.Write("q1f03-discard-partial", new
        {
            probe = "Q1-F03",
            discard_description = discard!.Description,
            discard_hex = discard.Hex,
            client_messages = Q1Evidence.MessagesDocument(clientMessages),
        });
    }

    [Fact]
    public async Task Q1F04_SessionDisposalWithOutstandingResult_OperationRetired()
    {
        var providers = new Q1ProviderLog();
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier, providers: providers);

        var driver = Q1Driver.Plaintext(server.HostPort);
        try
        {
            await using var session = driver.AsyncSession(o => o.WithFetchSize(1));
            var cursor = await Q1Driver.Bounded(
                session.RunAsync("MATCH (n:Person) RETURN n.name AS name ORDER BY name"));
            Assert.True(await Q1Driver.Bounded(cursor.FetchAsync()));
            // Dispose the session with the cursor outstanding.
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        // Session disposed here (using block end).

        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));
        var snapshot = providers.Providers;
        Assert.Single(snapshot);
        // The server retired exactly the one operation, with exactly one disposal attempt.
        Assert.Equal(1, snapshot[0].OperationsOpened);
        Assert.Equal(1, snapshot[0].DisposeCalls);
        Assert.Equal(0, snapshot[0].DisposeFailures);

        Q1Evidence.Write("q1f04-session-disposal-outstanding", new
        {
            probe = "Q1-F04",
            per_connection = Q1Evidence.ProvidersDocument(providers),
            counters = Q1Evidence.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    public async Task Q1F05_ThreeSequentialSessions_OnePooledPhysicalConnection()
    {
        var providers = new Q1ProviderLog();
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier, providers: providers);
        await using var tap = await Q1WireTap.StartAsync(server.Endpoint);

        var driver = Q1Driver.Plaintext(tap.HostPort);
        var observed = new List<long>();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                await using var session = driver.AsyncSession();
                var cursor = await Q1Driver.Bounded(session.RunAsync("RETURN 42 AS answer"));
                Assert.True(await Q1Driver.Bounded(cursor.FetchAsync()));
                observed.Add(cursor.Current["answer"].As<long>());
                await Q1Driver.Bounded(cursor.ConsumeAsync());
            }
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.Equal(new[] { 42L, 42L, 42L }, observed);
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0 && c.Completed >= 1, TimeSpan.FromSeconds(10)));

        // Independent pooling oracle: one physical server connection for all three sessions.
        var snapshot = providers.Providers;
        Assert.Single(snapshot);
        Assert.Equal(3, snapshot[0].PrepareCalls);
        Assert.Equal(3, snapshot[0].OperationsOpened);
        Assert.Equal(3, snapshot[0].DisposeCalls);

        // Exactly one HELLO and one LOGON on the wire; three RUNs and three PULLs.
        var (clientMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ClientToServer);
        Assert.Equal(1, clientMessages.Count(m => m.Name == "HELLO"));
        Assert.Equal(1, clientMessages.Count(m => m.Name == "LOGON"));
        Assert.Equal(3, clientMessages.Count(m => m.Name == "RUN"));
        Assert.Equal(3, clientMessages.Count(m => m.Name == "PULL"));
        Assert.Equal(1, clientMessages.Count(m => m.Name == "GOODBYE"));
        // No FAILURE or IGNORED frames in the healthy reuse flow.
        var (serverMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ServerToClient);
        Assert.DoesNotContain(serverMessages, m => m.Name is "FAILURE" or "IGNORED");

        Q1Evidence.Write("q1f05-pooling-three-sessions", new
        {
            probe = "Q1-F05",
            observed,
            physical_connections = snapshot.Count,
            per_connection = Q1Evidence.ProvidersDocument(providers),
            client_sequence = clientMessages.Select(m => m.Name),
            counters = Q1Evidence.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    public async Task Q1F06_DriverDisposal_ClosesPhysicalConnection()
    {
        var providers = new Q1ProviderLog();
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier, providers: providers);

        var driver = Q1Driver.Plaintext(server.HostPort);
        await using var session = driver.AsyncSession();
        var cursor = await Q1Driver.Bounded(session.RunAsync("RETURN 42 AS answer"));
        Assert.True(await Q1Driver.Bounded(cursor.FetchAsync()));
        await Q1Driver.Bounded(cursor.ConsumeAsync());

        await Q1Driver.Bounded(driver.DisposeAsync());

        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0 && c.Completed >= 1, TimeSpan.FromSeconds(10)));
        Assert.Single(providers.Providers);

        Q1Evidence.Write("q1f06-driver-disposal", new
        {
            probe = "Q1-F06",
            counters = Q1Evidence.CounterDocument(server.Counters()),
            per_connection = Q1Evidence.ProvidersDocument(providers),
        });
    }
}
