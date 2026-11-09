using BoltSharp.Sessions;
using BoltSharp.PackStream;
using BoltSharp.Framing;
using BoltSharp.Handshake;
using BoltSharp.Messages;
using BoltSharp.Transport;
using BoltSharp.Tests.Sessions;

using Xunit;

namespace BoltSharp.Tests.Transport;

/// <summary>B5-A4: chunked framing over real sockets — fragmentation, NOOP, truncation, limits.</summary>
public sealed class TcpFramingTests
{
    private static byte[] ScalarRun => TransportRequestEncoder.Encode(SessionHarness.Run("RETURN 42 AS answer"));

    private static byte[] ScalarPull => TransportRequestEncoder.Encode(new PullRequest(1000, null));

    [Fact]
    public async Task SingleChunkRequest_ProcessedEndToEnd()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TransportTestSupport.ConnectAndAuthorize(server, "RETURN 42 AS answer");

        client.SendRequest(new PullRequest(1000, null));
        var responses = client.ReadResult();
        Assert.Equal(2, responses.Length);
        var record = Assert.IsType<RecordResponse>(responses[0]);
        Assert.Equal(42, Assert.IsType<PackStreamInteger>(record.Data.Items[0]).Value);
    }

    [Fact]
    public async Task ChunkLengthHeaderSplitAcrossWrites_IsFramedCorrectly()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        // The 2-byte chunk length header is split into two writes.
        var framed = TransportTestSupport.Frame(ScalarRun);
        client.SendRaw(new[] { framed[0] });
        client.SendRaw(new[] { framed[1] });
        client.SendRaw(framed[2..]);
        Assert.IsAssignableFrom<SuccessResponse>(client.ReadResponse());

        client.SendRequest(new PullRequest(1000, null));
        var responses = client.ReadResult();
        Assert.Equal(42, Assert.IsType<PackStreamInteger>(Assert.IsType<RecordResponse>(responses[0]).Data.Items[0]).Value);
    }

    [Fact]
    public async Task ChunkPayloadSplitAtEveryBoundary_DecodesCorrectly()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        // RUN split into 1-byte chunks.
        client.SendFramed(ScalarRun.Select(b => new[] { b }).ToArray());
        Assert.IsAssignableFrom<SuccessResponse>(client.ReadResponse());

        client.SendRequest(new PullRequest(1000, null));
        var responses = client.ReadResult();
        Assert.Equal(42, Assert.IsType<PackStreamInteger>(Assert.IsType<RecordResponse>(responses[0]).Data.Items[0]).Value);
    }

    [Fact]
    public async Task SeveralMessagesInOneWrite_PreserveOrder()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        client.Handshake(new BoltProtocolVersion(5, 8));

        // HELLO + LOGON + RUN + PULL pipelined in one network write; responses must arrive in order.
        var hello = TransportTestSupport.Frame(TransportRequestEncoder.Encode(SessionHarness.Hello()));
        var logon = TransportTestSupport.Frame(TransportRequestEncoder.Encode(SessionHarness.Logon()));
        var run = TransportTestSupport.Frame(ScalarRun);
        var pull = TransportTestSupport.Frame(ScalarPull);
        client.SendRaw(hello.Concat(logon).Concat(run).Concat(pull).ToArray());

        Assert.IsAssignableFrom<SuccessResponse>(client.ReadResponse()); // HELLO
        Assert.IsAssignableFrom<SuccessResponse>(client.ReadResponse()); // LOGON
        var runSuccess = Assert.IsAssignableFrom<SuccessResponse>(client.ReadResponse()); // RUN
        Assert.Equal(new[] { "answer" }, runSuccess.Fields);
        var responses = client.ReadResult();
        Assert.Equal(42, Assert.IsType<PackStreamInteger>(Assert.IsType<RecordResponse>(responses[0]).Data.Items[0]).Value);
    }

    [Fact]
    public async Task OneMessageAcrossMultipleChunks_IsReassembled()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        client.SendRequestChunked(SessionHarness.Run("RETURN 42 AS answer"), chunkSize: 3);
        Assert.IsAssignableFrom<SuccessResponse>(client.ReadResponse());
    }

    [Fact]
    public async Task NoopBetweenMessages_ProducesNoSessionRequest()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        client.Handshake(new BoltProtocolVersion(5, 8));

        // HELLO, a lone zero-length NOOP chunk, then LOGON: the NOOP is keep-alive framing,
        // never an application request. The server must answer exactly HELLO and LOGON.
        var hello = TransportTestSupport.Frame(TransportRequestEncoder.Encode(SessionHarness.Hello()));
        var noop = new byte[] { 0x00, 0x00 };
        var logon = TransportTestSupport.Frame(TransportRequestEncoder.Encode(SessionHarness.Logon()));
        client.SendRaw(hello.Concat(noop).Concat(logon).ToArray());

        Assert.IsAssignableFrom<SuccessResponse>(client.ReadResponse());
        Assert.IsAssignableFrom<SuccessResponse>(client.ReadResponse());
        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.MessagesProcessed == 2, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task TruncatedChunkHeader_SafeClose()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        client.SendRaw(new byte[] { 0x00 }); // half a chunk length header
        client.Dispose(); // then EOF: the reader raises BoltFramingTruncatedException
        Assert.Equal("closed", client.WaitForClose(5_000));
        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, server.Diagnostics.ConnectionsFailed);
    }

    [Fact]
    public async Task TruncatedChunkPayload_SafeClose()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        // Declare a 10-byte chunk, deliver 3 bytes, then EOF.
        client.SendRaw(new byte[] { 0x00, 0x0A, 0xB1, 0x10, 0x01 });
        client.Dispose();
        Assert.Equal("closed", client.WaitForClose(5_000));
        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, server.Diagnostics.ConnectionsFailed);
    }

    [Fact]
    public async Task EofDuringRunMessage_NoExecutionOfIncompleteRequest()
    {
        var prepared = new List<ScriptedOperationProvider>();
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () =>
            {
                var provider = new ScriptedOperationProvider((IBoltOperation?)null);
                lock (prepared)
                {
                    prepared.Add(provider);
                }

                return provider;
            },
        });
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        var run = ScalarRun;
        client.SendRaw(run[..^1]); // everything but the last payload byte, then EOF
        client.Dispose();

        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)));
        lock (prepared)
        {
            Assert.All(prepared, provider => Assert.Equal(0, provider.PrepareCount));
        }
    }

    [Fact]
    public async Task OversizedCumulativeMessage_IsRefusedBeforeAllocation()
    {
        // HELLO (≈63 bytes) and LOGON (≈54 bytes) fit within 100; the oversized RUN does not.
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            FramingLimits = new BoltFramingLimits(maxMessageSize: 100),
        });
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        var paddedQuery = "RETURN 42 AS answer" + new string(' ', 90); // payload ≈ 117 bytes
        client.SendFramed(TransportRequestEncoder.Encode(SessionHarness.Run(paddedQuery)));
        Assert.Equal("closed", client.WaitForClose(5_000));
        Assert.Equal(1, server.Diagnostics.ConnectionsFailed);
    }

    [Fact]
    public async Task OutputFraming_MatchesGoldenRecordBytes()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TransportTestSupport.ConnectAndAuthorize(server, "RETURN 42 AS answer");

        client.SendRequest(new PullRequest(1000, null));
        var record = client.ReadFramedMessage();
        Assert.NotNull(record);
        // Authentic B3-A/B0-B RECORD [42] message bytes: B1 71 91 2A.
        Assert.Equal("B171912A", Convert.ToHexString(record));
    }
}
