// B5-B-F01-R1-Q1 — diagnostic signature-peek assessment and MessageProcessed accounting (§13).
//
// The peek (BoltTcpConnection.DescribePayloadSignature) must be non-authoritative: it may not
// throw on short payloads, may not create a second parser and may not alter protocol semantics.
// MessageProcessed must report exactly one event per processed message with accurate signature
// attribution, and refused messages must be counted exactly once.
using BoltSharp.B5BF01R1Q1.Probes.Infra;

using BoltSharp.Transport;

using Xunit;

namespace BoltSharp.B5BF01R1Q1.Probes;

public sealed class DiagnosticPeekTests
{
    [Fact]
    public async Task MessageProcessed_AttributesTheSignatureOfValidUnsupportedCommands()
    {
        var provider = new ProbeProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        var events = new List<BoltTransportDiagnosticEvent>();
        server.Diagnostics.Event += events.Add;
        using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);

        client.SendFramed(ProbeMessages.Begin());
        _ = client.ReadMessage();
        Assert.True(await WaitUntilAsync(() => server.Diagnostics.ConnectionsActive == 0));

        var processed = events.Where(e => e.Kind == BoltTransportEventKind.MessageProcessed).ToList();
        Assert.Equal(3, processed.Count); // HELLO, LOGON, BEGIN — one event per message, no doubles

        Assert.Equal("sig=0x01", FirstField(processed[0].Detail!));
        Assert.Equal("sig=0x6A", FirstField(processed[1].Detail!));
        Assert.Equal("sig=0x11", FirstField(processed[2].Detail!));
        Assert.Contains("responses=1", processed[2].Detail);
        Assert.Contains("state=Defunct", processed[2].Detail);

        // Terminal-message reporting: exactly one MessageProcessed for the refused BEGIN.
        Assert.Equal(1, processed.Count(e => e.Detail!.Contains("sig=0x11")));

        ProbeHost.AppendEvidence(
            "10-diagnostic-peek.txt",
            string.Join("\n", processed.Select(e => $"{e.Kind}: {e.Detail}")) + "\n");
    }

    [Fact]
    public async Task ShortAndMalformedPayloads_ProduceSafeDiagnostics_WithoutAlteringSemantics()
    {
        var provider = new ProbeProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        var events = new List<BoltTransportDiagnosticEvent>();
        server.Diagnostics.Event += events.Add;
        using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);

        // One-byte payload: too short for a structure header; the peek must not throw, and the
        // session must still answer with the terminal protocol-violation FAILURE.
        client.SendFramed(new byte[] { 0xB1 });
        var failure = client.ReadMessage();
        Assert.NotNull(failure);
        Assert.Equal(0x7F, failure![1]);
        Assert.Equal(
            Convert.ToHexString(ProbePackStream.ExpectedProtocolViolationFailure()),
            Convert.ToHexString(failure));
        Assert.True(await client.WaitForCloseAsync(TimeSpan.FromSeconds(5)));

        var processed = events.Where(e => e.Kind == BoltTransportEventKind.MessageProcessed).ToList();
        Assert.Equal(3, processed.Count);
        // A one-byte payload has no reliable signature: "unknown" is the accurate representation.
        Assert.Contains("sig=unknown", processed[2].Detail);

        // No diagnostic exception escaped into network processing: the connection completed as a
        // terminal refusal (ConnectionsFailed == 0), not as a transport fault.
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);

        ProbeHost.AppendEvidence(
            "10-diagnostic-peek.txt",
            "one-byte payload: sig=unknown reported, protocol-violation FAILURE unchanged, no diagnostic exception\n");
    }

    [Fact]
    public async Task DiagnosticPeek_DoesNotChangeExecution_ResponseEquivalenceHolds()
    {
        // Response-equivalence control: the refusal bytes observed on the wire are identical for a
        // supported-message context and an unsupported-message context, i.e. the diagnostic path
        // (which ran in both) did not influence the protocol outcome.
        var provider = new ProbeProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);

        client.SendFramed(ProbeMessages.Begin());
        var viaDiagnostics = client.ReadMessage(); // MessageProcessed ran after writing

        var direct = new BoltSharp.Sessions.BoltConnectionSession(
            new ProbeProvider(),
            new BoltSharp.Sessions.BoltSessionOptions { Verifier = new ProbeVerifier() });
        direct.ReceiveBytes(ProbeMessages.Hello());
        direct.ReceiveBytes(ProbeMessages.Logon());
        var directResult = direct.ReceiveBytes(ProbeMessages.Begin());
        var withoutTransport = new BoltSharp.Messages.BoltMessageCodec()
            .EncodeResponse(directResult.Responses.Single());

        Assert.NotNull(viaDiagnostics);
        Assert.Equal(Convert.ToHexString(withoutTransport), Convert.ToHexString(viaDiagnostics!));
    }

    private static string FirstField(string detail) => detail.Split(' ')[0];

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
