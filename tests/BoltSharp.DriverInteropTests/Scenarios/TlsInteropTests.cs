using BoltSharp.DriverInteropTests.Infrastructure;
using BoltSharp.Transport;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.DriverInteropTests.Scenarios;

/// <summary>
/// B5-B7 — TLS URI interoperability against the real B5-A listener. Self-signed TLS is exercised
/// through <c>bolt+ssc://</c>; CA-trusted validation is exercised with an isolated in-memory trust
/// list (never a machine trust store). Negative controls verify that no downgrade path exists and
/// that untrusted certificates and invalid credentials are rejected.
/// </summary>
[Collection("B5-B")]
public sealed class TlsInteropTests
{
    [Fact]
    [Trait("Task", "B5-B7")]
    public async Task B5B7_01_BoltSsc_SelfSigned_FullQueryFlow()
    {
        EvidenceWriter.WriteEnvironment();
        using var certificate = TestCertificates.CreateSelfSignedLocalhost();
        await using var server = await B5BServerHost.StartTlsAsync(certificate, TestCredentials.Verifier);

        var driver = DriverFactory.SelfSignedTls(server.HostPort);
        long answer = -1;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            var cursor = await DriverFactory.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
            answer = cursor.Current["answer"].As<long>();
            await DriverFactory.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.Equal(42, answer);
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0 && c.Completed == 1, TimeSpan.FromSeconds(10)));
        var counters = server.Counters();
        Assert.Equal(1, counters.Accepted);
        Assert.Equal(0, counters.TlsHandshakeFailures);
        Assert.Equal(0, counters.Failed);
        Assert.Contains(server.Events(), e => e.Kind == BoltTransportEventKind.TlsHandshakeCompleted);
        Assert.Contains(server.Events(), e => e.Kind == BoltTransportEventKind.BoltHandshakeCompleted);

        EvidenceWriter.Write("b5b7-01-bolt-ssc-self-signed", new
        {
            scenario = "B5B7-01",
            title = "bolt+ssc:// against the real TLS listener with a self-signed certificate",
            uri_scheme = "bolt+ssc",
            driver_visible = new { answer },
            tls = new
            {
                certificate = "ephemeral self-signed RSA-2048, CN=localhost, SAN localhost/127.0.0.1",
                trust = "driver scheme semantics: accepts any server certificate (+ssc)",
            },
            counters = EvidenceWriter.CounterDocument(counters),
            server_events = EvidenceWriter.EventDocument(server.Events()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B7")]
    public async Task B5B7_02_BoltS_SystemTrust_RejectsTestCertificate()
    {
        EvidenceWriter.WriteEnvironment();
        using var certificate = TestCertificates.CreateSelfSignedLocalhost();
        await using var server = await B5BServerHost.StartTlsAsync(certificate, TestCredentials.Verifier);

        var driver = DriverFactory.SystemTrustTls(server.HostPort);
        Exception? failure = null;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
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
        // No Bolt byte was ever processed: the TLS trust failure happens before the handshake.
        Assert.Equal(0, server.Counters().MessagesProcessed);
        Assert.DoesNotContain(server.Events(), e => e.Kind == BoltTransportEventKind.BoltHandshakeCompleted);

        // The listener remains healthy for the correct trust arrangement.
        var healthyDriver = DriverFactory.SelfSignedTls(server.HostPort);
        try
        {
            await DriverFactory.Bounded(healthyDriver.VerifyConnectivityAsync());
        }
        finally
        {
            await healthyDriver.DisposeAsync();
        }

        EvidenceWriter.Write("b5b7-02-bolt-s-untrusted", new
        {
            scenario = "B5B7-02",
            title = "bolt+s:// always validates against the system CA store: test certificate rejected",
            uri_scheme = "bolt+s",
            driver_failure = EvidenceWriter.ExceptionDocument(failure!),
            counters = EvidenceWriter.CounterDocument(server.Counters()),
            server_events = EvidenceWriter.EventDocument(server.Events()),
            note = "No machine trust store was modified; the test certificate is intentionally untrusted.",
        });
    }

    [Fact]
    [Trait("Task", "B5-B7")]
    public async Task B5B7_03_CaTrustedTls_IsolatedTrustListArrangement()
    {
        EvidenceWriter.WriteEnvironment();
        using var ca = TestCertificates.CreateTestCa();
        using var leaf = TestCertificates.CreateCaSignedLocalhost(ca);
        await using var server = await B5BServerHost.StartTlsAsync(leaf, TestCredentials.Verifier);

        var driver = DriverFactory.TrustListTls(server.HostPort, ca);
        long answer = -1;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            var cursor = await DriverFactory.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
            answer = cursor.Current["answer"].As<long>();
            await DriverFactory.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.Equal(42, answer);
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0 && c.Completed == 1, TimeSpan.FromSeconds(10)));
        Assert.Equal(0, server.Counters().TlsHandshakeFailures);

        EvidenceWriter.Write("b5b7-03-ca-trusted-trust-list", new
        {
            scenario = "B5B7-03",
            title = "CA-trusted TLS with an isolated in-memory trust list",
            uri_scheme = "bolt + EncryptionLevel.Encrypted + CertificateTrustRule.TrustList",
            driver_visible = new { answer },
            tls = new
            {
                certificate = "ephemeral RSA-2048 leaf signed by an in-memory test CA (no chain committed)",
                trust = "documented driver TrustList rule: chain must validate to the supplied CA certificate",
            },
            qualification = "The literal bolt+s:// scheme always validates against the operating system " +
                "trust store and ignores the trust list (documented driver behavior), so it cannot be " +
                "used with a test-only CA without an intrusive, permanent machine trust change. This " +
                "trust-list arrangement is the reproducible isolated alternative and the reason " +
                "bolt+s:// itself is recorded as an explicit trust-arrangement limitation.",
            counters = EvidenceWriter.CounterDocument(server.Counters()),
            server_events = EvidenceWriter.EventDocument(server.Events()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B7")]
    public async Task B5B7_04_PlaintextAgainstTlsListener_FailsClosed_NoDowngrade()
    {
        EvidenceWriter.WriteEnvironment();
        using var certificate = TestCertificates.CreateSelfSignedLocalhost();
        await using var server = await B5BServerHost.StartTlsAsync(certificate, TestCredentials.Verifier);

        var driver = DriverFactory.Plaintext(server.HostPort);
        Exception? failure = null;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
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
        var counters = server.Counters();
        Assert.Equal(0, counters.MessagesProcessed);
        Assert.True(counters.TlsHandshakeFailures >= 1, "the server must count the failed TLS authentication");

        // Healthy TLS client afterwards proves the listener was unaffected.
        var healthyDriver = DriverFactory.SelfSignedTls(server.HostPort);
        try
        {
            await DriverFactory.Bounded(healthyDriver.VerifyConnectivityAsync());
        }
        finally
        {
            await healthyDriver.DisposeAsync();
        }

        EvidenceWriter.Write("b5b7-04-plaintext-vs-tls", new
        {
            scenario = "B5B7-04",
            title = "Plaintext bolt:// against a TLS-only listener fails closed; no downgrade",
            uri_scheme = "bolt",
            driver_failure = EvidenceWriter.ExceptionDocument(failure!),
            counters = EvidenceWriter.CounterDocument(server.Counters()),
            server_events = EvidenceWriter.EventDocument(server.Events()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B7")]
    public async Task B5B7_05_InvalidCredentials_OverTls_RejectedWithoutDowngrade()
    {
        EvidenceWriter.WriteEnvironment();
        using var certificate = TestCertificates.CreateSelfSignedLocalhost();
        await using var server = await B5BServerHost.StartTlsAsync(certificate, TestCredentials.Verifier);

        var driver = DriverFactory.SelfSignedTls(
            server.HostPort,
            AuthTokens.Basic(TestCredentials.User, "wrong-tls-password"));
        Exception? failure = null;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await driver.DisposeAsync();
        }

        var neo4jFailure = Assert.IsAssignableFrom<Neo4jException>(failure);
        Assert.Equal("Neo.ClientError.Security.Unauthorized", neo4jFailure.Code);

        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));
        var counters = server.Counters();
        // HELLO + LOGON over the TLS stream; no query, no plaintext fallback.
        Assert.Equal(2, counters.MessagesProcessed);
        Assert.Equal(0, counters.TlsHandshakeFailures);
        Assert.Contains(server.Events(), e => e.Kind == BoltTransportEventKind.TlsHandshakeCompleted);

        EvidenceWriter.Write("b5b7-05-invalid-credentials-tls", new
        {
            scenario = "B5B7-05",
            title = "Invalid credentials rejected inside TLS; no protocol downgrade",
            uri_scheme = "bolt+ssc",
            driver_failure = EvidenceWriter.ExceptionDocument(failure!),
            counters = EvidenceWriter.CounterDocument(counters),
            server_events = EvidenceWriter.EventDocument(server.Events()),
        });
    }
}
