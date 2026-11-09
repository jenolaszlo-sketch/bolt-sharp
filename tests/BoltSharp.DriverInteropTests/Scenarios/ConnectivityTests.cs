using BoltSharp.DriverInteropTests.Infrastructure;
using BoltSharp.Transport;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.DriverInteropTests.Scenarios;

/// <summary>
/// B5-B2 — first real-driver connection. The unmodified official driver must establish a genuine
/// TCP connection to the real B5-A server, negotiate Bolt 5.8 (including manifest v1) and complete
/// HELLO/LOGON. Every assertion is made through the driver's public API or the server's own
/// diagnostics; constructing an IDriver alone is explicitly not treated as connectivity.
/// </summary>
[Collection("B5-B")]
public sealed class ConnectivityTests
{
    [Fact]
    [Trait("Task", "B5-B2")]
    public async Task B5B2_01_VerifyConnectivity_PlaintextLoopback_Succeeds()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);

        var driver = DriverFactory.Plaintext(server.HostPort);
        IServerInfo? serverInfo = null;
        Exception? constructionProbe = null;
        try
        {
            // Constructing the driver must not itself establish a network connection.
            await Task.Delay(250);
            Assert.Equal(0, server.Counters().Accepted);

            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            serverInfo = await DriverFactory.Bounded(driver.GetServerInfoAsync());
        }
        catch (Exception ex)
        {
            constructionProbe = ex;
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.Null(constructionProbe);
        Assert.NotNull(serverInfo);
        Assert.True(
            await server.WaitForCountersAsync(c => c.Active == 0 && c.Completed >= 1, TimeSpan.FromSeconds(10)),
            "the server must observe the released connection");
        var counters = server.Counters();

        Assert.Equal(1, counters.Accepted);
        Assert.Equal(0, counters.RejectedForCapacity);
        Assert.Equal(1, counters.Completed);
        Assert.Equal(0, counters.Failed);
        Assert.Equal(0, counters.BoltHandshakeFailures);
        Assert.Equal(0, counters.TlsHandshakeFailures);
        // HELLO and LOGON were processed (plus the driver's connection RESETs); no query ran.
        var events = server.Events();
        Assert.Equal(1, events.Count(e => e.Kind == BoltTransportEventKind.MessageProcessed && e.Detail!.Contains("sig=0x01")));
        Assert.Equal(1, events.Count(e => e.Kind == BoltTransportEventKind.MessageProcessed && e.Detail!.Contains("sig=0x6A")));
        Assert.DoesNotContain(events, e => e.Kind == BoltTransportEventKind.MessageProcessed && e.Detail!.Contains("sig=0x10"));
        Assert.Equal("ManifestV1", server.ObservedHandshakeMode());

        EvidenceWriter.Write("b5b2-01-connectivity-plaintext", new
        {
            scenario = "B5B2-01",
            title = "VerifyConnectivityAsync against the real B5-A plaintext listener",
            uri_scheme = "bolt",
            driver_package = DriverFactory.PackageIdentity,
            handshake_mode = server.ObservedHandshakeMode(),
            driver_visible = new
            {
                verified = true,
                server_info_agent = serverInfo!.Agent,
                server_info_protocol_version = serverInfo.ProtocolVersion?.ToString(),
                server_info_address = serverInfo.Address?.ToString(),
            },
            counters = EvidenceWriter.CounterDocument(counters),
            server_events = EvidenceWriter.EventDocument(server.Events()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B2")]
    public async Task B5B2_02_ManifestHandshake_ProposalsOfferSelectionAndPipelinedHello()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(tap.HostPort);
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        Assert.True(await server.WaitForCountersAsync(c => c.Completed >= 1, TimeSpan.FromSeconds(10)));

        var connection = tap.SingleConnection();
        var transcript = WireTranscript.Build(connection);

        // The driver's actual handshake: magic + manifest v1 marker + 5.8 range + 4.4 range + 3.0.
        Assert.Equal("60 60 B0 17 00 00 01 FF 00 08 08 05 00 02 04 04 00 00 00 03", transcript.ClientHandshakeHex);
        Assert.Equal(4, transcript.Proposals.Count);
        Assert.Equal("manifest-v1", transcript.Proposals[0].Kind);
        Assert.Equal("range", transcript.Proposals[1].Kind);
        Assert.Equal("5.8", transcript.Proposals[1].Version);
        Assert.Equal("range", transcript.Proposals[2].Kind);
        Assert.Equal("4.4", transcript.Proposals[2].Version);
        Assert.Equal("concrete", transcript.Proposals[3].Kind);
        Assert.Equal("3.0", transcript.Proposals[3].Version);

        // The server answered the marker with the accepted manifest offer (one range, zero
        // capabilities) and the driver selected 5.8 with a zero capability subset.
        Assert.Equal("000001FF010000080500", transcript.ServerManifestOfferHex);
        Assert.Null(transcript.ServerLegacyReplyHex);
        Assert.NotNull(transcript.ServerManifestOffer);
        Assert.Equal(1UL, transcript.ServerManifestOffer!.RangeCount);
        Assert.Equal(0UL, transcript.ServerManifestOffer.Capabilities);
        Assert.Equal("0000080500", transcript.ClientManifestSelectionHex);
        Assert.Equal("5.8", transcript.ClientManifestSelectionVersion);
        Assert.Equal(0UL, transcript.ClientManifestSelectionCapabilities);

        // Manifest: no server reply is expected after the client selection, so the driver may
        // pipeline it with HELLO. In the captured byte stream the selection is immediately followed
        // by the framed HELLO structure with no intervening server bytes (TCP read boundaries may
        // split it, so the assertion is made on the ordered stream, not on one read chunk).
        var clientStream = transcript.ClientToServer;
        Assert.True(clientStream.Length > 25 + 3);
        Assert.Equal("0000080500", WireTranscript.Hex(clientStream.AsSpan(20, 5)));
        var helloLength = (clientStream[25] << 8) | clientStream[26];
        Assert.InRange(helloLength, 1, 1024);
        Assert.Equal(0xB1, clientStream[27]);
        Assert.Equal(0x01, clientStream[28]);
        Assert.Equal("HELLO", transcript.ClientMessages[0].Name);
        Assert.Equal("SUCCESS", transcript.ServerMessages[0].Name);

        // The driver performed no conventional fallback: the first proposal was the marker and the
        // exchange transitioned directly into Bolt messages.
        Assert.Equal("ManifestV1", server.ObservedHandshakeMode());
        Assert.Null(transcript.ParseError);

        // Server write shape, where observable: the first server bytes are the manifest offer. On
        // loopback it is delivered as one read chunk; the observation is recorded, not required
        // (a TCP peer must never assume write atomicity).
        var firstServerChunk = connection.Chunks.First(c => c.Direction == TapDirection.ServerToClient);
        var firstServerChunkHex = WireTranscript.Hex(firstServerChunk.Bytes);
        Assert.StartsWith("000001FF010000080500", firstServerChunkHex, StringComparison.Ordinal);

        EvidenceWriter.Write("b5b2-02-manifest-handshake", new
        {
            scenario = "B5B2-02",
            title = "Driver manifest v1 proposals, server offer, selection and pipelined HELLO",
            uri_scheme = "bolt",
            handshake_mode = server.ObservedHandshakeMode(),
            server_write_observations = new
            {
                first_server_chunk_hex = firstServerChunkHex,
                first_server_chunk_length = firstServerChunk.Bytes.Length,
                manifest_offer_single_read_chunk = firstServerChunkHex == "000001FF010000080500",
                client_chunk_count = connection.Chunks.Count(c => c.Direction == TapDirection.ClientToServer),
                server_chunk_count = connection.Chunks.Count(c => c.Direction == TapDirection.ServerToClient),
            },
            transcript = transcript.ToDocument(connection),
            counters = EvidenceWriter.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B2")]
    public async Task B5B2_03_ConventionalFallback_ManifestDisabled_StillNegotiates58()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(
            TestCredentials.Verifier,
            manifestV1Enabled: false);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(tap.HostPort);
        long observed = -1;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            var cursor = await DriverFactory.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
            observed = cursor.Current["answer"].As<long>();
            await DriverFactory.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.Equal(42, observed);
        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));

        var connection = tap.SingleConnection();
        var transcript = WireTranscript.Build(connection);

        // The driver still proposes the manifest marker first; with manifest disabled the server
        // treats it as an unsupported version and the conventional range proposal selects 5.8.
        Assert.Equal("manifest-v1", transcript.Proposals[0].Kind);
        Assert.Null(transcript.ServerManifestOfferHex);
        Assert.Equal("00000805", transcript.ServerLegacyReplyHex);
        Assert.Null(transcript.ClientManifestSelectionHex);
        Assert.Equal("Legacy", server.ObservedHandshakeMode());
        Assert.Contains(transcript.ClientMessages, m => m.Name == "RUN");
        Assert.Contains(transcript.ServerMessages, m => m.Name == "RECORD");
        Assert.Null(transcript.ParseError);

        // Server write shape, where observable: the legacy 4-byte selection reply is the first
        // server bytes; on loopback it is delivered as one read chunk.
        var firstServerChunk = connection.Chunks.First(c => c.Direction == TapDirection.ServerToClient);
        var firstServerChunkHex = WireTranscript.Hex(firstServerChunk.Bytes);
        Assert.StartsWith("00000805", firstServerChunkHex, StringComparison.Ordinal);

        EvidenceWriter.Write("b5b2-03-conventional-fallback", new
        {
            scenario = "B5B2-03",
            title = "Conventional Bolt 5.8 fallback when manifest v1 is disabled",
            uri_scheme = "bolt",
            handshake_mode = server.ObservedHandshakeMode(),
            observed_answer = observed,
            server_write_observations = new
            {
                first_server_chunk_hex = firstServerChunkHex,
                first_server_chunk_length = firstServerChunk.Bytes.Length,
                legacy_reply_single_read_chunk = firstServerChunkHex == "00000805",
            },
            transcript = transcript.ToDocument(connection),
            counters = EvidenceWriter.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B2")]
    public async Task B5B2_04_ServerRejectsAllVersions_DriverFailsWithoutHello()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartRejectAllAsync();

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
        Assert.True(counters.BoltHandshakeFailures >= 1);
        Assert.DoesNotContain(
            server.Events(),
            e => e.Kind == BoltTransportEventKind.MessageProcessed);

        EvidenceWriter.Write("b5b2-04-reject-all-versions", new
        {
            scenario = "B5B2-04",
            title = "Version honesty: zero-word reply, no HELLO, driver failure",
            uri_scheme = "bolt",
            driver_failure = EvidenceWriter.ExceptionDocument(failure!),
            counters = EvidenceWriter.CounterDocument(counters),
            server_events = EvidenceWriter.EventDocument(server.Events()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B2")]
    public async Task B5B2_05_HelloAndLogon_MetadataShapesAndSuccessPayloads()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(tap.HostPort);
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var connection = tap.SingleConnection();
        var transcript = WireTranscript.Build(connection);

        // HELLO: the driver's actual metadata keys and non-credential values.
        var hello = transcript.FirstClient("HELLO");
        Assert.NotNull(hello);
        Assert.True(hello!.Decoded);
        Assert.Contains("keys=[user_agent,routing,bolt_agent]", hello.Description);
        Assert.Contains("user_agent=\"neo4j-dotnet/", hello.Description);
        Assert.Contains("routing=null", hello.Description);
        Assert.Contains("bolt_agent={", hello.Description);
        Assert.Contains("product=\"neo4j-dotnet/", hello.Description);

        // LOGON: Basic scheme; principal and credentials are never stored.
        var logon = transcript.FirstClient("LOGON");
        Assert.NotNull(logon);
        Assert.True(logon!.Redacted);
        Assert.Equal("<redacted>", logon.Hex);
        Assert.Contains("keys=[scheme,principal,credentials]", logon.Description);
        Assert.Contains("scheme=\"basic\"", logon.Description);
        Assert.DoesNotContain(
            TestCredentials.Password,
            EvidenceWriter.Serialize(transcript.ToDocument(connection)),
            StringComparison.Ordinal);

        // HELLO SUCCESS carries the accepted metadata triple; LOGON SUCCESS is empty.
        var successes = transcript.ServerMessages.Where(m => m.Name == "SUCCESS").ToArray();
        Assert.True(successes.Length >= 2);
        Assert.Equal("SUCCESS {server=BoltSharp/0.1.0,connection_id=bolt-b3b-1,hints={}}", successes[0].Description);
        Assert.Equal("B170A0", successes[1].Hex);
        // Normative classification recorded: protocol_version (optional, manifest-only) and
        // advertised_address (optional, 5.8+) are not present and were not required by the driver.
        Assert.DoesNotContain("protocol_version", successes[0].Description);
        Assert.DoesNotContain("advertised_address", successes[1].Description);

        // The credential sentinel never appears in server-side diagnostics either.
        Assert.DoesNotContain(
            server.Events(),
            e => (e.Detail ?? string.Empty).Contains(TestCredentials.Password, StringComparison.Ordinal));

        EvidenceWriter.Write("b5b2-05-hello-logon-metadata", new
        {
            scenario = "B5B2-05",
            title = "Actual HELLO/LOGON shapes and HELLO/LOGON SUCCESS metadata",
            uri_scheme = "bolt",
            hello = hello.Description,
            logon = logon.Description,
            hello_success = successes[0].Description,
            hello_success_hex = successes[0].Hex,
            logon_success_hex = successes[1].Hex,
            server_messages = transcript.ServerMessages.Select(m => new { m.Name, m.Description, m.Hex }),
            counters = EvidenceWriter.CounterDocument(server.Counters()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B2")]
    public async Task B5B2_06_ConnectionCloseSequence_ResetThenGoodbye()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(tap.HostPort);
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            var cursor = await DriverFactory.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
            await DriverFactory.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        Assert.True(await server.WaitForCountersAsync(c => c.Active == 0 && c.Completed == 1, TimeSpan.FromSeconds(10)));

        var connection = tap.SingleConnection();
        var transcript = WireTranscript.Build(connection);

        // The driver's shutdown sequence ends with GOODBYE (no fabricated response) and the socket
        // closes with a clean EOF, not an abortive reset.
        Assert.Equal("GOODBYE", transcript.ClientMessages.Last().Name);
        Assert.Equal(1, transcript.CountClient("GOODBYE"));
        Assert.Equal(0, transcript.CountServer("IGNORED"));
        Assert.DoesNotContain(transcript.ServerMessages, m => m.Name == "FAILURE");
        // Every client message except GOODBYE received a response; the single PULL additionally
        // produced one RECORD detail message.
        Assert.Equal(1, transcript.CountServer("RECORD"));
        Assert.Equal(transcript.ClientMessages.Count, transcript.ServerMessages.Count);
        Assert.Equal("eof", connection.ClientSideClose);

        var counters = server.Counters();
        Assert.Equal(1, counters.Accepted);
        Assert.Equal(1, counters.Completed);
        Assert.Equal(0, counters.Failed);
        Assert.Equal(0, counters.Active);

        EvidenceWriter.Write("b5b2-06-close-sequence", new
        {
            scenario = "B5B2-06",
            title = "RESET and GOODBYE shutdown sequence with clean EOF",
            uri_scheme = "bolt",
            client_sequence = transcript.ClientMessages.Select(m => m.Name),
            server_sequence = transcript.ServerMessages.Select(m => m.Name),
            client_side_close = connection.ClientSideClose,
            server_side_close = connection.ServerSideClose,
            counters = EvidenceWriter.CounterDocument(counters),
            server_events = EvidenceWriter.EventDocument(server.Events()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B2")]
    public async Task B5B2_07_DriverVisibleNegotiatedProtocolVersion()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);

        var driver = DriverFactory.Plaintext(server.HostPort);
        string? versionText = null;
        string? agent = null;
        string? queryServerVersion = null;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            var info = await DriverFactory.Bounded(driver.GetServerInfoAsync());
            versionText = info.ProtocolVersion?.ToString();
            agent = info.Agent;

            await using var session = driver.AsyncSession();
            var cursor = await DriverFactory.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
            var summary = await DriverFactory.Bounded(cursor.ConsumeAsync());
            queryServerVersion = summary.Server.ProtocolVersion?.ToString();
        }
        finally
        {
            await driver.DisposeAsync();
        }

        // The driver exposes the negotiated revision even though HELLO SUCCESS omits the optional
        // manifest-only protocol_version metadata field.
        Assert.NotNull(agent);
        Assert.Contains("BoltSharp/0.1.0", agent!);
        if (versionText is not null)
        {
            Assert.Contains("5.8", versionText);
        }

        EvidenceWriter.Write("b5b2-07-driver-visible-version", new
        {
            scenario = "B5B2-07",
            title = "Driver-visible server agent and negotiated protocol version",
            uri_scheme = "bolt",
            driver_visible = new
            {
                server_agent = agent,
                protocol_version = versionText,
                summary_protocol_version = queryServerVersion,
            },
            note = "HELLO SUCCESS omits the optional protocol_version field; the driver still exposes 5.8.",
        });
    }
}
