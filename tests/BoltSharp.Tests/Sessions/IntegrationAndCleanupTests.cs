using System.Buffers;
using BoltSharp.Framing;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Tests.Messages;

using Xunit;

namespace BoltSharp.Tests.Sessions;

/// <summary>
/// B3-B4: pipelining, GOODBYE/disconnect cleanup, bounded batching, and integration between the
/// accepted B3-A codec, the B2 framing adapter and the B3-B session â€” including authentic B0-B
/// driver request captures.
/// </summary>
public sealed class IntegrationAndCleanupTests
{
    private static byte[] Hex(string hex) =>
        Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));

    private static byte[] Frame(byte[] message)
    {
        var framed = new byte[message.Length + 4];
        framed[0] = (byte)(message.Length >> 8);
        framed[1] = (byte)message.Length;
        message.CopyTo(framed, 2);
        framed[^2] = 0x00;
        framed[^1] = 0x00;
        return framed;
    }

    private static string DescriptionOf(BoltSessionResult result) =>
        Assert.IsType<PackStreamString>(BoltMessageFieldsTestAccess.GetValue(
            Assert.IsType<FailureResponse>(Assert.Single(result.Responses)).Metadata, "description"))!.Value;

    [Fact]
    public void Pipelined_HelloLogonRunPull_ProducesOrderedResponses()
    {
        var session = SessionHarness.Session(ScriptedOperationProvider.WithRows(42));

        var envelopes = session.ReceiveBatch(new BoltRequest[]
        {
            SessionHarness.Hello(),
            SessionHarness.Logon(),
            SessionHarness.Run(),
            SessionHarness.Pull(1),
        });

        Assert.Equal(4, envelopes.Count);
        Assert.All(envelopes.Take(3), e => Assert.Single(e.Responses, r => r is SuccessResponse));
        Assert.Equal(BoltConnectionState.Streaming, envelopes[2].State);
        Assert.IsType<RecordResponse>(envelopes[3].Responses[0]);
        Assert.Equal(BoltConnectionState.Ready, envelopes[3].State);
        Assert.All(envelopes, e => Assert.DoesNotContain(e.Responses, r => r is FailureResponse));
    }

    [Fact]
    public void Batch_ExceedingMaxSize_IsRejected()
    {
        var session = SessionHarness.Authenticated(options: new BoltSessionOptions
        {
            Verifier = TestCredentialVerifier.AcceptAll(),
            MaxBatchSize = 2,
        });

        Assert.Throws<ArgumentOutOfRangeException>(() => session.ReceiveBatch(new BoltRequest[]
        {
            SessionHarness.Run(),
            SessionHarness.Run(),
            SessionHarness.Run(),
        }));
    }

    [Fact]
    public void Batch_StopsAtDefunct_NoResponseAfter()
    {
        var session = SessionHarness.Authenticated();

        var envelopes = session.ReceiveBatch(new BoltRequest[]
        {
            SessionHarness.Goodbye(),
            SessionHarness.Run(),
        });

        Assert.Single(envelopes);
        Assert.Empty(envelopes[0].Responses);
        Assert.Equal(BoltConnectionState.Defunct, envelopes[0].State);
    }

    [Fact]
    public void LogonFailure_InsideBatch_StopsProcessing()
    {
        var provider = ScriptedOperationProvider.WithRows(1);
        var session = SessionHarness.Session(provider, new BoltSessionOptions { Verifier = null });

        var envelopes = session.ReceiveBatch(new BoltRequest[]
        {
            SessionHarness.Hello(),
            SessionHarness.Logon(),
            SessionHarness.Run(),
        });

        Assert.Equal(2, envelopes.Count);
        Assert.IsType<FailureResponse>(Assert.Single(envelopes[1].Responses));
        Assert.Equal(BoltConnectionState.Defunct, envelopes[1].State);
        Assert.Equal(0, provider.PrepareCount);
    }

    [Fact]
    public void Goodbye_DuringActiveResult_DisposesCursor()
    {
        var operation = new ScriptedOperation(new[] { "x" }, ScriptedOperationProvider.Row(1));
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());
        Assert.True(session.HasActiveOperation);

        var result = session.Receive(SessionHarness.Goodbye());

        Assert.Empty(result.Responses);
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.False(session.HasActiveOperation);
        Assert.Equal(1, operation.DisposeCount);
    }

    [Fact]
    public void Disconnect_DuringActiveResult_DisposesCursor_AndIsIdempotent()
    {
        var operation = new ScriptedOperation(new[] { "x" }, ScriptedOperationProvider.Row(1));
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        session.Disconnect();
        session.Disconnect();

        Assert.Equal(BoltConnectionState.Defunct, session.State);
        Assert.False(session.HasActiveOperation);
        Assert.Equal(1, operation.DisposeCount);
    }

    [Fact]
    public void Dispose_IsIdempotent_AndReceiveAfterDisposeThrows()
    {
        var session = SessionHarness.Authenticated();

        session.Dispose();
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.Receive(SessionHarness.Run()));
    }

    // ---- Authentic B0-B driver captures through the session ----

    [Fact]
    public void AuthenticDriverRequests_ProduceExpectedResponses()
    {
        var provider = ScriptedOperationProvider.WithRows(42);
        var session = SessionHarness.Authenticated(provider);

        // t03-query-scalar: RUN("RETURN 42 AS answer", {}, {"mode":"r"}), PULL{n:1000}, RESET, GOODBYE.
        var run = session.ReceiveBytes(Hex("B310D01352455455524E20343220415320616E73776572A0A1846D6F64658172"));
        Assert.IsType<SuccessResponse>(Assert.Single(run.Responses));
        Assert.Equal(new[] { "x" }, Assert.IsType<SuccessResponse>(run.Responses[0]).Fields);

        var pull = session.ReceiveBytes(Hex("B13FA1816EC903E8"));
        Assert.Equal(42, Assert.IsType<PackStreamInteger>(
            Assert.IsType<RecordResponse>(pull.Responses[0]).Data.Items[0]).Value);
        Assert.Equal(BoltConnectionState.Ready, pull.State);

        var reset = session.ReceiveBytes(Hex("B00F"));
        Assert.IsType<SuccessResponse>(Assert.Single(reset.Responses));

        var goodbye = session.ReceiveBytes(Hex("B002"));
        Assert.Empty(goodbye.Responses);
        Assert.Equal(BoltConnectionState.Defunct, goodbye.State);
    }

    [Fact]
    public void AuthenticDiscardCapture_DiscardsRemainingRows()
    {
        var operation = new ScriptedOperation(new[] { "x" },
            ScriptedOperationProvider.Row(1), ScriptedOperationProvider.Row(2));
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.ReceiveBytes(Hex("B3108852455455524E206EA0A0")); // RUN "RETURN n"

        var discard = session.ReceiveBytes(Hex("B12FA1816EFF")); // DISCARD {n:-1}

        Assert.DoesNotContain(discard.Responses, r => r is RecordResponse);
        Assert.Single(discard.Responses, r => r is SuccessResponse);
        Assert.Equal(BoltConnectionState.Ready, discard.State);
    }

    [Fact]
    public void DriverLikeSequence_ResetsAfterLogonAndBeforeGoodbye()
    {
        var provider = ScriptedOperationProvider.WithRows(1);
        var session = SessionHarness.Session(provider);
        session.Receive(SessionHarness.Hello());
        session.Receive(SessionHarness.Logon());

        // The qualified driver sends RESET immediately after LOGON and again before GOODBYE.
        var reset1 = session.Receive(SessionHarness.Reset());
        Assert.IsType<SuccessResponse>(Assert.Single(reset1.Responses));
        Assert.Equal(BoltConnectionState.Ready, reset1.State);

        session.Receive(SessionHarness.Run());
        session.Receive(SessionHarness.Pull(1));

        var reset2 = session.Receive(SessionHarness.Reset());
        Assert.IsType<SuccessResponse>(Assert.Single(reset2.Responses));

        var goodbye = session.Receive(SessionHarness.Goodbye());
        Assert.Equal(BoltConnectionState.Defunct, goodbye.State);
    }

    [Fact]
    public void DriverLikeByteSequence_HelloLogonResetResetRunPullResetGoodbye()
    {
        // Spec-shaped HELLO/LOGON bytes (authentic captures are redacted in the B0-B evidence) plus
        // the authentic RUN/PULL/RESET/GOODBYE captures, all through the byte path.
        var hello = Hex("B101A2" +
            "8A757365725F6167656E748E74657374636C69656E742F312E30" +
            "8A626F6C745F6167656E74A18770726F647563748E74657374636C69656E742F312E30");
        var logon = Hex("B16AA386736368656D6585" + "6261736963" + "897072696E636970616C856E656F346A" +
            "8B63726564656E7469616C7386736563726574");

        var session = SessionHarness.Session(ScriptedOperationProvider.WithRows(1),
            new BoltSessionOptions { Verifier = TestCredentialVerifier.AcceptAll() });

        Assert.IsType<SuccessResponse>(Assert.Single(session.ReceiveBytes(hello).Responses));
        Assert.IsType<SuccessResponse>(Assert.Single(session.ReceiveBytes(logon).Responses));

        // The qualified driver sends RESET twice after LOGON before RUN, and once before GOODBYE.
        Assert.IsType<SuccessResponse>(Assert.Single(session.ReceiveBytes(Hex("B00F")).Responses));
        Assert.IsType<SuccessResponse>(Assert.Single(session.ReceiveBytes(Hex("B00F")).Responses));

        Assert.IsType<SuccessResponse>(Assert.Single(
            session.ReceiveBytes(Hex("B3108852455455524E206EA0A0")).Responses)); // RUN "RETURN n"
        var pull = session.ReceiveBytes(Hex("B13FA1816EC903E8"));                 // PULL {n:1000}
        Assert.Equal(BoltConnectionState.Ready, pull.State);
        Assert.IsType<SuccessResponse>(Assert.Single(session.ReceiveBytes(Hex("B00F")).Responses));

        var goodbye = session.ReceiveBytes(Hex("B002"));
        Assert.Empty(goodbye.Responses);
        Assert.Equal(BoltConnectionState.Defunct, goodbye.State);
    }

    [Fact]
    public void FramedBytes_FlowThroughB2IntoB3AAndBackThroughB2()
    {
        var codec = new BoltMessageCodec();
        var runBytes = Hex("B3108852455455524E2031A0A0"); // authentic RUN "RETURN 1"
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithRows(1));

        // B2 framing -> B3-A decode -> B3-B handling.
        var requestStream = new MemoryStream(Frame(runBytes));
        var reader = new BoltMessageReader(requestStream);
        var message = reader.ReadMessage();
        Assert.NotNull(message);
        Assert.Equal(runBytes, message);

        var result = session.ReceiveBytes(message!);
        Assert.Equal(BoltConnectionState.Streaming, result.State);

        // B3-A encode -> B2 framing.
        var responseBuffer = new ArrayBufferWriter<byte>();
        var writer = new BoltMessageWriter(responseBuffer);
        foreach (var response in result.Responses)
        {
            writer.WriteMessage(codec.EncodeResponse(response));
        }

        // Read the framed response back through B2 and decode it through B3-A.
        var responseStream = new MemoryStream(responseBuffer.WrittenSpan.ToArray());
        var responseReader = new BoltMessageReader(responseStream);
        var roundTripped = responseReader.ReadMessage();
        Assert.NotNull(roundTripped);
        var success = Assert.IsType<SuccessResponse>(codec.DecodeResponse(roundTripped!));
        Assert.Equal(new[] { "x" }, success.Fields);
    }

    // ---- Malformed vs unsupported vs unknown ----

    [Fact]
    public void KnownUnsupportedMessage_FromBytes_IsFailure_AndDefunct()
    {
        foreach (var hex in new[] { "B111A0", "B012", "B013", "B366A090A0" })
        {
            var session = SessionHarness.Authenticated();

            var result = session.ReceiveBytes(Hex(hex));

            Assert.Equal(BoltConnectionState.Defunct, result.State);
            Assert.True(result.ShouldClose);
            Assert.Contains("not supported", DescriptionOf(result), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UnknownSignature_FromBytes_IsProtocolViolation()
    {
        var session = SessionHarness.Authenticated();

        var result = session.ReceiveBytes(Hex("B15AA0"));

        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.Contains("current connection state", DescriptionOf(result), StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedBytes_AreNeverInterpretedAsValidRequests()
    {
        var session = SessionHarness.Authenticated();

        // RUN with two fields instead of three.
        var result = session.ReceiveBytes(Hex("B210A0A0"));

        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.IsType<FailureResponse>(Assert.Single(result.Responses));
    }

    [Fact]
    public void Telemetry_FromBytes_ValidatesIdentifierDomain()
    {
        var valid = SessionHarness.Authenticated();
        Assert.IsType<SuccessResponse>(Assert.Single(valid.ReceiveBytes(Hex("B15402")).Responses));

        var invalid = SessionHarness.Authenticated();
        var result = invalid.ReceiveBytes(Hex("B154CA00002329")); // TELEMETRY 9001

        Assert.Equal(BoltConnectionState.Failed, result.State);
        Assert.IsType<FailureResponse>(Assert.Single(result.Responses));
    }
}
