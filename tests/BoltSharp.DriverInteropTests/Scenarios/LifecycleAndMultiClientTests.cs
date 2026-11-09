using BoltSharp.DriverInteropTests.Infrastructure;
using Neo4j.Driver;
using Xunit;

// The legacy numeric Id member is obsolete in driver 6.x but is explicitly asserted in the
// multi-client scenario.
#pragma warning disable CS0618

namespace BoltSharp.DriverInteropTests.Scenarios;

/// <summary>
/// B5-B lifecycle and multi-client behavior: server shutdown during an active driver connection,
/// recovery after server-side connection loss, and two independent official drivers against one
/// server. Driver connection pooling is observed and recorded; it is never interpreted as
/// BoltSharp session concurrency.
/// </summary>
[Collection("B5-B")]
public sealed class LifecycleAndMultiClientTests
{
    [Fact]
    [Trait("Task", "B5-B9")]
    public async Task B5B9_01_ServerShutdown_DuringActiveDriverConnection()
    {
        EvidenceWriter.WriteEnvironment();
        var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);

        var driver = DriverFactory.Plaintext(server.HostPort);
        long beforeShutdown = -1;
        Exception? afterShutdown = null;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using (var session = driver.AsyncSession())
            {
                var cursor = await DriverFactory.Bounded(session.RunAsync("RETURN 42 AS answer"));
                Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
                beforeShutdown = cursor.Current["answer"].As<long>();
                await DriverFactory.Bounded(cursor.ConsumeAsync());
            }

            await server.DisposeAsync();
            Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));

            try
            {
                var cursor = await DriverFactory.Bounded(
                    driver.AsyncSession().RunAsync("RETURN 42 AS answer"),
                    TimeSpan.FromSeconds(15));
                await DriverFactory.Bounded(cursor.ConsumeAsync());
            }
            catch (Exception ex)
            {
                afterShutdown = ex;
            }
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.Equal(42, beforeShutdown);
        Assert.NotNull(afterShutdown);
        Assert.False(server.Server.IsRunning);
        Assert.Equal(0, server.Counters().Active);

        EvidenceWriter.Write("b5b9-01-server-shutdown", new
        {
            scenario = "B5B9-01",
            title = "Server shutdown during an active driver connection",
            uri_scheme = "bolt",
            driver_visible = new
            {
                before_shutdown = beforeShutdown,
                after_shutdown_failure = EvidenceWriter.ExceptionDocument(afterShutdown!),
            },
            counters = EvidenceWriter.CounterDocument(server.Counters()),
            server_events = EvidenceWriter.EventDocument(server.Events()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B9")]
    public async Task B5B9_02_MultiClient_TwoDrivers_IsolatedAndInterleaved()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driverA = DriverFactory.Plaintext(tap.HostPort);
        var driverB = DriverFactory.Plaintext(tap.HostPort);
        long a1 = -1;
        long b1 = -1;
        long b2 = -1;
        Exception? aFailure = null;
        long a2 = -1;
        try
        {
            await DriverFactory.Bounded(driverA.VerifyConnectivityAsync());
            await DriverFactory.Bounded(driverB.VerifyConnectivityAsync());

            // Interleaved independent sessions.
            await using var sessionA = driverA.AsyncSession();
            var cursorA = await DriverFactory.Bounded(sessionA.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverFactory.Bounded(cursorA.FetchAsync()));

            await using var sessionB = driverB.AsyncSession();
            var cursorB = await DriverFactory.Bounded(sessionB.RunAsync("MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b"));
            Assert.True(await DriverFactory.Bounded(cursorB.FetchAsync()));
            b1 = cursorB.Current["a"].As<INode>().Id;
            await DriverFactory.Bounded(cursorB.ConsumeAsync());

            a1 = cursorA.Current["answer"].As<long>();
            await DriverFactory.Bounded(cursorA.ConsumeAsync());

            // A's refused query must not disturb B.
            try
            {
                var refused = await DriverFactory.Bounded(sessionA.RunAsync("MATCH (n) DETACH DELETE n"));
                await DriverFactory.Bounded(refused.ConsumeAsync());
                Assert.Fail("the unsupported query must not succeed");
            }
            catch (Neo4jException ex)
            {
                aFailure = ex;
            }

            var cursorB2 = await DriverFactory.Bounded(sessionB.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverFactory.Bounded(cursorB2.FetchAsync()));
            b2 = cursorB2.Current["answer"].As<long>();
            await DriverFactory.Bounded(cursorB2.ConsumeAsync());

            // A recovers in the same session.
            var cursorA2 = await DriverFactory.Bounded(sessionA.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverFactory.Bounded(cursorA2.FetchAsync()));
            a2 = cursorA2.Current["answer"].As<long>();
            await DriverFactory.Bounded(cursorA2.ConsumeAsync());
        }
        finally
        {
            await driverA.DisposeAsync();
            await driverB.DisposeAsync();
        }

        Assert.Equal(42, a1);
        Assert.Equal(1, b1);
        Assert.Equal(42, b2);
        Assert.NotNull(aFailure);
        Assert.Equal(42, a2);

        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));
        var counters = server.Counters();
        Assert.True(counters.Accepted >= 2, "two independent drivers plus A's recovery connection");
        Assert.Equal(0, counters.Failed);

        EvidenceWriter.Write("b5b9-02-multi-client", new
        {
            scenario = "B5B9-02",
            title = "Two independent official drivers interleaved; failure isolation between clients",
            uri_scheme = "bolt",
            driver_visible = new
            {
                a_scalar = a1,
                b_alice_numeric_id = b1,
                b_after_a_failure = b2,
                a_failure = EvidenceWriter.ExceptionDocument(aFailure!),
                a_recovery = a2,
            },
            counters = EvidenceWriter.CounterDocument(counters),
            server_events = EvidenceWriter.EventDocument(server.Events()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B9")]
    public async Task B5B9_03_DriverRecovery_AfterServerSideConnectionLoss()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);

        var driver = DriverFactory.Plaintext(server.HostPort);
        long recovered = -1;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());

            // The accepted failure path destroys the driver's pooled connection after the refusal
            // (an unknown-classification ProtocolException). The driver must recover transparently
            // for the next supported query.
            await using (var session = driver.AsyncSession())
            {
                try
                {
                    var refused = await DriverFactory.Bounded(session.RunAsync("MATCH (n) DETACH DELETE n"));
                    await DriverFactory.Bounded(refused.ConsumeAsync());
                    Assert.Fail("the unsupported query must not succeed");
                }
                catch (Neo4jException)
                {
                    // Expected.
                }

                var cursor = await DriverFactory.Bounded(session.RunAsync("RETURN 42 AS answer"));
                Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
                recovered = cursor.Current["answer"].As<long>();
                await DriverFactory.Bounded(cursor.ConsumeAsync());
            }
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.Equal(42, recovered);
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));
        var counters = server.Counters();
        Assert.Equal(2, counters.Accepted);
        Assert.Equal(0, counters.Failed);

        EvidenceWriter.Write("b5b9-03-recovery-after-connection-loss", new
        {
            scenario = "B5B9-03",
            title = "Driver transparently replaces a destroyed connection for the next supported query",
            uri_scheme = "bolt",
            driver_visible = new { recovered_value = recovered },
            counters = EvidenceWriter.CounterDocument(counters),
            server_events = EvidenceWriter.EventDocument(server.Events()),
        });
    }
}
