using BoltSharp.B5BQ1.Probes.Infrastructure;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.B5BQ1.Probes.Scenarios;

/// <summary>
/// Q1-H/Q1-I/Q1-J: independent reproduction and adjudication of producer findings F01 (unsupported
/// transaction/routing messages) and F02 (generic error classification and connection disposal),
/// plus the natural live-driver FAILURE/IGNORED sequence. All reproductions go through the
/// unmodified official driver's public API; the tap is passive observation only.
/// </summary>
[Collection("Q1")]
public sealed class Q1FindingProbes
{
    [Fact]
    public async Task Q1H01_F01_ExplicitTransaction_BeginConnectionClosure()
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        await using var tap = await Q1WireTap.StartAsync(server.Endpoint);

        var driver = Q1Driver.Plaintext(tap.HostPort);
        Exception? failure = null;
        try
        {
            await using var session = driver.AsyncSession();
            var transaction = await Q1Driver.Bounded(session.BeginTransactionAsync());
            // If BEGIN somehow succeeded, commit would be the next step; with BoltSharp the
            // expected outcome is a connection failure surfaced by BeginTransactionAsync itself.
            await Q1Driver.Bounded(transaction.CommitAsync());
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        await ObserveF01Async(server, tap, failure!, probe: "Q1-H01", flow: "explicit BeginTransactionAsync");
    }

    [Fact]
    public async Task Q1H02_F01_ManagedTransaction_ExecuteRead()
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        await using var tap = await Q1WireTap.StartAsync(server.Endpoint);

        var driver = Q1Driver.Plaintext(tap.HostPort);
        Exception? failure = null;
        try
        {
            await using var session = driver.AsyncSession();
            var answer = await Q1Driver.Bounded(session.ExecuteReadAsync(async tx =>
            {
                var cursor = await tx.RunAsync("RETURN 42 AS answer");
                if (!await cursor.FetchAsync())
                {
                    throw new InvalidOperationException("q1: managed transaction produced no record");
                }

                return cursor.Current["answer"].As<long>();
            }));
            Assert.Equal(42L, answer);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.NotNull(failure);
        await ObserveF01Async(server, tap, failure!, probe: "Q1-H02", flow: "managed ExecuteReadAsync");
    }

    [Fact]
    public async Task Q1H03_F01_RoutingScheme_RouteMessageClosure()
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        await using var tap = await Q1WireTap.StartAsync(server.Endpoint);

        var driver = Q1Driver.RoutingScheme(tap.HostPort);
        Exception? failure = null;
        try
        {
            await Q1Driver.Bounded(driver.VerifyConnectivityAsync(), TimeSpan.FromSeconds(20));
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.NotNull(failure);
        await ObserveF01Async(server, tap, failure!, probe: "Q1-H03", flow: "neo4j:// routing scheme");
    }

    private static async Task ObserveF01Async(Q1Server server, Q1WireTap tap, Exception failure, string probe, string flow)
    {
        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));

        var (clientMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ClientToServer);
        var (serverMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ServerToClient);

        // Which unsupported message actually reached the server? (BEGIN 0x11 / COMMIT 0x12 /
        // ROLLBACK 0x13 / ROUTE 0x66).
        var unsupportedSent = clientMessages
            .Where(m => m.Name is "BEGIN" or "COMMIT" or "ROLLBACK" or "ROUTE")
            .Select(m => m.Name)
            .ToArray();

        // The server's response shape: whether any FAILURE (e.g. the documented 08N06) was written,
        // or the connection was closed with no response at all.
        var failureResponses = serverMessages.Where(m => m.Name == "FAILURE").ToArray();
        var counters = server.Counters();
        var connectionFailedEvent = server.Events().FirstOrDefault(e =>
            e.Kind == BoltSharp.Transport.BoltTransportEventKind.ConnectionFailed);

        // Driver-visible classification (recorded, not prescribed).
        var document = new
        {
            probe,
            flow,
            driver_failure = Q1Driver.ExceptionDocument(failure),
            unsupported_messages_reaching_wire = unsupportedSent,
            server_failure_responses = failureResponses.Select(m => m.Description).ToArray(),
            client_messages = clientMessages.Select(m => m.Name).ToArray(),
            server_messages = serverMessages.Select(m => m.Name).ToArray(),
            connection_failed_outcome = connectionFailedEvent?.Detail,
            counters = Q1Evidence.CounterDocument(counters),
        };
        Q1Evidence.Write(probe.ToLowerInvariant().Replace('_', '-') + "-f01", document);

        // The invariant that matters: the flow did NOT succeed, and the connection ended.
        Assert.True(unsupportedSent.Length > 0 || failure.Message.Contains("rout", StringComparison.OrdinalIgnoreCase),
            "either an unsupported transaction/routing message reached the wire or the failure is routing-related");
    }

    [Fact]
    public async Task Q1I01_F02_UnsupportedQuery_Classification_ConnectionDispositionAndRecovery()
    {
        var providers = new Q1ProviderLog();
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier, providers: providers);

        var driver = Q1Driver.Plaintext(server.HostPort);
        Exception? failure = null;
        long recoveryAnswer = 0;
        try
        {
            try
            {
                await using var session = driver.AsyncSession();
                var cursor = await Q1Driver.Bounded(session.RunAsync("MATCH (n) DETACH DELETE n"));
                await Q1Driver.Bounded(cursor.FetchAsync());
                await Q1Driver.Bounded(cursor.ConsumeAsync());
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            // Recovery: a subsequent supported query in a fresh session must return the correct value.
            await using var session2 = driver.AsyncSession();
            var cursor2 = await Q1Driver.Bounded(session2.RunAsync("RETURN 42 AS answer"));
            Assert.True(await Q1Driver.Bounded(cursor2.FetchAsync()));
            recoveryAnswer = cursor2.Current["answer"].As<long>();
            await Q1Driver.Bounded(cursor2.ConsumeAsync());

            // Graph unchanged: the destructive query never executed.
            await using var session3 = driver.AsyncSession();
            var cursor3 = await Q1Driver.Bounded(
                session3.RunAsync("MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b"));
            Assert.True(await Q1Driver.Bounded(cursor3.FetchAsync()));
            Assert.Equal("Alice", cursor3.Current["a"].As<INode>().Properties["name"]);
            await Q1Driver.Bounded(cursor3.ConsumeAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.NotNull(failure);
        Assert.Equal(42L, recoveryAnswer);
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));

        var providersSnapshot = providers.Providers;
        Assert.True(providersSnapshot.Count >= 1);
        var counters = server.Counters();

        Q1Evidence.Write("q1i01-f02-unsupported-query", new
        {
            probe = "Q1-I01",
            driver_failure = Q1Driver.ExceptionDocument(failure!),
            recovery_answer = recoveryAnswer,
            graph_unchanged = true,
            physical_connections = providersSnapshot.Count,
            connection_disposition = providersSnapshot.Count == 1 ? "same-connection-reuse" : "connection-destroyed-fresh-connection",
            per_connection = Q1Evidence.ProvidersDocument(providers),
            run_events_total = server.CountProcessedSignatures("0x10"),
            counters = Q1Evidence.CounterDocument(counters),
        });
    }

    [Fact]
    public async Task Q1J01_LiveDriverFailureThenIgnored_ZeroFieldEncoding_NoDesync()
    {
        var providers = new Q1ProviderLog();
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier, providers: providers);
        await using var tap = await Q1WireTap.StartAsync(server.Endpoint);

        var driver = Q1Driver.Plaintext(tap.HostPort);
        Exception? failure = null;
        long recoveryAnswer = 0;
        try
        {
            await using var session = driver.AsyncSession();
            // The driver pipelines RUN + PULL for auto-commit queries; the refused RUN fails and
            // the pipelined PULL is answered with IGNORED.
            var cursor = await Q1Driver.Bounded(session.RunAsync("MATCH (n) DETACH DELETE n"));
            await Q1Driver.Bounded(cursor.FetchAsync());
            await Q1Driver.Bounded(cursor.ConsumeAsync());
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        // The driver must recover and a subsequent query must return the correct value.
        try
        {
            await using var session2 = driver.AsyncSession();
            var cursor2 = await Q1Driver.Bounded(session2.RunAsync("RETURN 42 AS answer"));
            Assert.True(await Q1Driver.Bounded(cursor2.FetchAsync()));
            recoveryAnswer = cursor2.Current["answer"].As<long>();
            await Q1Driver.Bounded(cursor2.ConsumeAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.NotNull(failure);
        Assert.Equal(42L, recoveryAnswer);

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var (clientMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ClientToServer);
        var (serverMessages, _) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ServerToClient);

        // Both messages were actually sent: driver-generated RUN and PULL.
        var run = clientMessages.FirstOrDefault(m => m.Name == "RUN");
        var pull = clientMessages.FirstOrDefault(m => m.Name == "PULL");
        Assert.NotNull(run);
        Assert.NotNull(pull);

        // Server-side: FAILURE for the RUN, then a zero-field IGNORED for the PULL.
        var failureMessage = serverMessages.FirstOrDefault(m => m.Name == "FAILURE");
        var ignored = serverMessages.FirstOrDefault(m => m.Name == "IGNORED");
        Assert.NotNull(failureMessage);
        Assert.NotNull(ignored);
        Assert.Equal("B07E", ignored!.Hex);
        Assert.Contains("50N42", failureMessage!.Description);
        Assert.Contains("Neo.ClientError.Request.Invalid", failureMessage.Description);

        // No fabricated RECORD or SUCCESS for the ignored PULL: the FAILURE is immediately
        // followed by the IGNORED, and the refused connection's full response stream (everything
        // up to and including the IGNORED, before the recovery connection's handshake) contains
        // only the two authentication SUCCESSes and no RECORD.
        var failureIndex = serverMessages.ToList().FindIndex(m => m.Name == "FAILURE");
        var ignoredIndex = serverMessages.ToList().FindIndex(m => m.Name == "IGNORED");
        Assert.Equal(failureIndex + 1, ignoredIndex);
        var refusedConnectionResponses = serverMessages.Take(ignoredIndex + 1).ToArray();
        Assert.Equal(2, refusedConnectionResponses.Count(m => m.Name == "SUCCESS"));
        Assert.DoesNotContain(refusedConnectionResponses, m => m.Name == "RECORD");

        // No unintended query execution: the refused RUN never opened an operation on the server
        // (Prepare was invoked and refused it, so zero operations opened and zero rows were read).
        Assert.Equal(0, providers.Providers[0].OperationsOpened);
        Assert.Equal(1, providers.Providers[0].PrepareCalls);
        Assert.Equal(0, providers.Providers[0].ReadRowCalls);

        // Physical connection disposition: recovery used a fresh connection (provider #2) or
        // the same one; recorded honestly either way.
        var dispositionAfterRecovery = providers.Providers.Count == 1
            ? "same-connection-reuse"
            : "connection-destroyed-fresh-connection";

        Q1Evidence.Write("q1j01-live-driver-ignored", new
        {
            probe = "Q1-J01",
            driver_failure = Q1Driver.ExceptionDocument(failure!),
            recovery_answer = recoveryAnswer,
            run_description = run.Description,
            pull_description = pull.Description,
            server_failure = failureMessage.Description,
            ignored_hex = ignored.Hex,
            ignored_is_zero_field = ignored.Hex == "B07E",
            client_sequence = clientMessages.Select(m => m.Name).ToArray(),
            server_sequence = serverMessages.Select(m => m.Name).ToArray(),
            physical_connections = providers.Providers.Count,
            connection_disposition = dispositionAfterRecovery,
            per_connection = Q1Evidence.ProvidersDocument(providers),
            counters = Q1Evidence.CounterDocument(server.Counters()),
        });
    }
}
