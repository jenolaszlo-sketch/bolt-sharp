using System.Buffers.Binary;
using System.Reflection;
using BoltSharp.DriverIntegrationTests.HandshakeTrace;
using BoltSharp.Handshake;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.Qualification.B0ATRACEQ1;

/// <summary>
/// B0-A-TRACE-Q1 reviewer-owned probes. Serial execution: several probes use real sockets and the
/// official driver and are timing-sensitive.
/// </summary>
[CollectionDefinition("B0ATRACEQ1", DisableParallelization = true)]
public sealed class QualificationProbeCollection
{
}

[Collection("B0ATRACEQ1")]
public sealed class QualificationProbes
{
    private const string EvidenceDirVariable = "BOLTSHARP_TRACE01_EVIDENCE_DIR";
    private const string DriverPackageDllSha256 = "C0F34E7008F57D70C158E2FA3FDF2E96E3D93506BA1714929B2BD8F4F34B14CF";
    private static readonly byte[] Legacy58Response = { 0x00, 0x00, 0x08, 0x05 };
    private static readonly byte[] Manifest58Offer =
        { 0x00, 0x00, 0x01, 0xFF, 0x01, 0x00, 0x00, 0x08, 0x05, 0x00 };

    // ---- helpers -------------------------------------------------------------

    private static byte[] Frame(byte[] body)
    {
        var frame = new byte[body.Length + 4];
        frame[0] = (byte)(body.Length >> 8);
        frame[1] = (byte)(body.Length & 0xFF);
        body.CopyTo(frame, 2);
        frame[^2] = 0x00;
        frame[^1] = 0x00;
        return frame;
    }

    /// <summary>Structural HELLO-shaped message (synthetic; never attributed to the driver).</summary>
    private static byte[] HelloLikeFrame()
    {
        var body = new byte[155];
        body[0] = 0xB1;
        body[1] = 0x01;
        for (var i = 2; i < body.Length; i++)
        {
            body[i] = (byte)((i * 7) + 1);
        }

        return Frame(body);
    }

    /// <summary>Structural LOGON-shaped message (synthetic; never attributed to the driver).</summary>
    private static byte[] LogonLikeFrame()
    {
        var body = new byte[15];
        body[0] = 0xB1;
        body[1] = 0x6A;
        for (var i = 2; i < body.Length; i++)
        {
            body[i] = (byte)((i * 11) + 3);
        }

        return Frame(body);
    }

    private static byte[] Concat(byte[] first, byte[] second)
    {
        var combined = new byte[first.Length + second.Length];
        first.CopyTo(combined, 0);
        second.CopyTo(combined, first.Length);
        return combined;
    }

    private static LoopbackListenerLimits ProducerLimits(
        int maxConnections = 1,
        bool observe = true,
        int? maxMessageBytes = null,
        int? maxHandshakeCaptureBytes = null) =>
        new()
        {
            MaxConnections = maxConnections,
            ObserveFirstMessage = observe,
            MaxObservedMessageBytes = maxMessageBytes ?? (64 * 1024),
            MaxHandshakeCaptureBytes = maxHandshakeCaptureBytes ?? 4_096,
            ConnectionLifetime = TimeSpan.FromSeconds(5),
            HandshakeTimeout = TimeSpan.FromSeconds(5),
            FirstMessageObservationTimeout = TimeSpan.FromSeconds(1),
            PostMessageObservationWindow = TimeSpan.FromMilliseconds(150),
        };

    private static async Task<Exception?> RunOfficialDriverAsync(Uri endpoint, TimeSpan timeout)
    {
        IDriver? driver = null;
        try
        {
            driver = GraphDatabase.Driver(
                endpoint,
                AuthTokens.None,
                options => options.WithConnectionTimeout(TimeSpan.FromSeconds(3)));
            var connectivity = driver.VerifyConnectivityAsync();
            await connectivity.WaitAsync(timeout);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
        finally
        {
            if (driver is not null)
            {
                await driver.DisposeAsync();
            }
        }
    }

    private static byte[] ParseHexFile(string path)
    {
        var text = File.ReadAllText(path).Trim();
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => Convert.ToByte(token, 16))
            .ToArray();
    }

    private static uint BigEndian(byte[] buffer, int offset) =>
        ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16) | ((uint)buffer[offset + 2] << 8) |
        buffer[offset + 3];

    private static uint EncodeVersion(int major, int minor) => (uint)((minor << 8) | major);

    private static string GetFileSha256(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BoltSharp.slnx")))
        {
            directory = directory.Parent!;
        }

        return directory!.FullName;
    }

    private static string ProducerEvidenceDir()
    {
        var overrideDir = Environment.GetEnvironmentVariable(EvidenceDirVariable);
        return string.IsNullOrWhiteSpace(overrideDir)
            ? Path.Combine(RepoRoot(), "docs", "reviews", "evidence", "B0-A-TRACE-01")
            : overrideDir;
    }

    /// <summary>
    /// Completes a legacy 5.8 handshake against the producer listener with a synthetic peer, then
    /// sends the given payload chunks (optionally fragmented byte-by-byte, with optional
    /// inter-chunk delay) and returns the resulting trace. The synthetic peer's bytes are never
    /// attributed to the official driver.
    /// </summary>
    private static async Task<HandshakeTrace> CompleteLegacyHandshakeAndSendAsync(
        byte[][] chunks,
        bool bytewise = false,
        int interChunkDelayMs = 0,
        int? maxMessageBytes = null,
        int? maxHandshakeCaptureBytes = null)
    {
        var options = new BoltHandshakeOptions(
            new[] { new BoltProtocolVersion(5, 8) },
            manifestV1Enabled: false);
        await using var listener = LoopbackHandshakeListener.Start(
            options,
            ProducerLimits(
                maxConnections: 1,
                observe: true,
                maxMessageBytes: maxMessageBytes,
                maxHandshakeCaptureBytes: maxHandshakeCaptureBytes));

        await using var client = await SyntheticHandshakeClient.ConnectAsync(listener.Port, TimeSpan.FromSeconds(3));
        await client.SendAsync(
            SyntheticHandshakeClient.Preamble(SyntheticHandshakeClient.Version(5, 8)),
            TimeSpan.FromSeconds(3));
        var response = await client.ReadExactlyAsync(4, TimeSpan.FromSeconds(3));
        Assert.Equal(Legacy58Response, response);

        for (var index = 0; index < chunks.Length; index++)
        {
            if (index > 0 && interChunkDelayMs > 0)
            {
                await Task.Delay(interChunkDelayMs);
            }

            if (bytewise)
            {
                foreach (var value in chunks[index])
                {
                    await client.SendAsync(new[] { value }, TimeSpan.FromSeconds(3));
                }
            }
            else
            {
                await client.SendAsync(chunks[index], TimeSpan.FromSeconds(3));
            }
        }

        return await listener.WaitForFirstTraceAsync(TimeSpan.FromSeconds(8));
    }

    // ---- T01: non-fabrication control ----------------------------------------

    [Fact]
    [Trait("Category", "B0-A-TRACE-Q1")]
    public async Task T01_NoTraffic_Produces_No_Handshake_At_All()
    {
        await using var listener = ReviewerBoltListener.Start(
            ReviewerBoltListener.Policy.Legacy58,
            TimeSpan.FromMilliseconds(1200));

        using var silentClient = new System.Net.Sockets.TcpClient();
        await silentClient.ConnectAsync(System.Net.IPAddress.Loopback, listener.Port);
        // Nothing is ever sent.

        var trace = await listener.Trace.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.Empty(trace.Proposals);
        Assert.False(trace.MagicMatches);
        Assert.NotEqual("HANDSHAKE_COMPLETE", trace.Outcome);
        Assert.Empty(trace.Messages);
    }

    // ---- T02: producer capture fidelity (no hardcoded proposals) --------------

    [Fact]
    [Trait("Category", "B0-A-TRACE-Q1")]
    public async Task T02_Producer_Capture_Reflects_Exact_Wire_Bytes()
    {
        var options = new BoltHandshakeOptions(
            new[] { new BoltProtocolVersion(5, 8) },
            manifestV1Enabled: true,
            offeredCapabilities: 0);
        await using var listener = LoopbackHandshakeListener.Start(
            options,
            ProducerLimits(maxConnections: 1, observe: false));

        await using var client = await SyntheticHandshakeClient.ConnectAsync(listener.Port, TimeSpan.FromSeconds(3));
        var distinctive = SyntheticHandshakeClient.Preamble(
            0x0102_0304,
            0x0506_0708,
            0x090A_0B0C,
            0x0D0E_0F10);
        await client.SendAsync(distinctive, TimeSpan.FromSeconds(3));

        var trace = await listener.WaitForFirstTraceAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("FORMAT_ERROR", trace.Outcome);
        Assert.Equal(distinctive, trace.ClientHandshakeBytes);
        Assert.Equal(20, trace.ClientHandshakeBytes.Length);
    }

    // ---- T03: independent decode of the committed producer evidence bytes -----

    [Fact]
    [Trait("Category", "B0-A-TRACE-Q1")]
    public async Task T03_Committed_Evidence_Bytes_Decode_Independently()
    {
        var evidenceDir = ProducerEvidenceDir();
        var manifestClient = ParseHexFile(Path.Combine(evidenceDir, "driver-manifest-client-handshake.hex"));
        var manifestServer = ParseHexFile(Path.Combine(evidenceDir, "driver-manifest-server-response.hex"));
        var legacyClient = ParseHexFile(Path.Combine(evidenceDir, "driver-legacy-client-handshake.hex"));
        var legacyServer = ParseHexFile(Path.Combine(evidenceDir, "driver-legacy-server-response.hex"));

        // Magic.
        Assert.Equal(new byte[] { 0x60, 0x60, 0xB0, 0x17 }, manifestClient.AsSpan(0, 4).ToArray());
        Assert.Equal(new byte[] { 0x60, 0x60, 0xB0, 0x17 }, legacyClient.AsSpan(0, 4).ToArray());

        // Preamble lengths: 20 + 5-byte selection (manifest), 20 (legacy).
        Assert.Equal(25, manifestClient.Length);
        Assert.Equal(20, legacyClient.Length);

        // Four proposals, decoded with the reviewer's own table.
        Assert.Equal(0x0000_01FFu, BigEndian(manifestClient, 4));
        Assert.Equal(0x0008_0805u, BigEndian(manifestClient, 8));
        Assert.Equal(0x0002_0404u, BigEndian(manifestClient, 12));
        Assert.Equal(0x0000_0003u, BigEndian(manifestClient, 16));
        for (var slot = 0; slot < 4; slot++)
        {
            Assert.Equal(BigEndian(manifestClient, 4 + (slot * 4)), BigEndian(legacyClient, 4 + (slot * 4)));
        }

        // Independent 4.0-style encoder: 5.8 == 00 00 08 05.
        Assert.Equal(0x0000_0805u, EncodeVersion(5, 8));

        // Independent 4.3-style range decode of 00 08 08 05: covers 5.0..5.8, includes 5.8
        // (and therefore also 5.5, which no Neo4j server ever negotiates — a range artifact).
        Assert.Equal(8, (int)((0x0008_0805u >> 16) & 0xFF));
        Assert.Equal(8, (int)((0x0008_0805u >> 8) & 0xFF));
        Assert.Equal(5, (int)(0x0008_0805u & 0xFF));
        Assert.True(ReviewerBoltListener.RangeCovers58(0x0008_0805u));

        // Server manifest offer: marker, VarInt(1 range), the 5.8 range, VarInt(0 capabilities).
        Assert.Equal(new byte[] { 0x00, 0x00, 0x01, 0xFF }, manifestServer.AsSpan(0, 4).ToArray());
        Assert.Equal(1, manifestServer[4]);
        Assert.Equal(EncodeVersion(5, 8), BigEndian(manifestServer, 5));
        Assert.Equal(0x00, manifestServer[9]);
        Assert.Equal(10, manifestServer.Length);

        // Manifest selection: 4.0-style 5.8 plus a single-byte zero capability VarInt.
        Assert.Equal(EncodeVersion(5, 8), BigEndian(manifestClient, 20));
        Assert.Equal(0x00, manifestClient[24]);

        // Legacy response: exactly four bytes selecting 5.8.
        Assert.Equal(Legacy58Response, legacyServer);
        Assert.Equal(4, legacyServer.Length);
        await Task.CompletedTask;
    }

    // ---- T04/T05: authentic driver against the reviewer's own listener --------

    [Fact]
    [Trait("Category", "B0-A-TRACE-Q1")]
    public async Task T04_Independent_Manifest_Listener_Driver_Selects_58_And_Pipelines()
    {
        await using var listener = ReviewerBoltListener.Start(ReviewerBoltListener.Policy.Manifest58Only);
        var failure = await RunOfficialDriverAsync(
            new Uri($"bolt://127.0.0.1:{listener.Port}"),
            TimeSpan.FromSeconds(10));

        // Diagnostic: if the trace task has not completed, dump the listener phase first.
        var traceCompleted = await Task.WhenAny(listener.Trace, Task.Delay(TimeSpan.FromSeconds(2))) == listener.Trace;
        if (!traceCompleted)
        {
            // Probe the same port with a raw client to distinguish a broken accept loop from a
            // driver-side connection failure.
            using var probe = new System.Net.Sockets.TcpClient();
            await probe.ConnectAsync(System.Net.IPAddress.Loopback, listener.Port);
            await Task.Delay(300);
            throw new Exception(
                $"T04 diagnostic: driver={failure?.GetType().Name ?? "none"} " +
                $"msg={ReviewerBoltListener.Sanitize(failure?.Message)} phase={listener.DebugPhase} " +
                $"observedBytes={listener.DebugObservedBytes} blockedRead={listener.DebugBlockedRead} " +
                $"postProbePhase={listener.DebugPhase}");
        }

        var trace = await listener.Trace;
        Assert.True(trace.MagicMatches);
        Assert.Equal(new uint[] { 0x0000_01FFu, 0x0008_0805u, 0x0002_0404u, 0x0000_0003u }, trace.Proposals);
        Assert.Equal(Manifest58Offer, trace.ServerResponse);
        Assert.Equal(5, trace.ClientSelection!.Length);
        Assert.Equal(5, trace.SelectionVersionMajor);
        Assert.Equal(8, trace.SelectionVersionMinor);
        Assert.Equal(0x00, trace.SelectionCapabilitiesVarint);
        Assert.True(trace.SelectionVarintContinuationClear);

        // The handshake ends exactly at the selection: 20 preamble + 5 selection bytes.
        Assert.Equal(25, trace.HandshakeByteCount);

        // Post-handshake: HELLO (155-byte body) then pipelined LOGON (15-byte body).
        Assert.Equal("COMPLETED", trace.Outcome);
        Assert.Equal(2, trace.Messages.Count);
        Assert.Equal("HELLO", trace.Messages[0].SignatureName);
        Assert.Equal(155, trace.Messages[0].BodyLength);
        Assert.Equal(0xB1, trace.Messages[0].StructureMarker);
        Assert.Equal(1, trace.Messages[0].StructureFieldCount);
        Assert.Equal("LOGON", trace.Messages[1].SignatureName);
        Assert.Equal(15, trace.Messages[1].BodyLength);

        // Full connectivity must not succeed: the listener never answers HELLO/LOGON.
        Assert.NotNull(failure);
        Assert.IsType<ServiceUnavailableException>(failure!);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-Q1")]
    public async Task T05_Independent_Legacy_Listener_Driver_Continues_After_Selection()
    {
        await using var listener = ReviewerBoltListener.Start(ReviewerBoltListener.Policy.Legacy58);
        var failure = await RunOfficialDriverAsync(
            new Uri($"bolt://127.0.0.1:{listener.Port}"),
            TimeSpan.FromSeconds(10));
        var trace = await listener.Trace.WaitAsync(TimeSpan.FromSeconds(3));
        if (failure is TimeoutException || listener.DebugPhase < 7)
        {
            throw new Exception(
                $"T04 diagnostic: failureType={failure?.GetType().Name ?? "none"} " +
                $"failureMsg={ReviewerBoltListener.Sanitize(failure?.Message)} debugPhase={listener.DebugPhase}");
        }

        Assert.True(trace.MagicMatches);
        Assert.True(trace.Legacy58WasOffered);
        Assert.Equal(Legacy58Response, trace.ServerResponse);

        // The legacy handshake ends exactly after the 20-byte preamble + 4-byte response.
        Assert.Equal(20, trace.HandshakeByteCount);

        // Continuation is demonstrated by real protocol messages after the selection response.
        Assert.Equal("COMPLETED", trace.Outcome);
        Assert.Equal(2, trace.Messages.Count);
        Assert.Equal("HELLO", trace.Messages[0].SignatureName);
        Assert.Equal(155, trace.Messages[0].BodyLength);
        Assert.Equal("LOGON", trace.Messages[1].SignatureName);
        Assert.Equal(15, trace.Messages[1].BodyLength);

        Assert.NotNull(failure);
    }

    // ---- T06: producer listener sanitization (capture boundary) ---------------

    [Fact]
    [Trait("Category", "B0-A-TRACE-Q1")]
    public async Task T06_Producer_Capture_Stops_Exactly_At_The_Handshake_Boundary()
    {
        var options = new BoltHandshakeOptions(
            new[] { new BoltProtocolVersion(5, 8) },
            manifestV1Enabled: true,
            offeredCapabilities: 0);
        await using var listener = LoopbackHandshakeListener.Start(
            options,
            ProducerLimits(maxConnections: 1, observe: true));

        var failureTask = RunOfficialDriverAsync(listener.Uri, TimeSpan.FromSeconds(10));
        var trace = await listener.WaitForFirstTraceAsync(TimeSpan.FromSeconds(8));
        await failureTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Captured client bytes are handshake-only: preamble + manifest selection.
        Assert.Equal(25, trace.ClientHandshakeBytes.Length);
        Assert.Equal(10, trace.ServerResponseBytes.Length);
        Assert.False(trace.HandshakeCaptureTruncated);

        // Post-handshake data is structural metadata only.
        Assert.NotNull(trace.PostHandshake);
        Assert.Equal("MESSAGE_READ", trace.PostHandshake!.Status);
        Assert.Equal(155, trace.PostHandshake.MessageBodyLength);
        Assert.Equal("HELLO", trace.PostHandshake.SignatureName);
        Assert.Equal("LOGON", trace.PostHandshake.AdditionalMessages[0].SignatureName);
        Assert.Equal(15, trace.PostHandshake.AdditionalMessages[0].MessageBodyLength);

        // No property of the post-handshake observation carries raw bytes.
        var byteProperties = typeof(PostHandshakeObservation)
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(byte[]))
            .ToList();
        Assert.Empty(byteProperties);
    }

    // ---- T07–T09: fragmentation, ordering, coalescing -------------------------

    [Fact]
    [Trait("Category", "B0-A-TRACE-Q1")]
    public async Task T07_ByteByByte_Fragmented_Message_Is_Attributed_Correctly()
    {
        var trace = await CompleteLegacyHandshakeAndSendAsync(
            new[] { HelloLikeFrame() },
            bytewise: true);
        Assert.NotNull(trace.PostHandshake);
        Assert.Equal("MESSAGE_READ", trace.PostHandshake!.Status);
        Assert.True(trace.PostHandshake.FramingValid);
        Assert.Equal(155, trace.PostHandshake.MessageBodyLength);
        Assert.Equal("HELLO", trace.PostHandshake.SignatureName);
        Assert.Equal(159, trace.PostHandshake.BytesReadAfterHandshake);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-Q1")]
    public async Task T08_Ordered_Messages_With_Delay_Keep_Their_Order()
    {
        // The 40 ms gap is well inside the producer's 150 ms additional-message observation
        // window, so this probes order attribution across a genuine inter-message gap rather
        // than racing the observation timeout.
        var trace = await CompleteLegacyHandshakeAndSendAsync(
            new[] { HelloLikeFrame(), LogonLikeFrame() },
            interChunkDelayMs: 40);
        Assert.Equal("HELLO", trace.PostHandshake!.SignatureName);
        Assert.Equal("LOGON", trace.PostHandshake.AdditionalMessages[0].SignatureName);
        Assert.Equal(15, trace.PostHandshake.AdditionalMessages[0].MessageBodyLength);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-Q1")]
    public async Task T09_Coalesced_SingleWrite_Pipelining_Is_Attributed_Identically()
    {
        var trace = await CompleteLegacyHandshakeAndSendAsync(
            new[] { Concat(HelloLikeFrame(), LogonLikeFrame()) });
        Assert.Equal("MESSAGE_READ", trace.PostHandshake!.Status);
        Assert.Equal("HELLO", trace.PostHandshake.SignatureName);
        Assert.Equal(155, trace.PostHandshake.MessageBodyLength);
        Assert.Equal("LOGON", trace.PostHandshake.AdditionalMessages[0].SignatureName);
        Assert.Equal(15, trace.PostHandshake.AdditionalMessages[0].MessageBodyLength);
        Assert.Equal(178, trace.PostHandshake.BytesReadAfterHandshake);
    }

    // ---- T10–T13: listener boundaries ------------------------------------------

    [Fact]
    [Trait("Category", "B0-A-TRACE-Q1")]
    public async Task T10_Disposal_With_Pending_Connection_Does_Not_Throw()
    {
        // Demonstrates the bounded-connection contract and guards finding TRACEQ1-F01 (a
        // disposal-race NullReferenceException observed once and preserved in the qualification
        // evidence). Correct behaviour: bounded, exception-free disposal with exactly one trace.
        var options = new BoltHandshakeOptions(
            new[] { new BoltProtocolVersion(5, 8) },
            manifestV1Enabled: false);
        var listener = LoopbackHandshakeListener.Start(
            options,
            ProducerLimits(maxConnections: 1, observe: false));

        using var first = new System.Net.Sockets.TcpClient();
        await first.ConnectAsync(System.Net.IPAddress.Loopback, listener.Port);
        await first.Client.SendAsync(new byte[] { 0x60, 0x60, 0xB0, 0x17 });

        var secondRefused = false;
        using var second = new System.Net.Sockets.TcpClient();
        try
        {
            await second.ConnectAsync(System.Net.IPAddress.Loopback, listener.Port)
                .WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (System.Net.Sockets.SocketException)
        {
            secondRefused = true; // cap enforced by refusing further connections: acceptable
        }
        catch (TimeoutException)
        {
            secondRefused = true; // connect left pending: also bounded behaviour
        }

        // Correct behaviour: bounded, exception-free disposal.
        await listener.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(listener.Traces);
        Assert.NotEqual(true, listener.Traces[0].Accepted);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-Q1")]
    public async Task T11_Handshake_Flood_Is_Rejected_With_Bounded_Reads()
    {
        // A 4100-byte flood must yield a deterministic format error, no server response, and
        // bounded captured input. (The producer's CaptureTruncated flag is defensive-only: the
        // negotiator never reads more than ~34 client bytes in any valid or invalid exchange,
        // so it cannot be observed with cap >= the negotiated byte counts.)
        var options = new BoltHandshakeOptions(
            new[] { new BoltProtocolVersion(5, 8) },
            manifestV1Enabled: false);
        await using var listener = LoopbackHandshakeListener.Start(
            options,
            ProducerLimits(maxConnections: 1, observe: false, maxHandshakeCaptureBytes: 64));

        await using var client = await SyntheticHandshakeClient.ConnectAsync(listener.Port, TimeSpan.FromSeconds(3));
        var payload = new byte[4100];
        payload.AsSpan(0, 4).Fill(0x60);
        await client.SendAsync(payload, TimeSpan.FromSeconds(3));

        var trace = await listener.WaitForFirstTraceAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("FORMAT_ERROR", trace.Outcome);
        Assert.Empty(trace.ServerResponseBytes);
        Assert.True(
            trace.ClientHandshakeBytes.Length <= 64,
            $"captured {trace.ClientHandshakeBytes.Length} bytes, exceeding the 64-byte capture cap");
        Assert.True(trace.ClientHandshakeBytes.Length < payload.Length);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-Q1")]
    public async Task T12_Message_Cap_Overrun_Is_Format_Error()
    {
        var options = new BoltHandshakeOptions(
            new[] { new BoltProtocolVersion(5, 8) },
            manifestV1Enabled: false);
        await using var listener = LoopbackHandshakeListener.Start(
            options,
            ProducerLimits(maxConnections: 1, observe: true, maxMessageBytes: 1024));

        var trace = await CompleteLegacyHandshakeAndSendAsync(
            new[] { Frame(new byte[2000]) },
            maxMessageBytes: 1024);
        // A single 2000-byte chunk against a 1024-byte message cap must be rejected before the
        // payload is read; the observed driver (155-byte HELLO) stays far under any sane cap.
        Assert.Equal("ACCEPTED", trace.Outcome);
        Assert.NotNull(trace.PostHandshake);
        Assert.Equal("FORMAT_ERROR", trace.PostHandshake!.Status);
        Assert.False(trace.PostHandshake.FramingValid);
        Assert.Null(trace.PostHandshake.MessageBodyLength);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-Q1")]
    public async Task T13_Listener_Disposal_Is_Idempotent()
    {
        var options = new BoltHandshakeOptions(
            new[] { new BoltProtocolVersion(5, 8) },
            manifestV1Enabled: false);
        var listener = LoopbackHandshakeListener.Start(
            options,
            ProducerLimits(maxConnections: 1, observe: false));

        await listener.DisposeAsync();
        await listener.DisposeAsync();
    }

    // ---- T14: official driver package provenance -------------------------------

    [Fact]
    [Trait("Category", "B0-A-TRACE-Q1")]
    public void T14_Official_Driver_Package_Provenance()
    {
        var assembly = typeof(GraphDatabase).Assembly;
        var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        Assert.StartsWith("6.4.0", fileVersion, StringComparison.Ordinal);
        Assert.StartsWith("6.4.0", informational, StringComparison.Ordinal);

        var csproj = Path.Combine(
            RepoRoot(), "tests", "BoltSharp.DriverIntegrationTests", "BoltSharp.DriverIntegrationTests.csproj");
        Assert.Contains("<PackageReference Include=\"Neo4j.Driver\" Version=\"6.4.0\" />", File.ReadAllText(csproj));

        var assets = Path.Combine(
            RepoRoot(), "tests", "BoltSharp.DriverIntegrationTests", "obj", "project.assets.json");
        Assert.Contains("\"Neo4j.Driver/6.4.0\"", File.ReadAllText(assets));

        var dll = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages", "neo4j.driver", "6.4.0", "lib", "net8.0", "Neo4j.Driver.dll");
        Assert.True(File.Exists(dll), $"resolved package DLL not found at {dll}");
        Assert.Equal(DriverPackageDllSha256, GetFileSha256(dll));
    }
}
