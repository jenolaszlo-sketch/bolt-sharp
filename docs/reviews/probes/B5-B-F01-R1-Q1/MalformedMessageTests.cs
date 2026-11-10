// B5-B-F01-R1-Q1 — malformed-message boundary (§10) and framing-level failures.
//
// Required probes: wrong arity, unknown signature, wrong field type, truncated structure,
// malformed nested value, invalid request metadata, empty payload, invalid marker, and a
// correctly-framed invalid protocol payload. Framing-level failures (truncated header/body,
// oversized message, premature EOF) are kept separate: the accepted B2 contract closes those
// connections without a response.
using System.Net;

using BoltSharp.B5BF01R1Q1.Probes.Infra;

using BoltSharp.Framing;

using BoltSharp.Transport;

using Xunit;

namespace BoltSharp.B5BF01R1Q1.Probes;

public sealed class MalformedMessageTests
{
    public static TheoryData<string, byte[]> CategoryCProbes() => new()
    {
        { "wrong-arity-B010", Convert.FromHexString("B010") }, // RUN with zero fields
        { "unknown-signature-B17BA0", Convert.FromHexString("B17BA0") },
        { "wrong-field-type-query-list", ProbePackStream.Structure(0x10, ProbePackStream.List(), ProbePackStream.Map(), ProbePackStream.Map()) },
        { "truncated-structure", Convert.FromHexString("B110A2A3616263") }, // declares 2 fields, supplies 1
        { "malformed-nested-params", ProbePackStream.Structure(0x10, ProbePackStream.Str("RETURN 42 AS answer"), ProbePackStream.List(), ProbePackStream.Map()) },
        { "invalid-hello-metadata", ProbePackStream.Structure(0x01, ProbePackStream.Map(("scheme", ProbePackStream.Str("basic")))) },
        { "invalid-marker-non-structure", Convert.FromHexString("0F") }, // integer marker, not a structure
        { "invalid-protocol-tag-0F", ProbePackStream.Structure(0x0F, ProbePackStream.Map()) }, // valid structure, undefined tag
    };

    [Theory]
    [MemberData(nameof(CategoryCProbes))]
    public async Task MalformedPayload_IsTerminalProtocolViolation_NoRecoveryPath(string label, byte[] payload)
    {
        var provider = new ProbeProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);

        client.SendFramed(payload);
        var response = client.ReadMessage();

        // Required: terminal protocol-violation FAILURE transmitted before closure.
        Assert.NotNull(response);
        Assert.Equal(0x7F, response![1]);
        Assert.Equal(
            Convert.ToHexString(ProbePackStream.ExpectedProtocolViolationFailure()),
            Convert.ToHexString(response));
        var metadata = ProbeMessages.FailureMetadata(response);
        Assert.Equal("08N06", metadata["gql_status"]);
        Assert.Equal(ProbePackStream.ProtocolViolationDescription, metadata["description"]);

        // Terminal: the connection closes after the response.
        Assert.True(await client.WaitForCloseAsync(TimeSpan.FromSeconds(5)));

        // No execution, and no later request may execute on the same (now dead) connection.
        Assert.Equal(0, provider.PrepareCount);
        client.SendFramed(ProbeMessages.ScalarQuery());
        Assert.False(await client.HasAdditionalMessageAsync()); // no further response is delivered
        Assert.Equal(0, provider.PrepareCount); // the follow-up RUN never reached the provider

        Assert.True(await WaitUntilAsync(() => server.Diagnostics.ConnectionsActive == 0));
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed); // terminal refusal, not a fault

        ProbeHost.AppendEvidence(
            "05-malformed-probes.txt",
            $"{label}: terminal protocol-violation FAILURE transmitted, connection closed, no recovery path, prepareCount=0\n");
    }

    [Fact]
    public async Task EmptyPayload_ZeroLengthChunk_IsNoopPerAcceptedB2Contract()
    {
        // The accepted B2 contract: a zero-length chunk before any payload byte is a NOOP
        // keep-alive, not an empty message (empty messages are never returned by the framing
        // layer). The connection must stay open and remain fully usable.
        var provider = new ProbeProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);

        client.SendRaw(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // NOOP chunks
        client.SendFramed(ProbeMessages.Reset());
        var reset = client.ReadMessage();
        Assert.NotNull(reset);
        Assert.Equal(0x70, reset![1]); // NOOP was not treated as a message; RESET answered

        // The codec-level EmptyPayload category is still terminal when a zero-length payload
        // reaches the session boundary directly.
        var direct = new BoltSharp.Sessions.BoltConnectionSession(
            new ProbeProvider(), new BoltSharp.Sessions.BoltSessionOptions { Verifier = new ProbeVerifier() });
        direct.ReceiveBytes(ProbeMessages.Hello());
        direct.ReceiveBytes(ProbeMessages.Logon());
        var empty = direct.ReceiveBytes(Array.Empty<byte>());
        Assert.Equal(BoltSharp.Sessions.BoltConnectionState.Defunct, empty.State);
        Assert.Single(empty.Responses);
        Assert.Equal(0x7F, new BoltSharp.Messages.BoltMessageCodec().EncodeResponse(empty.Responses.Single())[1]);
    }

    [Theory]
    [InlineData("truncated-chunk-header")] // length prefix declared, body never sent, then EOF
    [InlineData("premature-eof-mid-message")]
    public async Task FramingLevelFailures_CloseWithoutResponse_PerAcceptedB2Contract(string label)
    {
        var provider = new ProbeProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);

        if (label == "truncated-chunk-header")
        {
            // Declare a 10-byte chunk, send nothing else, close the write side (EOF).
            client.SendRaw(new byte[] { 0x00, 0x0A });
            client.Dispose();
        }
        else
        {
            // One complete chunk of a multi-chunk message, then EOF before the terminator.
            client.SendRaw(new byte[] { 0x00, 0x03, 0xB1, 0x10, 0xA0 });
            client.Dispose();
        }

        // The accepted B2 framing contract closes the connection without any response.
        Assert.True(await client.WaitForCloseAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await WaitUntilAsync(() => server.Diagnostics.ConnectionsActive == 0));
        Assert.Equal(0, provider.PrepareCount);

        ProbeHost.AppendEvidence(
            "05-malformed-probes.txt",
            $"FRAMING {label}: connection closed without response (accepted B2 framing contract)\n");
    }

    [Fact]
    public async Task OversizedMessage_ExceedsConfiguredBound_ClosesWithoutResponse()
    {
        var provider = new ProbeProvider();
        // Small receiver bound so the test does not need to stream 64 MiB.
        await using var server = await ProbeHost.StartServerAsync(
            new ProbeVerifier(), () => provider, new BoltFramingLimits(1024));
        using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);

        // A 2000-byte payload exceeds the configured 1024-byte message bound.
        var oversized = new byte[2000];
        client.SendFramed(oversized);
        Assert.True(await client.WaitForCloseAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, provider.PrepareCount);

        ProbeHost.AppendEvidence(
            "05-malformed-probes.txt",
            "FRAMING oversized-message: connection closed without response (accepted B2 framing contract)\n");
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan? within = null)
    {
        var deadline = DateTime.UtcNow + (within ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return condition();
    }
}
