using System.Buffers;

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

        client.SendFramed(BeginPayload);
        var message = client.ReadMessage();
        Assert.NotNull(message);
        Assert.Equal(AcceptedUnsupportedCommandFailureHex, Convert.ToHexString(message!));
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

        // One client write: BEGIN, PULL, RESET, RUN, PULL. Only the BEGIN refusal may be answered;
        // everything pipelined after the terminal outcome must never be processed.
        client.SendRaw(
            TransportTestSupport.Frame(BeginPayload)
                .Concat(TransportTestSupport.Frame(TransportRequestEncoder.Encode(SessionHarness.Pull(1000))))
                .Concat(TransportTestSupport.Frame(TransportRequestEncoder.Encode(SessionHarness.Reset())))
                .Concat(TransportTestSupport.Frame(TransportRequestEncoder.Encode(SessionHarness.Run("RETURN 42 AS answer"))))
                .Concat(TransportTestSupport.Frame(TransportRequestEncoder.Encode(SessionHarness.Pull(1000))))
                .ToArray());

        var failure = Assert.IsType<FailureResponse>(client.ReadResponse());
        AssertFailure(failure, "Explicit transactions and routing are not supported by this server.");
        Assert.Equal("closed", client.WaitForClose(5_000)); // terminal: no further responses
        Assert.Null(TryReadAnyFinalMessage(client));
        Assert.Equal(0, provider.PrepareCount);

        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.MessagesProcessed == 3, // HELLO, LOGON, BEGIN only
            TimeSpan.FromSeconds(5)));
        Assert.True(TransportTestSupport.WaitUntil(() => server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)));
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

        client.SendRaw(
            TransportTestSupport.Frame(MalformedRunPayload)
                .Concat(TransportTestSupport.Frame(TransportRequestEncoder.Encode(SessionHarness.Run("RETURN 42 AS answer"))))
                .Concat(TransportTestSupport.Frame(TransportRequestEncoder.Encode(SessionHarness.Pull(1000))))
                .ToArray());

        var failure = Assert.IsType<FailureResponse>(client.ReadResponse());
        AssertFailure(failure, "The message cannot be handled in the current connection state.");
        Assert.Equal("closed", client.WaitForClose(5_000)); // terminal: no further responses
        Assert.Null(TryReadAnyFinalMessage(client));
        Assert.Equal(0, provider.PrepareCount);
        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.MessagesProcessed == 3, // HELLO, LOGON, malformed payload only
            TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// After a terminal response the server closes the connection; with pipelined input still in
    /// the socket buffer the close may be abortive (RST), so a final read attempt can surface an
    /// IOException. Either way no further message may be delivered.
    /// </summary>
    private static byte[]? TryReadAnyFinalMessage(TestBoltClient client)
    {
        try
        {
            return client.ReadFramedMessage();
        }
        catch (IOException)
        {
            return null;
        }
        catch (ObjectDisposedException)
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
