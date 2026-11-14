using System.Buffers;
using System.Net.Sockets;

using BoltSharp.Handshake;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Tests.Sessions;
using BoltSharp.Transport;
using Xunit;

namespace BoltSharp.Tests.Transport;

/// <summary>
/// B5-B-F01-R1 focused regression tests: structurally valid but unsupported Bolt commands
/// (BEGIN, COMMIT, ROLLBACK, ROUTE) must reach the accepted B3-B session refusal path through the
/// real TCP transport instead of being closed at the B3-A decode boundary, while malformed input
/// keeps its terminal (never recoverable) disposition and supported requests are unaffected.
/// </summary>
public sealed class UnsupportedCommandRefusalTests
{
    /// <summary>
    /// The accepted B3-B encoding for the unsupported-command terminal FAILURE
    /// (<c>BoltConnectionSession.ReceiveBytes</c> → <c>UnsupportedMessageFailure</c>):
    /// <c>gql_status=08N06</c>, <c>neo4j_code=Neo.ClientError.Request.Invalid</c>,
    /// <c>description="Explicit transactions and routing are not supported by this server."</c>,
    /// empty <c>diagnostic_record</c>. Byte-locked here because it is the exact payload the
    /// transport must now deliver for BEGIN/COMMIT/ROLLBACK/ROUTE.
    /// </summary>
    private const string AcceptedUnsupportedCommandFailureHex =
        "B17FA48A67716C5F7374617475738530384E30368A6E656F346A5F636F6465D01F4E656F2E436C69656E744572726F722E526571756573742E496E76616C69648B6465736372697074696F6ED0434578706C69636974207472616E73616374696F6E7320616E6420726F7574696E6720617265206E6F7420737570706F727465642062792074686973207365727665722ED011646961676E6F737469635F7265636F7264A0";

    /// <summary>
    /// The accepted B3-B terminal protocol-violation FAILURE encoding for malformed input
    /// (<c>ProtocolViolationFailure</c>): same status/code, description
    /// <c>"The message cannot be handled in the current connection state."</c>.
    /// </summary>
    private const string AcceptedProtocolViolationFailureHex =
        "B17FA48A67716C5F7374617475738530384E30368A6E656F346A5F636F6465D01F4E656F2E436C69656E744572726F722E526571756573742E496E76616C69648B6465736372697074696F6ED03E546865206D6573736167652063616E6E6F742062652068616E646C656420696E207468652063757272656E7420636F6E6E656374696F6E2073746174652ED011646961676E6F737469635F7265636F7264A0";

    private static readonly byte[] BeginPayload = { 0xB1, 0x11, 0xA0 };

    private static readonly byte[] CommitPayload = { 0xB1, 0x12, 0xA0 };

    private static readonly byte[] RollbackPayload = { 0xB1, 0x13, 0xA0 };

    private static byte[] RoutePayload() => Structure(
        0x66,
        Map(("address", new PackStreamString("127.0.0.1:1"))),
        new PackStreamList(Array.Empty<PackStreamValue?>()),
        Map());

    /// <summary>Malformed: RUN (signature 0x10) with zero fields instead of the required three.</summary>
    private static readonly byte[] MalformedRunPayload = { 0xB0, 0x10 };

    /// <summary>Structurally valid structure carrying an undefined Bolt 5.8 message signature.</summary>
    private static readonly byte[] UnknownSignaturePayload = { 0xB1, 0x7B, 0xA0 };

    [Fact]
    public async Task Begin_AfterCompletedQuery_ReceivesAcceptedTerminalFailure_AndNoOperationOpens()
    {
        var provider = ScriptedOperationProvider.WithRows(42);
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () => provider,
        });
        var events = new List<BoltTransportDiagnosticEvent>();
        server.Diagnostics.Event += events.Add;

        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        client.Handshake(new BoltProtocolVersion(5, 8));
        client.SendRequest(SessionHarness.Hello());
        TransportTestSupport.ExpectSuccess(client.ReadResponse(), "HELLO");
        client.SendRequest(SessionHarness.Logon());
        TransportTestSupport.ExpectSuccess(client.ReadResponse(), "LOGON");

        // Supported path first: RUN + PULL complete normally on this connection.
        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        TransportTestSupport.ExpectSuccess(client.ReadResponse(), "RUN");
        client.SendRequest(SessionHarness.Pull(1000));
        var queryResult = client.ReadResult();
        Assert.IsType<SuccessResponse>(queryResult[^1]);
        Assert.Equal(1, provider.PrepareCount);

        // Now the valid-but-unsupported BEGIN: it must receive the accepted terminal FAILURE.
        client.SendFramed(BeginPayload);
        var failure = Assert.IsType<FailureResponse>(client.ReadResponse());
        AssertFailure(failure, "Explicit transactions and routing are not supported by this server.");

        // Terminal: the connection closes and no further work is processed.
        Assert.Equal("closed", client.WaitForClose(5_000));
        Assert.Equal(1, provider.PrepareCount); // BEGIN must not open a provider operation

        // The transport processed BEGIN through the session (responses=1, terminal state) and the
        // connection completed cleanly rather than being classified as a transport fault.
        Assert.Contains(
            events,
            e => e.Kind == BoltTransportEventKind.MessageProcessed
                && e.Detail!.Contains("sig=0x11")
                && e.Detail!.Contains("state=Defunct"));
        Assert.Equal(5, server.Diagnostics.MessagesProcessed); // HELLO, LOGON, RUN, PULL, BEGIN
        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 0,
            TimeSpan.FromSeconds(5)));
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
    }

    [Theory]
    [InlineData("BEGIN")]
    [InlineData("COMMIT")]
    [InlineData("ROLLBACK")]
    [InlineData("ROUTE")]
    public async Task UnsupportedCommands_ReceiveTheAcceptedTerminalFailure_NoOperationOpens(string command)
    {
        var payload = command switch
        {
            "BEGIN" => BeginPayload,
            "COMMIT" => CommitPayload,
            "ROLLBACK" => RollbackPayload,
            _ => RoutePayload(),
        };
        var provider = ScriptedOperationProvider.WithRows(1);
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () => provider,
        });

        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        client.Handshake(new BoltProtocolVersion(5, 8));
        client.SendRequest(SessionHarness.Hello());
        TransportTestSupport.ExpectSuccess(client.ReadResponse(), "HELLO");
        client.SendRequest(SessionHarness.Logon());
        TransportTestSupport.ExpectSuccess(client.ReadResponse(), "LOGON");

        client.SendFramed(payload);
        var failure = Assert.IsType<FailureResponse>(client.ReadResponse());
        AssertFailure(failure, "Explicit transactions and routing are not supported by this server.");
        Assert.Equal("closed", client.WaitForClose(5_000));
        Assert.Equal(0, provider.PrepareCount);
        Assert.True(TransportTestSupport.WaitUntil(() => server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)));
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed); // terminal FAILURE, not a transport fault
    }

    [Fact]
    public async Task UnsupportedCommand_FailurePayloadIsByteIdenticalToTheAcceptedSessionEncoding()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        // Standalone terminal refusal: no pipelined tail is sent, so there is no unread request
        // input and the terminal close is orderly (FIN). Client-visible delivery of the complete
        // FAILURE is therefore required here — a reset or EOF before the response fails the test.
        client.SendFramed(BeginPayload);
        var message = client.ReadMessage();
        Assert.NotNull(message);
        Assert.Equal(AcceptedUnsupportedCommandFailureHex, Convert.ToHexString(message!));

        // Exactly one terminal response, followed by an orderly end of stream (not a reset).
        Assert.Null(client.ReadFramedMessage());
        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task MalformedPayload_ReceivesTerminalProtocolViolation_NotRecoverable_NoExecution()
    {
        var provider = ScriptedOperationProvider.WithRows(1);
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () => provider,
        });
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        client.SendFramed(MalformedRunPayload);
        var message = client.ReadMessage();
        Assert.NotNull(message);
        Assert.Equal(AcceptedProtocolViolationFailureHex, Convert.ToHexString(message!));
        Assert.Equal("closed", client.WaitForClose(5_000));
        Assert.Equal(0, provider.PrepareCount);
        Assert.True(TransportTestSupport.WaitUntil(() => server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task UnknownSignature_ReceivesTerminalProtocolViolation_NoExecution()
    {
        var provider = ScriptedOperationProvider.WithRows(1);
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () => provider,
        });
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        client.SendFramed(UnknownSignaturePayload);
        var message = client.ReadMessage();
        Assert.NotNull(message);
        Assert.Equal(AcceptedProtocolViolationFailureHex, Convert.ToHexString(message!));
        Assert.Equal("closed", client.WaitForClose(5_000));
        Assert.Equal(0, provider.PrepareCount);
    }

    [Fact]
    public async Task PipelinedUnsupportedCommand_NothingAfterTheTerminalFailureExecutes()
    {
        var provider = ScriptedOperationProvider.WithRows(42);
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () => provider,
        });
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        // Subscribe before the terminal write, filtered to the BEGIN signature so the preceding
        // LOGON MessageProcessed event (which can be reported just after the client reads the
        // LOGON SUCCESS) cannot satisfy the probe. The connection task reports MessageProcessed
        // only after the accepted FAILURE has been written and flushed, and reports completion
        // only after session disposal and counter finalization. Both are authoritative
        // server-side observations independent of client-side TCP delivery (owner finding OWNER-F01).
        var processed = TransportTestSupport.ProbeEvent(
            server,
            BoltTransportEventKind.MessageProcessed,
            e => e.Detail?.Contains("sig=0x11") == true);
        var completed = TransportTestSupport.ProbeEvent(server, BoltTransportEventKind.ConnectionCompleted);

        // One client write: BEGIN, PULL, RESET, RUN, PULL. Only the BEGIN refusal may be answered;
        // everything pipelined after the terminal outcome must never be processed. The unread
        // tail is exactly what can turn the terminal close into an OS abortive close (RST), so
        // server-side terminality and client-visible delivery are asserted separately.
        client.SendRaw(
            TransportTestSupport.Frame(BeginPayload)
                .Concat(TransportTestSupport.Frame(TransportRequestEncoder.Encode(SessionHarness.Pull(1000))))
                .Concat(TransportTestSupport.Frame(TransportRequestEncoder.Encode(SessionHarness.Reset())))
                .Concat(TransportTestSupport.Frame(TransportRequestEncoder.Encode(SessionHarness.Run("RETURN 42 AS answer"))))
                .Concat(TransportTestSupport.Frame(TransportRequestEncoder.Encode(SessionHarness.Pull(1000))))
                .ToArray());

        // Property C (client-visible delivery) may be suppressed by the characterized abortive
        // close. Only a connection reset/abort is accepted as the alternative; EOF, timeout and
        // every other fault are rejected by TryReadTerminalFailure.
        var delivered = TryReadTerminalFailure(
            client, "Explicit transactions and routing are not supported by this server.");

        // Property A/B (generation and server-side write): exactly one response for BEGIN, and
        // the session reached the terminal Defunct state.
        var processedEvent = await TransportTestSupport.AwaitEventAsync(
            processed, server, "terminal MessageProcessed");
        Assert.Contains("sig=0x11", processedEvent.Detail);
        Assert.Contains("responses=1", processedEvent.Detail);
        Assert.Contains("state=Defunct", processedEvent.Detail);

        // Connection retirement completed cleanly (terminal refusal is not a transport fault).
        var completedEvent = await TransportTestSupport.AwaitEventAsync(
            completed, server, "connection completion");
        Assert.Equal("closed", completedEvent.Detail);

        // Only HELLO, LOGON and BEGIN were processed: the PULL/RESET/RUN tail never reached the
        // session, so RESET could not revive it and the second RUN could not execute.
        Assert.Equal(3, server.Diagnostics.MessagesProcessed);
        Assert.Equal(0, provider.PrepareCount);
        Assert.Equal(1, server.Diagnostics.ConnectionsAccepted);
        Assert.Equal(1, server.Diagnostics.ConnectionsCompleted);
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
        Assert.Equal(0, server.Diagnostics.ConnectionsActive);

        if (delivered)
        {
            // The complete FAILURE was observed before the close: nothing else may follow it.
            Assert.Equal("closed", client.WaitForClose(5_000));
            Assert.Null(TryReadAnyFinalMessage(client));
        }
    }

    [Fact]
    public async Task PipelinedMalformedMessage_NothingAfterTheTerminalFailureExecutes()
    {
        var provider = ScriptedOperationProvider.WithRows(42);
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () => provider,
        });
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        // Filtered to the malformed RUN signature (0x10) so the preceding LOGON event cannot
        // satisfy the probe; see the unsupported-command test for the full rationale.
        var processed = TransportTestSupport.ProbeEvent(
            server,
            BoltTransportEventKind.MessageProcessed,
            e => e.Detail?.Contains("sig=0x10") == true);
        var completed = TransportTestSupport.ProbeEvent(server, BoltTransportEventKind.ConnectionCompleted);

        client.SendRaw(
            TransportTestSupport.Frame(MalformedRunPayload)
                .Concat(TransportTestSupport.Frame(TransportRequestEncoder.Encode(SessionHarness.Run("RETURN 42 AS answer"))))
                .Concat(TransportTestSupport.Frame(TransportRequestEncoder.Encode(SessionHarness.Pull(1000))))
                .ToArray());

        var delivered = TryReadTerminalFailure(
            client, "The message cannot be handled in the current connection state.");

        var processedEvent = await TransportTestSupport.AwaitEventAsync(
            processed, server, "terminal MessageProcessed");
        Assert.Contains("sig=0x10", processedEvent.Detail);
        Assert.Contains("responses=1", processedEvent.Detail);
        Assert.Contains("state=Defunct", processedEvent.Detail);

        var completedEvent = await TransportTestSupport.AwaitEventAsync(
            completed, server, "connection completion");
        Assert.Equal("closed", completedEvent.Detail);

        // Only HELLO, LOGON and the malformed payload were processed; the RUN/PULL tail never ran.
        Assert.Equal(3, server.Diagnostics.MessagesProcessed);
        Assert.Equal(0, provider.PrepareCount);
        Assert.Equal(1, server.Diagnostics.ConnectionsAccepted);
        Assert.Equal(1, server.Diagnostics.ConnectionsCompleted);
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
        Assert.Equal(0, server.Diagnostics.ConnectionsActive);

        if (delivered)
        {
            Assert.Equal("closed", client.WaitForClose(5_000));
            Assert.Null(TryReadAnyFinalMessage(client));
        }
    }

    /// <summary>
    /// Reads the single expected terminal FAILURE. Returns <see langword="true"/> when the complete
    /// response was client-visible. The only accepted alternative is the characterized abortive
    /// close retained as owner finding OWNER-F01 / <c>Q-B5B-09</c>: a connection reset or abort
    /// while reading the in-flight response, with unread pipelined input deliberately present.
    /// A clean EOF before any response, a receive timeout, or any other fault is rethrown.
    /// </summary>
    private static bool TryReadTerminalFailure(TestBoltClient client, string expectedDescription)
    {
        try
        {
            var failure = Assert.IsType<FailureResponse>(client.ReadResponse());
            AssertFailure(failure, expectedDescription);
            return true;
        }
        catch (IOException ex) when (IsCharacterizedAbortiveClose(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// True only for the OS abortive-close signature (connection reset/abort). Deliberately
    /// excludes clean EOF (<see cref="EndOfStreamException"/>), timeouts and every other I/O
    /// fault, so an unexpected transport failure can never be accepted as the documented race.
    /// </summary>
    private static bool IsCharacterizedAbortiveClose(IOException exception)
    {
        var socketError = (exception.InnerException as SocketException)?.SocketErrorCode;
        return exception is not EndOfStreamException
            && socketError is SocketError.ConnectionReset or SocketError.ConnectionAborted;
    }

    /// <summary>
    /// After a delivered terminal response the server closes the connection; with pipelined input
    /// still in the socket buffer the close may be abortive (RST), so a final read attempt can
    /// surface only that characterized reset. Anything else is a test failure.
    /// </summary>
    private static byte[]? TryReadAnyFinalMessage(TestBoltClient client)
    {
        try
        {
            return client.ReadFramedMessage();
        }
        catch (IOException ex) when (IsCharacterizedAbortiveClose(ex))
        {
            return null;
        }
    }

    private static void AssertFailure(FailureResponse failure, string expectedDescription)
    {
        Assert.Equal("08N06", TransportTestSupport.GetStringEntry(failure.Metadata, "gql_status"));
        Assert.Equal("Neo.ClientError.Request.Invalid", TransportTestSupport.GetStringEntry(failure.Metadata, "neo4j_code"));
        Assert.Equal(expectedDescription, TransportTestSupport.GetStringEntry(failure.Metadata, "description"));
    }

    private static byte[] Structure(byte signature, params PackStreamValue?[] fields)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        writer.WriteStructure(signature, fields);
        return buffer.WrittenSpan.ToArray();
    }

    private static PackStreamMap Map(params (string Key, PackStreamValue? Value)[] entries) =>
        new(entries.Select(e => new KeyValuePair<string, PackStreamValue?>(e.Key, e.Value)).ToArray());
}
