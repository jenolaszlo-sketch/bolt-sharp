using BoltSharp.B5BQ1.Probes.Infrastructure;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.B5BQ1.Probes.Scenarios;

/// <summary>
/// Q1-C/Q1-D: scalar and graph query interoperability. Expected values are constructed from the
/// commission's independent oracles and the accepted reference fixture, never from the producer's
/// encodings; wire captures are independent observations.
/// </summary>
[Collection("Q1")]
public sealed class Q1ScalarGraphProbes
{
    [Fact]
    public async Task Q1C01_ScalarQuery_DriverVisibleResultAndWireOracle()
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        await using var tap = await Q1WireTap.StartAsync(server.Endpoint);

        var driver = Q1Driver.Plaintext(tap.HostPort);
        object? rawValue = null;
        long answer = 0;
        IResultSummary? summary = null;
        try
        {
            await using var session = driver.AsyncSession();
            var cursor = await Q1Driver.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await Q1Driver.Bounded(cursor.FetchAsync()));
            rawValue = cursor.Current["answer"];
            answer = cursor.Current["answer"].As<long>();
            Assert.False(await Q1Driver.Bounded(cursor.FetchAsync()), "cursor must exhaust after one record");
            summary = await Q1Driver.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        // Driver-visible oracle: Int64 42, exhaustion, summary.
        Assert.Equal(42L, answer);
        Assert.IsType<long>(rawValue);
        Assert.NotNull(summary);

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var (clientMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ClientToServer);
        var (serverMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ServerToClient);

        // Actual RUN and PULL requests were sent (driver-generated, no fabricated client traffic).
        Assert.Contains(clientMessages, m => m.Name == "RUN");
        Assert.Contains(clientMessages, m => m.Name == "PULL");
        var run = clientMessages.First(m => m.Name == "RUN");
        Assert.Contains("RETURN 42 AS answer", run.Description);

        // RUN SUCCESS metadata: fields=["answer"].
        var runSuccess = serverMessages.Where(m => m.Name == "SUCCESS").First(s => s.Description.Contains("fields="));
        Assert.Contains("fields=[answer]", runSuccess.Description);

        // One RECORD; independent wire oracle: the accepted historical RECORD encoding B1 71 91 2A.
        var records = serverMessages.Where(m => m.Name == "RECORD").ToArray();
        Assert.Single(records);
        Assert.Equal("B171912A", records[0].Hex);

        // Terminal SUCCESS {} without has_more: cursor exhaustion signal.
        var terminal = serverMessages.Last(m => m.Name == "SUCCESS");
        Assert.Equal("B170A0", terminal.Hex);

        // No ROUTE, TELEMETRY, DISCARD or duplicate RUN in this flow.
        Assert.DoesNotContain(clientMessages, m => m.Name is "ROUTE" or "TELEMETRY" or "DISCARD");
        Assert.Equal(1, clientMessages.Count(m => m.Name == "RUN"));
        Assert.Equal(1, clientMessages.Count(m => m.Name == "PULL"));

        Q1Evidence.Write("q1c01-scalar-query", new
        {
            probe = "Q1-C01",
            driver_visible = new { answer, raw_type = rawValue?.GetType().FullName },
            client_messages = Q1Evidence.MessagesDocument(clientMessages),
            server_messages = Q1Evidence.MessagesDocument(serverMessages),
            record_hex = records[0].Hex,
            terminal_hex = terminal.Hex,
            counters = Q1Evidence.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    public async Task Q1C02_ScalarQuery_SubsequentSupportedQuerySucceedsOnSameConnection()
    {
        var providers = new Q1ProviderLog();
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier, providers: providers);

        var driver = Q1Driver.Plaintext(server.HostPort);
        var observed = new List<long>();
        try
        {
            await using var session = driver.AsyncSession();
            for (var i = 0; i < 2; i++)
            {
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

        Assert.Equal(new[] { 42L, 42L }, observed);
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));

        // Independent pooled-connection oracle: exactly one per-connection provider instance was
        // created, and it served both RUNs.
        var providersSnapshot = providers.Providers;
        Assert.Single(providersSnapshot);
        Assert.Equal(2, providersSnapshot[0].PrepareCalls);
        Assert.Equal(2, providersSnapshot[0].OperationsOpened);
        Assert.Equal(2, providersSnapshot[0].DisposeCalls);
        Assert.Equal(0, providersSnapshot[0].DisposeFailures);

        Q1Evidence.Write("q1c02-sequential-queries-one-connection", new
        {
            probe = "Q1-C02",
            observed = observed,
            physical_connections = providersSnapshot.Count,
            per_connection = Q1Evidence.ProvidersDocument(providers),
            counters = Q1Evidence.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    public async Task Q1D01_GraphQuery_TypedIdentity_IndependentOracle()
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        await using var tap = await Q1WireTap.StartAsync(server.Endpoint);

        var driver = Q1Driver.Plaintext(tap.HostPort);
        INode? alice = null;
        IRelationship? knows = null;
        INode? bob = null;
        try
        {
            await using var session = driver.AsyncSession();
            var cursor = await Q1Driver.Bounded(
                session.RunAsync("MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b"));
            Assert.True(await Q1Driver.Bounded(cursor.FetchAsync()));
            alice = cursor.Current["a"].As<INode>();
            knows = cursor.Current["r"].As<IRelationship>();
            bob = cursor.Current["b"].As<INode>();
            Assert.False(await Q1Driver.Bounded(cursor.FetchAsync()));
            await Q1Driver.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        // Independent commission oracle for Alice (numeric Id is deliberately exercised; the
        // element id is the modern identity).
#pragma warning disable CS0618
        Assert.NotNull(alice);
        Assert.Equal(1L, alice!.Id);
        Assert.Equal("n:1", alice.ElementId);
        Assert.Contains("Person", alice.Labels);
        Assert.Equal("Alice", alice.Properties["name"]);

        // Independent oracle for Bob.
        Assert.NotNull(bob);
        Assert.Equal(2L, bob!.Id);
        Assert.Equal("n:2", bob.ElementId);
        Assert.Contains("Person", bob.Labels);
        Assert.Equal("Bob", bob.Properties["name"]);

        // Independent oracle for the relationship (numeric ID 1 deliberately collides with Alice's).
        Assert.NotNull(knows);
        Assert.Equal(1L, knows!.Id);
        Assert.Equal("r:1", knows.ElementId);
        Assert.Equal("KNOWS", knows.Type);
        Assert.Equal("n:1", knows.StartNodeElementId);
        Assert.Equal("n:2", knows.EndNodeElementId);
        Assert.Equal(2024L, knows.Properties["since"]);
        Assert.Equal(1L, knows.StartNodeId);
        Assert.Equal(2L, knows.EndNodeId);
#pragma warning restore CS0618

        // Node and relationship identity remain distinct despite the numeric collision.
        Assert.NotEqual(alice!.ElementId, knows.ElementId);
        Assert.False(ReferenceEquals(alice, knows));

        // Direction control: the relationship points from Alice to Bob (never reversed).
        Assert.Equal(alice.ElementId, knows.StartNodeElementId);
        Assert.Equal(bob!.ElementId, knows.EndNodeElementId);

        // Wire oracle: exactly one RECORD for the single KNOWS relationship.
        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var (serverMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ServerToClient);
        Assert.Equal(1, serverMessages.Count(m => m.Name == "RECORD"));

#pragma warning disable CS0618
        Q1Evidence.Write("q1d01-graph-query", new
        {
            probe = "Q1-D01",
            alice = new { id = alice!.Id, element_id = alice.ElementId, labels = alice.Labels, name = alice.Properties["name"] },
            bob = new { id = bob!.Id, element_id = bob.ElementId, labels = bob.Labels, name = bob.Properties["name"] },
            knows = new
            {
                id = knows!.Id,
                element_id = knows.ElementId,
                type = knows.Type,
                start = knows.StartNodeElementId,
                end = knows.EndNodeElementId,
                since = knows.Properties["since"],
            },
            server_messages = Q1Evidence.MessagesDocument(serverMessages),
        });
#pragma warning restore CS0618
    }
}
