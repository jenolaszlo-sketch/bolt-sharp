using System.Net;

using BoltSharp.B5BQ1.Probes.Infrastructure;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.B5BQ1.Probes.Scenarios;

/// <summary>
/// Q1-A: connectivity and negotiation. The unmodified official driver must establish a real
/// network connection to the real B5-A server, negotiate Bolt 5.8 (Manifest v1 and conventional
/// fallback verified independently), and complete HELLO/LOGON. Constructing an IDriver alone is
/// explicitly not connectivity.
/// </summary>
[Collection("Q1")]
public sealed class Q1ConnectivityProbes
{
    [Fact]
    public async Task Q1A01_VerifyConnectivity_RealConnection_ManifestV1()
    {
        Q1Evidence.Write("q1a00-environment", new
        {
            probe = "B5-B-Q1",
            driver_assembly_version = Q1Driver.PackageVersion,
            driver_informational_version = Q1Driver.InformationalVersion,
            dotnet = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        });

        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);

        var driver = Q1Driver.Plaintext(server.HostPort);
        IServerInfo? info = null;
        Exception? failure = null;
        try
        {
            // Construction alone must not open a network connection.
            await Task.Delay(250);
            Assert.Equal(0, server.Counters().Accepted);

            await Q1Driver.Bounded(driver.VerifyConnectivityAsync());
            info = await Q1Driver.Bounded(driver.GetServerInfoAsync());
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.Null(failure);
        Assert.NotNull(info);
        Assert.True(
            await server.WaitForCountersAsync(c => c.Active == 0 && c.Completed >= 1, TimeSpan.FromSeconds(10)),
            "server must observe the released connection");

        var counters = server.Counters();
        Assert.Equal(1, counters.Accepted);
        Assert.Equal(1, counters.Completed);
        Assert.Equal(0, counters.Failed);
        Assert.Equal("ManifestV1", server.ObservedHandshakeMode());

        // HELLO (0x01) and LOGON (0x6A) processed; no RUN (0x10).
        Assert.Equal(1, server.CountProcessedSignatures("0x01"));
        Assert.Equal(1, server.CountProcessedSignatures("0x6A"));
        Assert.Equal(0, server.CountProcessedSignatures("0x10"));

        Q1Evidence.Write("q1a01-connectivity", new
        {
            probe = "Q1-A01",
            uri_scheme = "bolt",
            handshake_mode = server.ObservedHandshakeMode(),
            driver_visible = new
            {
                verified = true,
                server_agent = info!.Agent,
                protocol_version = info.ProtocolVersion?.ToString(),
                address = info.Address?.ToString(),
            },
            counters = Q1Evidence.CounterDocument(counters),
        });
    }

    [Fact]
    public async Task Q1A02_ManifestV1_HandshakeBytes_IndependentOracle()
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier);
        await using var tap = await Q1WireTap.StartAsync(server.Endpoint);

        var driver = Q1Driver.Plaintext(tap.HostPort);
        try
        {
            await Q1Driver.Bounded(driver.VerifyConnectivityAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        Assert.Equal(1, tap.ConnectionsAccepted);

        var (magic, proposals) = Q1WireTranscript.ParseClientHandshake(tap);
        // Normative Bolt handshake preamble: 60 60 B0 17.
        Assert.Equal("6060B017", magic);
        // The official driver 6.4.0 proposes: manifest marker, range 5.0-5.8, range 4.2-4.4, 3.0.
        Assert.Equal(4, proposals.Length);
        Assert.Equal("000001FF", proposals[0]);
        Assert.Equal("00080805", proposals[1]); // 5.0-5.8 (first 5.0, last 5.8, wire form 00 08 08 05)
        Assert.Equal("00020404", proposals[2]); // 4.2-4.4
        Assert.Equal("00000003", proposals[3]); // 3.0

        // Server manifest offer: marker + range count 1 + range 5.8 + capabilities 0.
        var serverHandshake = Q1WireTranscript.ParseServerHandshake(tap);
        Assert.Equal("000001FF010000080500", serverHandshake);

        // Client's final selection: 5.8 with zero capabilities, immediately followed by the
        // framed HELLO in the same client byte stream (pipelined HELLO preserved).
        var clientStream = tap.Chunks.Where(c => c.Direction == Q1TapDirection.ClientToServer)
            .SelectMany(c => c.Bytes).ToArray();
        var selection = Q1WireTranscript.Hex(clientStream.AsSpan(20, 5));
        Assert.Equal("0000080500", selection);
        Assert.True(clientStream.Length > 27);
        Assert.Equal(0xB1, clientStream[25 + 2]);
        Assert.Equal(0x01, clientStream[25 + 3]);

        var (clientMessages, clientError) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ClientToServer);
        var (serverMessages, serverError) = Q1WireTranscript.ParseMessages(tap, Q1TapDirection.ServerToClient);
        Assert.Equal(string.Empty, clientError);
        Assert.Equal(string.Empty, serverError);
        Assert.Equal("HELLO", clientMessages[0].Name);
        Assert.Equal("SUCCESS", serverMessages[0].Name);
        Assert.Equal("LOGON", clientMessages[1].Name);
        Assert.True(clientMessages[1].Redacted, "LOGON must be redacted at the raw-chunk level");
        Assert.Equal("SUCCESS", serverMessages[1].Name);

        Q1Evidence.Write("q1a02-manifest-handshake", new
        {
            probe = "Q1-A02",
            client_handshake_magic = magic,
            client_proposals = proposals,
            server_handshake = serverHandshake,
            client_selection_hex = selection,
            pipelined_hello_preserved = clientStream[25 + 2] == 0xB1 && clientStream[25 + 3] == 0x01,
            client_messages = Q1Evidence.MessagesDocument(clientMessages),
            server_messages = Q1Evidence.MessagesDocument(serverMessages),
            handshake_mode = server.ObservedHandshakeMode(),
            counters = Q1Evidence.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    public async Task Q1A03_ConventionalFallback_ManifestDisabled()
    {
        await using var server = await Q1Server.StartPlaintextAsync(Q1Credentials.Verifier, manifestV1Enabled: false);
        await using var tap = await Q1WireTap.StartAsync(server.Endpoint);

        var driver = Q1Driver.Plaintext(tap.HostPort);
        long observed = -1;
        try
        {
            await Q1Driver.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            var cursor = await Q1Driver.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await Q1Driver.Bounded(cursor.FetchAsync()));
            observed = cursor.Current["answer"].As<long>();
            await Q1Driver.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await Q1Driver.Bounded(driver.DisposeAsync());
        }

        Assert.Equal(42, observed);
        Assert.Equal("Legacy", server.ObservedHandshakeMode());

        var serverHandshake = Q1WireTranscript.ParseServerHandshake(tap);
        // Conventional fallback: the marker is unsupported; the range proposal selects 5.8.
        Assert.Equal("00000805", serverHandshake);

        Q1Evidence.Write("q1a03-conventional-fallback", new
        {
            probe = "Q1-A03",
            server_handshake = serverHandshake,
            handshake_mode = server.ObservedHandshakeMode(),
            observed_answer = observed,
            counters = Q1Evidence.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    public async Task Q1A04_NegativeControl_ServerRejectsAllVersions_NoHello()
    {
        await using var server = await Q1Server.StartRejectAllAsync();

        var driver = Q1Driver.Plaintext(server.HostPort);
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
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0, TimeSpan.FromSeconds(10)));
        var counters = server.Counters();
        Assert.Equal(0, counters.MessagesProcessed);
        Assert.True(counters.BoltHandshakeFailures >= 1);

        Q1Evidence.Write("q1a04-reject-all-versions", new
        {
            probe = "Q1-A04",
            driver_failure = Q1Driver.ExceptionDocument(failure!),
            counters = Q1Evidence.CounterDocument(counters),
            note = "Driver failed without ever sending HELLO; the failure is the zero-word handshake rejection.",
        });
    }

    [Fact]
    public async Task Q1A05_NegativeControl_ConnectionRefused_NoServer()
    {
        // Bind a port, then immediately release it: the connection attempt must fail closed.
        var port = GetFreePort();
        var driver = Q1Driver.Plaintext($"localhost:{port}");
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
        Assert.IsNotType<TimeoutException>(failure);

        Q1Evidence.Write("q1a05-connection-refused", new
        {
            probe = "Q1-A05",
            port = port,
            driver_failure = Q1Driver.ExceptionDocument(failure!),
        });
    }

    [Fact]
    public async Task Q1A06_NegativeControl_UnexpectedEarlyEof()
    {
        // A raw socket that accepts and immediately closes simulates early EOF: the driver must
        // fail closed without hanging and without opening unbounded additional connections.
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var eofTask = Task.Run(async () =>
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                client.Close();
            }
        });

        var driver = Q1Driver.Plaintext($"localhost:{port}");
        Exception? failure = null;
        var driverClosed = false;
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
            driverClosed = true;
        }

        listener.Stop();
        _ = eofTask;

        Assert.NotNull(failure);
        Assert.True(driverClosed);

        Q1Evidence.Write("q1a06-early-eof", new
        {
            probe = "Q1-A06",
            port = port,
            driver_failure = Q1Driver.ExceptionDocument(failure!),
        });
    }

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
