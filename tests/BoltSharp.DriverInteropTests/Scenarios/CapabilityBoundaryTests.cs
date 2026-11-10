using BoltSharp.DriverInteropTests.Infrastructure;
using BoltSharp.Transport;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.DriverInteropTests.Scenarios;

/// <summary>
/// Explicit capability boundaries: features deliberately outside the accepted BoltSharp scope
/// (explicit transactions, managed transaction callbacks, routing, explicit database selection)
/// are exercised through the official driver to document the exact refusal behavior. Nothing here
/// is claimed as support; the reference backend's allowlist is preserved untouched.
/// <para>
/// <b>Finding B5-B-IMPL-F01 (recorded here, no production change).</b> BEGIN / COMMIT / ROLLBACK /
/// ROUTE are refused by the accepted B5-A transport at the B3-A decode boundary: the transport
/// closes the connection without the B3-B documented 08N06 FAILURE, so the driver surfaces a
/// connection failure (ServiceUnavailableException) rather than the richer refusal. Codec-level
/// malformed-message rejections behave the same way. The refused operation still never executes
/// and no graph state changes. The smallest remediation (a candidate for a separately authorized
/// change) is to route complete payloads through the accepted B3-B <c>ReceiveBytes</c> entry
/// point, which maps unsupported and malformed messages to the documented terminal FAILURE.
/// </para>
/// </summary>
[Collection("B5-B")]
public sealed class CapabilityBoundaryTests
{
    private const string ScalarQuery = "RETURN 42 AS answer";

    [Fact]
    [Trait("Task", "B5-B10")]
    public async Task B5B10_01_ExplicitTransaction_Refused_WithoutExecution()
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
            await using (var session = driver.AsyncSession())
            {
                try
                {
                    await DriverFactory.Bounded(session.BeginTransactionAsync());
                    Assert.Fail("explicit transactions are not supported and must not begin");
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            }

            await using (var session = driver.AsyncSession())
            {
                var cursor = await DriverFactory.Bounded(session.RunAsync(ScalarQuery));
                Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
                recovered = cursor.Current["answer"].As<long>();
                await DriverFactory.Bounded(cursor.ConsumeAsync());
            }
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.NotNull(failure);
        Assert.Equal(42, recovered);

        Assert.True(await tap.WaitForClosedConnectionsAsync(2, TimeSpan.FromSeconds(10)));
        var (beginConnection, transcript) = tap.FindConnection(t => t.CountClient("BEGIN") == 1);
        var begin = Assert.Single(transcript.ClientMessages, m => m.Name == "BEGIN");
        Assert.Equal("B111A0", begin.Hex);
        Assert.Equal(0, transcript.CountClient("RUN"));
        // B5-B-F01-R1: BEGIN now receives the accepted B3-B terminal FAILURE (08N06) through the
        // transport; the connection then closes without any transaction or provider operation.
        var beginFailure = Assert.Single(transcript.ServerMessages, m => m.Name == "FAILURE");
        Assert.Contains("gql_status=08N06", beginFailure.Description);
        Assert.Contains("Neo.ClientError.Request.Invalid", beginFailure.Description);
        Assert.Contains("Explicit transactions and routing are not supported", beginFailure.Description);
        Assert.NotEqual("open", beginConnection.ClientSideClose);
        Assert.NotEqual("open", beginConnection.ServerSideClose);

        // Server-side: BEGIN was processed through the accepted session path and ended the
        // connection; the only RUN is the recovery query (on the replacement connection proven
        // above).
        var events = server.Events();
        Assert.Contains(
            events,
            e => e.Kind == BoltTransportEventKind.MessageProcessed
                && e.Detail!.Contains("sig=0x11")
                && e.Detail!.Contains("state=Defunct"));
        var runEvents = events.Count(e => e.Kind == BoltTransportEventKind.MessageProcessed && e.Detail!.Contains("sig=0x10"));
        Assert.Equal(1, runEvents);

        EvidenceWriter.Write("b5b10-01-explicit-transaction", new
        {
            scenario = "B5B10-01",
            title = "Explicit transaction BEGIN refused through the accepted session path (08N06); no execution; no mutation",
            uri_scheme = "bolt",
            driver_failure = EvidenceWriter.ExceptionDocument(failure!),
            begin_hex = begin.Hex,
            begin_connection = transcript.ToDocument(beginConnection),
            finding = "B5-B-F01-R1: the transport now delivers the accepted B3-B unsupported-command " +
                "terminal FAILURE (gql_status=08N06, Neo.ClientError.Request.Invalid) and then closes.",
            driver_visible = new { recovered_scalar = recovered },
            counters = EvidenceWriter.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B10")]
    public async Task B5B10_02_ManagedTransactionCallback_Refused()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(
            tap.HostPort,
            configure: o => o.WithMaxTransactionRetryTime(TimeSpan.FromSeconds(1)));
        Exception? failure = null;
        long recovered = -1;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using (var session = driver.AsyncSession())
            {
                try
                {
                    await DriverFactory.Bounded(session.ExecuteReadAsync(async runner =>
                    {
                        var cursor = await runner.RunAsync(ScalarQuery);
                        return await cursor.ConsumeAsync();
                    }));
                    Assert.Fail("managed transactions are not supported by the current reference backend");
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            }

            // The graph is untouched and the driver remains usable with auto-commit.
            await using (var session = driver.AsyncSession())
            {
                var cursor = await DriverFactory.Bounded(session.RunAsync(ScalarQuery));
                Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
                recovered = cursor.Current["answer"].As<long>();
                await DriverFactory.Bounded(cursor.ConsumeAsync());
            }
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.NotNull(failure);
        Assert.Equal(42, recovered);
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));

        // Every managed attempt starts with BEGIN, receives the accepted 08N06 terminal FAILURE
        // (B5-B-F01-R1) and is refused before any RUN executes.
        var beginAttempts = tap.Analyze().Count(a => a.Transcript.CountClient("BEGIN") >= 1);
        Assert.True(beginAttempts >= 1, "the driver must attempt BEGIN for a managed transaction");
        Assert.All(
            tap.Analyze().Where(a => a.Transcript.CountClient("BEGIN") >= 1),
            a => Assert.Equal(0, a.Transcript.CountClient("RUN")));
        Assert.All(
            tap.Analyze().Where(a => a.Transcript.CountClient("BEGIN") >= 1),
            a => Assert.Contains(
                a.Transcript.ServerMessages,
                m => m.Name == "FAILURE" && m.Description.Contains("gql_status=08N06", StringComparison.Ordinal)));

        var runEvents = server.Events().Count(
            e => e.Kind == BoltTransportEventKind.MessageProcessed && e.Detail!.Contains("sig=0x10"));
        Assert.Equal(1, runEvents); // only the explicit recovery query

        EvidenceWriter.Write("b5b10-02-managed-transaction", new
        {
            scenario = "B5B10-02",
            title = "Managed transaction callback refused through the accepted session path (08N06); auto-commit unaffected",
            uri_scheme = "bolt",
            driver_failure = EvidenceWriter.ExceptionDocument(failure!),
            begin_attempt_connections = beginAttempts,
            run_processed = runEvents,
            notes = "With a 1s transaction retry bound the driver exhausts its retries quickly; each BEGIN " +
                "attempt receives the accepted B3-B unsupported-command terminal FAILURE (08N06) through the " +
                "transport (B5-B-F01-R1) and never reaches RUN.",
            counters = EvidenceWriter.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B10")]
    public async Task B5B10_03_RoutingScheme_Refused_NoSilentSuccess()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.RoutingScheme(tap.HostPort);
        Exception? failure = null;
        try
        {
            var cursor = await DriverFactory.Bounded(
                driver.AsyncSession().RunAsync(ScalarQuery),
                TimeSpan.FromSeconds(15));
            await DriverFactory.Bounded(cursor.ConsumeAsync());
            Assert.Fail("routing is not supported and must not silently succeed");
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.NotNull(failure);
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));

        // The driver attempted ROUTE (0x66) and never executed a query.
        Assert.Contains(
            server.Events(),
            e => e.Kind == BoltTransportEventKind.MessageProcessed && e.Detail!.Contains("sig=0x01"));
        Assert.DoesNotContain(
            server.Events(),
            e => e.Kind == BoltTransportEventKind.MessageProcessed && e.Detail!.Contains("sig=0x10"));

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var (routeConnection, transcript) = tap.FindConnection(t => t.CountClient("ROUTE") == 1);
        Assert.Equal(0, transcript.CountClient("RUN"));
        // B5-B-F01-R1: ROUTE now receives the accepted B3-B terminal FAILURE (08N06) through the
        // transport instead of a bare decode-boundary closure.
        var routeFailure = Assert.Single(transcript.ServerMessages, m => m.Name == "FAILURE");
        Assert.Contains("gql_status=08N06", routeFailure.Description);
        Assert.Contains("Neo.ClientError.Request.Invalid", routeFailure.Description);

        EvidenceWriter.Write("b5b10-03-routing-scheme", new
        {
            scenario = "B5B10-03",
            title = "neo4j:// routing attempt: ROUTE refused through the accepted session path (08N06), no query executed",
            uri_scheme = "neo4j",
            driver_failure = EvidenceWriter.ExceptionDocument(failure!),
            route_connection = transcript.ToDocument(routeConnection),
            finding = "B5-B-F01-R1: ROUTE receives the accepted B3-B unsupported-command terminal FAILURE.",
            counters = EvidenceWriter.CounterDocument(server.Counters()),
            server_events = EvidenceWriter.EventDocument(server.Events()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B10")]
    public async Task B5B10_04_ExplicitDatabaseSelection_Refused_AllowlistPreserved()
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
            await using (var session = driver.AsyncSession(o => o.WithDatabase("neo4j")))
            {
                try
                {
                    var cursor = await DriverFactory.Bounded(session.RunAsync(ScalarQuery));
                    await DriverFactory.Bounded(cursor.ConsumeAsync());
                    Assert.Fail("multi-database selection is not supported and must not silently succeed");
                }
                catch (Neo4jException ex)
                {
                    failure = ex;
                }
            }

            // The single-database allowlist is unchanged: the default session still works.
            await using (var session = driver.AsyncSession())
            {
                var cursor = await DriverFactory.Bounded(session.RunAsync(ScalarQuery));
                Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
                recovered = cursor.Current["answer"].As<long>();
                await DriverFactory.Bounded(cursor.ConsumeAsync());
            }
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.NotNull(failure);
        Assert.Equal(42, recovered);

        Assert.True(await tap.WaitForClosedConnectionsAsync(2, TimeSpan.FromSeconds(10)));
        var (refusedConnection, refusedTranscript) = tap.FindConnection(t => t.CountServer("FAILURE") == 1);
        var run = Assert.Single(refusedTranscript.ClientMessages, m => m.Name == "RUN");
        Assert.Equal("RUN \"RETURN 42 AS answer\" parameters=0 entry(ies) extra={db=neo4j}", run.Description);
        var refusal = Assert.Single(refusedTranscript.ServerMessages, m => m.Name == "FAILURE");
        Assert.Contains("Neo.ClientError.Request.Invalid", refusal.Description);

        EvidenceWriter.Write("b5b10-04-explicit-database", new
        {
            scenario = "B5B10-04",
            title = "Explicit database selection refused by the accepted RUN-option allowlist",
            uri_scheme = "bolt",
            driver_failure = EvidenceWriter.ExceptionDocument(failure!),
            refused_run_hex = run.Hex,
            refusal = refusal.Description,
            refused_connection = refusedTranscript.ToDocument(refusedConnection),
            driver_visible = new { default_session_recovered = recovered },
            counters = EvidenceWriter.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B-F01-R1")]
    public async Task B5B10_05_BeginRefusal_DriverObservesAcceptedProtocolFailure()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(tap.HostPort);
        Neo4jException? failure = null;
        long recovered = -1;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using (var session = driver.AsyncSession())
            {
                try
                {
                    await DriverFactory.Bounded(session.BeginTransactionAsync());
                    Assert.Fail("explicit transactions are not supported and must not begin");
                }
                catch (Neo4jException ex)
                {
                    failure = ex;
                }
            }

            // The driver remains usable through the normal auto-commit path.
            await using (var session = driver.AsyncSession())
            {
                var cursor = await DriverFactory.Bounded(session.RunAsync(ScalarQuery));
                Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
                recovered = cursor.Current["answer"].As<long>();
                await DriverFactory.Bounded(cursor.ConsumeAsync());
            }
        }
        finally
        {
            await driver.DisposeAsync();
        }

        // B5-B-F01-R1: the official driver now observes the accepted B3-B protocol-level refusal
        // (08N06 / Neo.ClientError.Request.Invalid) instead of an unexplained connection failure.
        Assert.NotNull(failure);
        Assert.Equal("Neo.ClientError.Request.Invalid", failure!.Code);
        Assert.Equal("08N06", failure.GqlStatus);
        Assert.False(failure.IsRetriable);
        Assert.Equal(42, recovered);

        Assert.True(await tap.WaitForClosedConnectionsAsync(2, TimeSpan.FromSeconds(10)));
        var (beginConnection, transcript) = tap.FindConnection(t => t.CountClient("BEGIN") == 1);
        var begin = Assert.Single(transcript.ClientMessages, m => m.Name == "BEGIN");
        Assert.Equal("B111A0", begin.Hex);
        Assert.Equal(0, transcript.CountClient("RUN"));
        var refusal = Assert.Single(transcript.ServerMessages, m => m.Name == "FAILURE");
        Assert.Contains("gql_status=08N06", refusal.Description);
        Assert.Contains("Explicit transactions and routing are not supported", refusal.Description);

        // The BEGIN connection completed with a terminal FAILURE (no transport fault); the only
        // RUN is the recovery query on the replacement connection.
        var counters = server.Counters();
        Assert.Equal(0, counters.Failed);
        Assert.Contains(
            server.Events(),
            e => e.Kind == BoltTransportEventKind.MessageProcessed && e.Detail!.Contains("sig=0x11"));

        EvidenceWriter.Write("b5b10-05-begin-refusal-driver-visible", new
        {
            scenario = "B5B10-05",
            title = "Corrected BEGIN refusal: official driver observes the accepted 08N06 protocol failure",
            uri_scheme = "bolt",
            driver_failure = EvidenceWriter.ExceptionDocument(failure),
            begin_hex = begin.Hex,
            begin_connection = transcript.ToDocument(beginConnection),
            driver_visible = new
            {
                exception_type = failure.GetType().FullName,
                neo4j_code = failure.Code,
                gql_status = failure.GqlStatus,
                retriable = failure.IsRetriable,
                recovered_scalar = recovered,
            },
            counters = EvidenceWriter.CounterDocument(counters),
        });
    }
}
