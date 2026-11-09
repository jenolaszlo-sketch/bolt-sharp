using System.Buffers.Binary;
using System.Net;

using BoltSharp.Handshake;
using BoltSharp.Transport;

namespace B5AQ1Probe;

/// <summary>
/// Critical investigation A: Manifest v1 handshake over TCP (§8). The transport's default
/// BoltHandshakeOptions (Create([5.8])) enables Manifest v1 by default; the producer had no
/// TCP manifest test.
/// </summary>
internal static class ProbeManifest
{
    public const uint Marker = 0x0000_01FFu;
    public const uint V58 = 0x0000_0805u;

    public static void Run()
    {
        Program.Section("A. Manifest v1 handshake over TCP");
        ManifestPreferredSelects58();
        ManifestOfferShape();
        ManifestFragmentedOneByte();
        ManifestWithLegacyFallbacks();
        ManifestUnsupportedSelection();
        ManifestCapabilitySubset();
        ManifestPipelinedHello();
        ManifestUnknownVersionOnly();
        LegacyRangeProposalIncluding58();
        ManifestDisconnectBeforeSelection();
        ManifestInvalidCapabilityRejected();
        ManifestDisabledByOptions();
    }

    private static Program.ProbeServer Start() =>
        Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
        });

    private static void ManifestPreferredSelects58()
    {
        using var server = Start();
        using var client = ReviewClient.Connect(server.EndPoint);
        // Marker as the first proposal; zero placeholders in the remaining slots.
        var reply = client.HandshakeWords(Marker, 0, 0, 0);
        Program.Check(
            "A1 manifest marker answered with offer (not zero-word)",
            reply != "00000000",
            $"reply={reply}");
        Program.Check("A1 conventional reply shape preserved for legacy path", reply.Length == 8, reply);
    }

    private static void ManifestOfferShape()
    {
        using var server = Start();
        using var client = ReviewClient.Connect(server.EndPoint);

        var reply = client.HandshakeWords(Marker, 0, 0, 0); // marker reply consumed here
        Program.Check("A2 server offer begins with manifest marker 00 00 01 FF", reply == "000001FF", reply);

        var selection = new byte[] { 0x00, 0x00, 0x08, 0x05, 0x00 }; // 5.8 + capability subset 0
        var offer = client.CompleteManifest(selection);

        // Offer tail grammar: VarInt(count) | ranges*4 | VarInt(capabilities); the marker was
        // already verified in the reply above.

        var countOk = offer.Length > 0 && offer[0] == 0x01;
        Program.Check("A2 offer advertises exactly one version range (count VarInt == 1)", countOk, $"countByte=0x{(offer.Length > 0 ? offer[0] : 0):X2}");

        var rangeOk = offer.Length >= 5
            && offer[1] == 0x00 && offer[2] == 0x00 && offer[3] == 0x08 && offer[4] == 0x05;
        Program.Check("A2 advertised range word encodes 5.8 (00 00 08 05)", rangeOk, $"range={Program.Hex(offer.AsSpan(1, Math.Min(4, offer.Length - 1)))}");

        var capsOk = offer.Length >= 6 && offer[5] == 0x00;
        Program.Check("A2 capability bitmask present (0 offered)", capsOk, $"capsByte=0x{(offer.Length >= 6 ? offer[5] : 0):X2}");

        // After a valid selection the server transitions into Bolt message processing.
        var events = new List<string>();
        server.Server.Diagnostics.Event += e => events.Add($"{e.Kind} {e.Detail}");
        var helloBytes = Review.Hello();
        Program.TraceLine($"reviewer HELLO payload: {Program.Hex(helloBytes)}");
        client.Send(helloBytes);
        try
        {
            var hello = client.ReadMessageOrThrow();
            var parsed = Parse.Read(hello).As<Parse.St>();
            var serverName = (parsed.Fields[0].As<Parse.M>()).Get("server")?.As<Parse.S>().Value;
            Program.Check(
                "A2 valid selection transitions into normal Bolt messages",
                hello.Length > 4 && hello[0] == 0xB1 && hello[1] == 0x70 && serverName is not null,
                $"HELLO SUCCESS server={serverName}");
            client.Send(Review.Goodbye);
        }
        catch (EndOfStreamException)
        {
            Program.Check(
                "A2 valid selection transitions into normal Bolt messages",
                false,
                $"server closed; events: {string.Join(" | ", events)}");
        }
    }

    private static void ManifestFragmentedOneByte()
    {
        using var server = Start();
        using var client = ReviewClient.Connect(server.EndPoint);

        // Manifest preamble fragmented one byte at a time: magic + marker proposal + zero slots.
        var bytes = new byte[20];
        bytes[0] = 0x60; bytes[1] = 0x60; bytes[2] = 0xB0; bytes[3] = 0x17;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4, 4), Marker);
        foreach (var b in bytes)
        {
            client.SendRaw(new[] { b });
        }

        // The server must respond with the manifest offer despite the fragmentation. The
        // 4-byte marker is consumed first, then the offer tail (count + ranges + capabilities).
        var marker = new byte[4];
        for (var i = 0; i < 4; i++)
        {
            var one = new byte[1];
            if (!client.ReadRawByte(one)) { throw new EndOfStreamException("no marker"); }
            marker[i] = one[0];
        }

        var offer = ConsumeOffer(client);
        Program.Check(
            "A3 one-byte-at-a-time manifest preamble negotiates",
            offer.Length >= 6,
            $"tail={Program.Hex(offer)}");

        // Complete the exchange: selection, then a message.
        client.SendRaw(new byte[] { 0x00, 0x00, 0x08, 0x05, 0x00 });
        client.Send(Review.Hello());
        var hello = client.ReadMessageOrThrow();
        Program.Check(
            "A3 fragmented manifest path processes HELLO after selection",
            hello[0] == 0xB1 && hello[1] == 0x70,
            Program.Hex(hello.AsSpan(0, Math.Min(4, hello.Length))));
        client.Send(Review.Goodbye);
    }

    private static void ManifestWithLegacyFallbacks()
    {
        using var server = Start();
        using var client = ReviewClient.Connect(server.EndPoint);
        // Manifest preferred, then conventional 5.8, then unsupported revisions.
        var reply = client.HandshakeWords(Marker, V58, 0x0000_0905u /*5.9*/, 0x0000_0705u /*5.7*/);
        Program.Check(
            "A4 manifest preferred over conventional fallbacks",
            reply != "00000000",
            $"reply={reply}");

        // Complete the manifest exchange and confirm Bolt processing works.
        var selection = new byte[] { 0x00, 0x00, 0x08, 0x05, 0x00 };
        var offer = client.CompleteManifest(selection);
        client.Send(Review.Hello());
        var hello = client.ReadMessageOrThrow();
        Program.Check(
            "A4 manifest exchange with fallback proposals completes HELLO",
            hello[0] == 0xB1 && hello[1] == 0x70,
            Program.Hex(hello.AsSpan(0, Math.Min(4, hello.Length))));
        client.Send(Review.Goodbye);
    }

    private static void ManifestUnsupportedSelection()
    {
        using var server = Start();
        using var client = ReviewClient.Connect(server.EndPoint);

        // Obtain the offer, then select a version the server did NOT offer (6.0).
        var reply = client.HandshakeWords(Marker, 0, 0, 0);
        var offer = ConsumeOffer(client);
        Program.Check("A5 server offered a manifest (precondition)", reply == "000001FF" && offer.Length >= 6, "tail=" + Program.Hex(offer));

        client.SendRaw(new byte[] { 0x00, 0x00, 0x06, 0x00, 0x00 }); // select 6.0, caps 0
        Program.Check(
            "A5 unoffered manifest selection is refused (connection closed)",
            client.WaitForClose(5_000) == "closed",
            "socket must close after BoltHandshakeProtocolException");

        // The listener remains healthy.
        using var probe = ReviewClient.Connect(server.EndPoint);
        var legacy = probe.HandshakeLegacy(V58);
        Program.Check("A5 listener healthy after refused manifest selection", legacy == "00000805", legacy);
    }

    private static byte[] ConsumeOffer(ReviewClient client)
    {
        // Reads the offer TAIL (count + ranges + capabilities); the 4-byte marker was already
        // consumed by HandshakeWords.
        using var ms = new MemoryStream();
        var buf = new byte[1];
        var count = ReadVarInt(client, ms);
        for (var i = 0; i < count * 4; i++) { if (client.ReadRawByte(buf)) { ms.WriteByte(buf[0]); } }
        ReadVarInt(client, ms); // capabilities
        return ms.ToArray();
    }

    private static long ReadVarInt(ReviewClient client, MemoryStream ms)
    {
        long value = 0;
        var shift = 0;
        while (true)
        {
            var buf = new byte[1];
            if (!client.ReadRawByte(buf)) { throw new EndOfStreamException("EOF in offer VarInt"); }
            ms.WriteByte(buf[0]);
            value |= (long)(buf[0] & 0x7F) << shift;
            if ((buf[0] & 0x80) == 0) { return value; }
            shift += 7;
        }
    }

    private static void ManifestCapabilitySubset()
    {
        using var server = Start();
        using var client = ReviewClient.Connect(server.EndPoint);

        // The server offered capabilities = 0; a selection asking for unknown capability bits
        // must be rejected.
        var reply = client.HandshakeWords(Marker, 0, 0, 0);
        Program.Check("A7 offer present (precondition)", reply == "000001FF", reply);
        ConsumeOffer(client); // drain the offer tail
        client.SendRaw(new byte[] { 0x00, 0x00, 0x08, 0x05 });
        client.SendRaw(new byte[] { 0x01 }); // VarInt capability 1 (not offered)
        Program.Check(
            "A7 selection with unoffered capabilities is refused",
            client.WaitForClose(5_000) == "closed",
            "capability subset enforcement");
    }

    private static void ManifestPipelinedHello()
    {
        using var server = Start();
        using var client = ReviewClient.Connect(server.EndPoint);

        // Selection and the framed HELLO pipelined in ONE write: the transport must not discard
        // or misinterpret the message bytes beyond the manifest boundary.
        var reply = client.HandshakeWords(Marker, 0, 0, 0);
        Program.Check("A8 offer present (precondition)", reply != "00000000", reply);
        ConsumeOffer(client);

        var selection = new byte[] { 0x00, 0x00, 0x08, 0x05, 0x00 };
        var hello = Framing.Frame(Review.Hello());
        client.SendRaw(selection.Concat(hello).ToArray());

        var first = client.ReadMessageOrThrow();
        Program.Check(
            "A8 selection + pipelined HELLO in one write processes HELLO correctly",
            first[0] == 0xB1 && first[1] == 0x70,
            $"first message: {Program.Hex(first.AsSpan(0, Math.Min(4, first.Length)))}");
        client.Send(Review.Goodbye);
    }

    private static void ManifestUnknownVersionOnly()
    {
        using var server = Start();
        using var client = ReviewClient.Connect(server.EndPoint);
        // No manifest marker at all; only unsupported revisions: legacy reject with zero word.
        var reply = client.HandshakeWords(0x0000_0705u, 0x0000_0905u, 0x0000_0600u, 0x0000_0404u);
        Program.Check(
            "A9 unsupported-only proposals answered with zero word",
            reply == "00000000",
            reply);
        Program.Check("A9 rejected handshake closes the connection", client.WaitForClose(5_000) == "closed", "close after reject");
    }

    private static void LegacyRangeProposalIncluding58()
    {
        using var server = Start();
        using var client = ReviewClient.Connect(server.EndPoint);
        // Bolt 4.3 style range proposal covering 5.8 down to 5.6: range=2, minor=8, major=5
        // => (2 << 16) | (8 << 8) | 5 = 0x00020805.
        var reply = client.HandshakeWords(0x0002_0805u, 0, 0, 0);
        Program.Check(
            "A6 version-range proposal containing 5.8 selects 5.8",
            reply == "00000805",
            reply);
    }

    private static void ManifestDisconnectBeforeSelection()
    {
        using var server = Start();
        
        using (var client = ReviewClient.Connect(server.EndPoint))
        {
            client.HandshakeWords(Marker, 0, 0, 0); // server sends the offer
            // Disconnect before sending the final selection.
        }

        Program.Check(
            "A10 disconnect before final manifest selection does not fault the listener",
            Program.WaitUntil(() => server.Server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)),
            $"active={server.Server.Diagnostics.ConnectionsActive}");

        using var probe = ReviewClient.Connect(server.EndPoint);
        var legacy = probe.HandshakeLegacy(V58);
        Program.Check("A10 listener healthy after manifest-stage disconnect", legacy == "00000805", legacy);
    }

    private static void ManifestInvalidCapabilityRejected()
    {
        using var server = Start();
        using var client = ReviewClient.Connect(server.EndPoint);
        var reply = client.HandshakeWords(Marker, 0, 0, 0);
        Program.Check("A11 offer present (precondition)", reply == "000001FF", reply);
        ConsumeOffer(client);
        // Selection of 5.8 with capabilities = 0x8000... (unoffered high bits).
        client.SendRaw(new byte[] { 0x00, 0x00, 0x08, 0x05, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 });
        Program.Check(
            "A11 invalid capability selection rejected and connection closed",
            client.WaitForClose(5_000) == "closed",
            "fail-closed capability validation");
    }

    private static void ManifestDisabledByOptions()
    {
        // Manifest disabled: the marker is then merely an unsupported version and the server
        // answers with the zero word.
        using var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
            HandshakeOptions = BoltHandshakeOptions.Create(new[] { new BoltProtocolVersion(5, 8) }, manifestV1Enabled: false),
        });
        using var client = ReviewClient.Connect(server.EndPoint);
        var reply = client.HandshakeWords(Marker, 0, 0, 0);
        Program.Check(
            "A12 manifest disabled by options: marker treated as unsupported version (zero reply)",
            reply == "00000000",
            reply);
        Program.Check("A12 rejected connection closes", client.WaitForClose(5_000) == "closed", "close");
    }
}
