using BoltSharp.B5BQ1.Probes.Infrastructure;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.B5BQ1.Probes.Scenarios;

/// <summary>
/// Q1-G: TLS interoperability. Self-signed (bolt+ssc), untrusted system-trust rejection (bolt+s),
/// and the isolated driver trust-list arrangement are verified as three distinct paths, plus the
/// negative controls. No machine trust store is ever modified.
/// </summary>
[Collection("Q1")]
public sealed class Q1TlsProbes
{
    [Fact]
    public async Task Q1G01_SelfSignedTls_FullAuthenticatedConnectionAndScalarQuery()
    {
        var certificate = Q1Certificates.CreateSelfSignedLocalhost();
        await using var server = await Q1Server.StartTlsAsync(certificate, Q1Credentials.Verifier);

        var driver = Q1Driver.SelfSignedTls(server.HostPort);
        long answer = 0;
        IServerInfo? info = null;
        try
        {
            await Q1Driver.Bounded(driver.VerifyConnectivityAsync());
            info = await Q1Driver.Bounded(driver.GetServerInfoAsync());
            await using var session = driver.AsyncSession();
            var cursor = await Q1Driver.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await Q1Driver.Bounded(cursor.FetchAsync()));
            answer = cursor.Current["answer"].As<long>();
            await Q1Driver.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.Equal(42L, answer);
        Assert.NotNull(info);
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0 && c.Completed >= 1, TimeSpan.FromSeconds(10)));
        var counters = server.Counters();
        Assert.Equal(1, counters.Accepted);
        Assert.Equal(0, counters.TlsHandshakeFailures);
        Assert.Equal(1, server.CountProcessedSignatures("0x10"));

        Q1Evidence.Write("q1g01-bolt-ssc-self-signed", new
        {
            probe = "Q1-G01",
            uri_scheme = "bolt+ssc",
            driver_visible = new { answer, server_agent = info!.Agent },
            counters = Q1Evidence.CounterDocument(counters),
        });
    }

    [Fact]
    public async Task Q1G02_SystemTrustTls_UntrustedCertificate_Rejected_NoBoltByte()
    {
        var certificate = Q1Certificates.CreateSelfSignedLocalhost();
        await using var server = await Q1Server.StartTlsAsync(certificate, Q1Credentials.Verifier);

        var driver = Q1Driver.SystemTrustTls(server.HostPort);
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
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));
        var counters = server.Counters();
        // No Bolt byte was processed: the TLS layer rejected the certificate first.
        Assert.Equal(0, counters.MessagesProcessed);

        Q1Evidence.Write("q1g02-bolt-s-untrusted", new
        {
            probe = "Q1-G02",
            uri_scheme = "bolt+s",
            driver_failure = Q1Driver.ExceptionDocument(failure!),
            counters = Q1Evidence.CounterDocument(counters),
            note = "bolt+s validates against the OS trust store only; a test certificate is rejected. " +
                   "This is the distinct system-trust path and is NOT proof of the trust-list arrangement below.",
        });
    }

    [Fact]
    public async Task Q1G03_TrustListArrangement_CaSignedLeaf_EncryptedConnectivity()
    {
        var ca = Q1Certificates.CreateTestCa();
        var leaf = Q1Certificates.CreateCaSignedLocalhost(ca);
        await using var server = await Q1Server.StartTlsAsync(leaf, Q1Credentials.Verifier);

        // The driver's documented trust-list configuration on a plain bolt:// URI: an isolated,
        // reproducible CA-trust arrangement that never touches the machine trust store.
        var driver = Q1Driver.TrustListTls(server.HostPort, ca);
        long answer = 0;
        try
        {
            await Q1Driver.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            var cursor = await Q1Driver.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await Q1Driver.Bounded(cursor.FetchAsync()));
            answer = cursor.Current["answer"].As<long>();
            await Q1Driver.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.Equal(42L, answer);
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0 && c.Completed >= 1, TimeSpan.FromSeconds(10)));
        Assert.Equal(0, server.Counters().TlsHandshakeFailures);

        Q1Evidence.Write("q1g03-ca-trust-list", new
        {
            probe = "Q1-G03",
            uri_scheme = "bolt (WithEncryptionLevel Encrypted + CertificateTrustRule TrustList)",
            driver_visible = new { answer },
            note = "Isolated driver trust-list arrangement; distinct from bolt+s system-store validation.",
            counters = Q1Evidence.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    public async Task Q1G04_NegativeControl_PlaintextClientAgainstTlsListener_FailsClosed()
    {
        var certificate = Q1Certificates.CreateSelfSignedLocalhost();
        await using var server = await Q1Server.StartTlsAsync(certificate, Q1Credentials.Verifier);

        var driver = Q1Driver.Plaintext(server.HostPort);
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
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));
        var counters = server.Counters();
        Assert.Equal(0, counters.MessagesProcessed);
        Assert.True(counters.TlsHandshakeFailures >= 1);
        // No downgrade: no Bolt message was processed, the listener stayed in TLS-only mode.
        Assert.Equal(0, server.CountProcessedSignatures("0x01"));

        Q1Evidence.Write("q1g04-plaintext-vs-tls", new
        {
            probe = "Q1-G04",
            driver_failure = Q1Driver.ExceptionDocument(failure!),
            counters = Q1Evidence.CounterDocument(counters),
        });
    }

    [Fact]
    public async Task Q1G05_NegativeControl_TlsClientAgainstPlaintextListener_FailsClosed()
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);

        var driver = Q1Driver.SelfSignedTls(server.HostPort);
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
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));
        var counters = server.Counters();
        Assert.Equal(0, counters.MessagesProcessed);

        Q1Evidence.Write("q1g05-tls-vs-plaintext", new
        {
            probe = "Q1-G05",
            driver_failure = Q1Driver.ExceptionDocument(failure!),
            counters = Q1Evidence.CounterDocument(counters),
        });
    }

    [Fact]
    public async Task Q1G06_NegativeControl_InvalidCredentialsOverTls_Rejected_NoDowngrade()
    {
        var certificate = Q1Certificates.CreateSelfSignedLocalhost();
        await using var server = await Q1Server.StartTlsAsync(certificate, Q1Credentials.Verifier);

        var driver = Q1Driver.SelfSignedTls(server.HostPort, Q1Driver.WrongToken());
        Exception? failure = null;
        try
        {
            await Q1Driver.Bounded(driver.VerifyConnectivityAsync());
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
        var neo4jFailure = Assert.IsAssignableFrom<Neo4jException>(failure);
        Assert.Equal("Neo.ClientError.Security.Unauthorized", neo4jFailure.Code);
        Assert.Equal(2, server.Counters().MessagesProcessed);

        Q1Evidence.Write("q1g06-invalid-credentials-tls", new
        {
            probe = "Q1-G06",
            driver_failure = Q1Driver.ExceptionDocument(failure!),
            counters = Q1Evidence.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    public async Task Q1G07_NegotiatedTlsVersion_Observable()
    {
        var certificate = Q1Certificates.CreateSelfSignedLocalhost();
        await using var server = await Q1Server.StartTlsAsync(certificate, Q1Credentials.Verifier);

        var driver = Q1Driver.SelfSignedTls(server.HostPort);
        try
        {
            await Q1Driver.Bounded(driver.VerifyConnectivityAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));
        var tlsCompleted = server.Events().FirstOrDefault(e => e.Kind == BoltSharp.Transport.BoltTransportEventKind.TlsHandshakeCompleted);
        Assert.NotNull(tlsCompleted);
        Assert.False(string.IsNullOrWhiteSpace(tlsCompleted!.Detail));

        Q1Evidence.Write("q1g07-tls-version", new
        {
            probe = "Q1-G07",
            negotiated_tls = tlsCompleted.Detail,
        });
    }
}
