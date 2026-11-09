using BoltSharp.DriverInteropTests.Infrastructure;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.DriverInteropTests.Scenarios;

/// <summary>
/// B5-B8 — unsupported-query refusal and recovery interoperability. The accepted B3-C reference
/// backend refuses <c>MATCH (n) DETACH DELETE n</c> with the bounded generic 50N42 FAILURE; the
/// official driver must surface it accurately, execute nothing, and recover. This suite also
/// exercises the historical B3-A F-02 live-driver IGNORED obligation with the zero-field
/// <c>B0 7E</c> encoding — no bytes are injected into the driver's stream.
/// </summary>
[Collection("B5-B")]
public sealed class ErrorInteropTests
{
    private const string UnsupportedQuery = "MATCH (n) DETACH DELETE n";

    [Fact]
    [Trait("Task", "B5-B8")]
    public async Task B5B8_01_UnsupportedQuery_Refused_DriverErrorClassification()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(tap.HostPort);
        Exception? failure = null;
        var namesAfter = new List<string>();
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            try
            {
                var cursor = await DriverFactory.Bounded(session.RunAsync(UnsupportedQuery));
                await DriverFactory.Bounded(cursor.ConsumeAsync());
                Assert.Fail("the unsupported query must not succeed");
            }
            catch (Neo4jException ex)
            {
                failure = ex;
            }

            // The graph must be unchanged: the two-person reference fixture still answers.
            var graph = await DriverFactory.Bounded(session.RunAsync("MATCH (n:Person) RETURN n.name AS name ORDER BY name"));
            while (await DriverFactory.Bounded(graph.FetchAsync()))
            {
                namesAfter.Add(graph.Current["name"].As<string>());
            }

            await DriverFactory.Bounded(graph.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        var neo4jFailure = Assert.IsAssignableFrom<Neo4jException>(failure);
        Assert.Equal("Neo.ClientError.Request.Invalid", neo4jFailure.Code);
        Assert.Equal("50N42", neo4jFailure.GqlStatus);
        Assert.False(neo4jFailure.IsRetriable);
        Assert.Equal(new[] { "Alice", "Bob" }, namesAfter);

        Assert.True(await tap.WaitForClosedConnectionsAsync(2, TimeSpan.FromSeconds(10)));

        // The failing connection carries the refused RUN; recovery happened on a fresh connection
        // because the driver destroys a connection after an unknown-classification failure.
        var (failedConnection, transcript) = tap.FindConnection(
            t => t.CountClient("RUN") == 1 && t.FirstClient("RUN")!.Description.Contains(UnsupportedQuery, StringComparison.Ordinal));

        // Exactly one attempt: no driver-side retry, no duplicate execution, no fabricated record.
        var unsupportedRuns = transcript.ClientMessages
            .Where(m => m.Name == "RUN" && m.Description.Contains(UnsupportedQuery, StringComparison.Ordinal))
            .ToArray();
        Assert.Single(unsupportedRuns);
        var failureMessage = transcript.ServerMessages.First(m => m.Name == "FAILURE");
        Assert.Contains("gql_status=50N42", failureMessage.Description);
        Assert.Contains("Neo.ClientError.Request.Invalid", failureMessage.Description);
        // No query-text echo on the wire in the FAILURE metadata, and no RECORD for the refusal.
        Assert.DoesNotContain(
            Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes("DETACH DELETE")),
            failureMessage.Hex);
        Assert.Equal(0, transcript.CountServer("RECORD"));

        // The recovery graph query ran on the replacement connection and returned both records.
        var (recoveryConnection, recoveryTranscript) = tap.FindConnection(t => t.CountServer("RECORD") == 2);
        Assert.Equal(1, recoveryTranscript.CountClient("RUN"));

        EvidenceWriter.Write("b5b8-01-unsupported-query", new
        {
            scenario = "B5B8-01",
            title = "Unsupported query refused; driver-visible Neo4jException classification",
            uri_scheme = "bolt",
            driver_failure = EvidenceWriter.ExceptionDocument(failure!),
            driver_visible = new { graph_unchanged_names = namesAfter },
            unsupported_run_hex = unsupportedRuns[0].Hex,
            failure_description = failureMessage.Description,
            failed_connection = transcript.ToDocument(failedConnection),
            recovery_connection = recoveryTranscript.ToDocument(recoveryConnection),
            counters = EvidenceWriter.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B8")]
    public async Task B5B8_02_LiveDriverIgnored_PipelinedFailureSequence()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(tap.HostPort);
        Exception? failure = null;
        long recovered = -1;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            try
            {
                var cursor = await DriverFactory.Bounded(session.RunAsync(UnsupportedQuery));
                await DriverFactory.Bounded(cursor.ConsumeAsync());
                Assert.Fail("the unsupported query must not succeed");
            }
            catch (Neo4jException ex)
            {
                failure = ex;
            }

            // Session-level recovery in the SAME session works.
            var recovery = await DriverFactory.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverFactory.Bounded(recovery.FetchAsync()));
            recovered = recovery.Current["answer"].As<long>();
            await DriverFactory.Bounded(recovery.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.NotNull(failure);
        Assert.Equal(42, recovered);

        Assert.True(await tap.WaitForClosedConnectionsAsync(2, TimeSpan.FromSeconds(10)));
        var connections = tap.Connections();
        var (failingConnection, transcript) = tap.FindConnection(
            t => t.CountClient("RUN") == 1 && t.FirstClient("RUN")!.Description.Contains(UnsupportedQuery, StringComparison.Ordinal));

        // The driver pipelined RUN and PULL in one client write: the PULL was already in flight
        // when the FAILURE for RUN arrived.
        var runHex = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(UnsupportedQuery));
        Assert.Contains(
            failingConnection.Chunks,
            c => c.Direction == TapDirection.ClientToServer
                && WireTranscript.Hex(c.Bytes).Contains("B310D019" + runHex, StringComparison.Ordinal)
                && WireTranscript.Hex(c.Bytes).Contains("B13FA1816EC903E8", StringComparison.Ordinal));

        // The server answered FAILURE to RUN and the zero-field IGNORED (B0 7E) to PULL.
        var runFailure = Assert.Single(transcript.ServerMessages, m => m.Name == "FAILURE");
        Assert.Contains("gql_status=50N42", runFailure.Description);
        var ignored = Assert.Single(transcript.ServerMessages, m => m.Name == "IGNORED");
        Assert.Equal("B07E", ignored.Hex);
        Assert.True(ignored.Decoded);

        // The failing connection received no further client requests after the pipelined PULL:
        // there was no protocol desynchronization and no additional request was issued on it.
        // (RESETs before the RUN belong to the driver's normal pool check-out behavior.)
        var lastClientMessage = transcript.ClientMessages.Last();
        Assert.Equal("PULL", lastClientMessage.Name);
        var afterPull = transcript.ClientMessages.SkipWhile(m => m.Name != "PULL").Skip(1);
        Assert.Empty(afterPull);

        // Driver-level recovery happened through a fresh connection (the driver destroys a
        // connection after an unknown-classification failure rather than RESET-recovering it).
        Assert.True(connections.Count >= 2);
        var (_, recoveryTranscript) = tap.FindConnection(t => t.CountServer("RECORD") == 1);
        Assert.Contains(recoveryTranscript.ServerMessages, m => m.Name == "RECORD" && m.Hex == "B171912A");

        EvidenceWriter.Write("b5b8-02-live-driver-ignored", new
        {
            scenario = "B5B8-02",
            title = "Live-driver IGNORED: pipelined RUN->FAILURE and PULL->IGNORED (B0 7E)",
            uri_scheme = "bolt",
            driver_failure = EvidenceWriter.ExceptionDocument(failure!),
            ignored_hex = ignored.Hex,
            ignored_decoded = ignored.Decoded,
            pipelined_run_plus_pull_in_one_client_chunk = true,
            requests_after_ignored_on_failing_connection = Array.Empty<string>(),
            same_session_recovery_value = recovered,
            recovery_connection = "new connection after driver-side destroy",
            disposition = "LIVE-DRIVER IGNORED - EXERCISED (automatic pipelined PULL after a failed " +
                "auto-commit RUN received the zero-field B0 7E IGNORED and the driver completed its " +
                "error handling without desynchronization). Same-connection RESET recovery after the " +
                "IGNORED was NOT exercised: under the accepted 50N42 classification the driver treats " +
                "the failure as an unknown ProtocolException and destroys the connection instead of " +
                "sending RESET. Historical scripted evidence (B0-B T06) shows RESET + reuse for a " +
                "driver-known 42001 SyntaxError classification.",
            failing_connection = transcript.ToDocument(failingConnection),
        });
    }

    [Fact]
    [Trait("Task", "B5-B8")]
    public async Task B5B8_03_NoAutomaticRetry_WithAndWithoutExplicitRetryDisabling()
    {
        EvidenceWriter.WriteEnvironment();
        await using var serverDefault = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tapDefault = await WireTapProxy.StartAsync(serverDefault.Endpoint);

        var driverDefault = DriverFactory.Plaintext(tapDefault.HostPort);
        try
        {
            await DriverFactory.Bounded(driverDefault.VerifyConnectivityAsync());
            var session = driverDefault.AsyncSession();
            Neo4jException? failure = null;
            // The refusal surfaces at RunAsync or at the first consumption/disposal of the failed
            // result; a more derived Neo4jException subtype (ProtocolException) is expected.
            try
            {
                var cursor = await DriverFactory.Bounded(session.RunAsync(UnsupportedQuery));
                try
                {
                    await DriverFactory.Bounded(cursor.ConsumeAsync());
                }
                catch (Neo4jException ex)
                {
                    failure = ex;
                }
            }
            catch (Neo4jException ex)
            {
                failure = ex;
            }

            try
            {
                await session.DisposeAsync();
            }
            catch (Neo4jException ex)
            {
                failure ??= ex;
            }

            Assert.NotNull(failure);
            Assert.Equal("Neo.ClientError.Request.Invalid", failure!.Code);
        }
        finally
        {
            await driverDefault.DisposeAsync();
        }

        Assert.True(await tapDefault.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var defaultTranscript = WireTranscript.Build(tapDefault.SingleConnection());
        Assert.Equal(
            1,
            defaultTranscript.ClientMessages.Count(
                m => m.Name == "RUN" && m.Description.Contains(UnsupportedQuery, StringComparison.Ordinal)));

        await using var serverDisabled = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tapDisabled = await WireTapProxy.StartAsync(serverDisabled.Endpoint);
        var driverDisabled = DriverFactory.Plaintext(
            tapDisabled.HostPort,
            configure: o => o.WithDisableAutoCommitRetries(true));
        try
        {
            await DriverFactory.Bounded(driverDisabled.VerifyConnectivityAsync());
            var session = driverDisabled.AsyncSession();
            Neo4jException? failure = null;
            try
            {
                var cursor = await DriverFactory.Bounded(session.RunAsync(UnsupportedQuery));
                try
                {
                    await DriverFactory.Bounded(cursor.ConsumeAsync());
                }
                catch (Neo4jException ex)
                {
                    failure = ex;
                }
            }
            catch (Neo4jException ex)
            {
                failure = ex;
            }

            try
            {
                await session.DisposeAsync();
            }
            catch (Neo4jException ex)
            {
                failure ??= ex;
            }

            Assert.NotNull(failure);
            Assert.Equal("Neo.ClientError.Request.Invalid", failure!.Code);
        }
        finally
        {
            await driverDisabled.DisposeAsync();
        }

        Assert.True(await tapDisabled.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var disabledTranscript = WireTranscript.Build(tapDisabled.SingleConnection());
        Assert.Equal(
            1,
            disabledTranscript.ClientMessages.Count(
                m => m.Name == "RUN" && m.Description.Contains(UnsupportedQuery, StringComparison.Ordinal)));

        EvidenceWriter.Write("b5b8-03-no-automatic-retry", new
        {
            scenario = "B5B8-03",
            title = "A non-retriable 50N42 refusal is never retried, with and without explicit retry disabling",
            uri_scheme = "bolt",
            default_config_run_count = 1,
            retries_disabled_run_count = 1,
            notes = "The driver's automatic one-shot retry applies to retriable failures only; 50N42/Request.Invalid is non-retriable (IsRetriable=false).",
        });
    }

    [Fact]
    [Trait("Task", "B5-B8")]
    public async Task B5B8_04_FailureIsolation_SubsequentQueriesRemainCorrect()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);

        var driver = DriverFactory.Plaintext(server.HostPort);
        long scalar = -1;
        string? alice = null;
        string? bob = null;
        long since = -1;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());

            await using (var session = driver.AsyncSession())
            {
                try
                {
                    var cursor = await DriverFactory.Bounded(session.RunAsync(UnsupportedQuery));
                    await DriverFactory.Bounded(cursor.ConsumeAsync());
                    Assert.Fail("the unsupported query must not succeed");
                }
                catch (Neo4jException)
                {
                    // Expected refusal.
                }
            }

            await using (var session = driver.AsyncSession())
            {
                var cursor = await DriverFactory.Bounded(session.RunAsync("RETURN 42 AS answer"));
                Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
                scalar = cursor.Current["answer"].As<long>();
                await DriverFactory.Bounded(cursor.ConsumeAsync());
            }

            await using (var session = driver.AsyncSession())
            {
                var cursor = await DriverFactory.Bounded(
                    session.RunAsync("MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b"));
                Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
                alice = cursor.Current["a"].As<INode>().Properties["name"].As<string>();
                bob = cursor.Current["b"].As<INode>().Properties["name"].As<string>();
                since = cursor.Current["r"].As<IRelationship>().Properties["since"].As<long>();
                await DriverFactory.Bounded(cursor.ConsumeAsync());
            }
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.Equal(42, scalar);
        Assert.Equal("Alice", alice);
        Assert.Equal("Bob", bob);
        Assert.Equal(2024, since);

        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));
        var counters = server.Counters();
        Assert.Equal(0, counters.Failed);

        EvidenceWriter.Write("b5b8-04-failure-isolation", new
        {
            scenario = "B5B8-04",
            title = "Supported queries after a refusal return the correct reference values",
            uri_scheme = "bolt",
            driver_visible = new { scalar, alice, bob, since },
            counters = EvidenceWriter.CounterDocument(counters),
            server_events = EvidenceWriter.EventDocument(server.Events()),
        });
    }
}
