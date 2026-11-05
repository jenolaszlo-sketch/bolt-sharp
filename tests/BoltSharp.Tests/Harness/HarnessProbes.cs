using BoltSharp.Framing;
using BoltSharp.Handshake;

using Xunit;

namespace BoltSharp.Tests.Harness;

/// <summary>
/// B2-C-01 harness verification: exercises the accepted B2-A framing and B2-B handshake
/// implementations through the in-memory <see cref="HarnessStream"/> harness. Cases H01–H07.
/// </summary>
public sealed class HarnessProbes
{
    private static readonly byte[] Selected58 = { 0x00, 0x00, 0x08, 0x05 };
    private static readonly byte[] ZeroResponse = { 0x00, 0x00, 0x00, 0x00 };
    private static readonly byte[] ManifestOffer58 = Convert.FromHexString("000001FF010000080500");

    private static BoltProtocolVersion V(int major, int minor) => new(major, minor);

    private static BoltHandshakeOptions Legacy(params BoltProtocolVersion[] versions) =>
        new(versions, manifestV1Enabled: false);

    private static async Task<byte[]> ReadExactlyAsync(HarnessStream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));
            if (read == 0)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Unexpected end of stream after {offset} of {count} byte(s).");
            }

            offset += read;
        }

        return buffer;
    }

    // ---- H01: deterministic fragmentation ------------------------------------

    [Fact]
    public async Task H01_Legacy_handshake_outcome_is_invariant_under_fragmentation()
    {
        var input = HarnessWire.Preamble(HarnessWire.Version(5, 8));
        var outcomes = new List<(BoltProtocolVersion? Selected, string Response)>();

        foreach (var fragment in new[] { 1, 2, 3, 4, 5, 8, 13, 19, 20, 21, 64 })
        {
            var (client, server) = HarnessDuplex.Create(
                new HarnessStreamOptions { MaxReadFragment = fragment },
                new HarnessStreamOptions { MaxReadFragment = fragment });
            client.Write(input);
            client.CompleteWrites();

            var result = await new BoltHandshakeNegotiator(Legacy(V(5, 8))).NegotiateAsync(server);
            var response = await ReadExactlyAsync(client, 4);
            outcomes.Add((result.SelectedVersion, Convert.ToHexString(response)));
        }

        Assert.All(outcomes, o => Assert.Equal(V(5, 8), o.Selected));
        Assert.All(outcomes, o => Assert.Equal("00000805", o.Response));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(2026)]
    public async Task H01_Seeded_random_fragmentation_is_invariant(int seed)
    {
        var input = HarnessWire.Concat(
            HarnessWire.Preamble(HarnessWire.ManifestMarker),
            HarnessWire.Selection(5, 8, capabilities: 0));
        var (client, server) = HarnessDuplex.Create(
            new HarnessStreamOptions { MinReadFragment = 1, MaxReadFragment = 4, ReadRandom = new Random(seed) },
            new HarnessStreamOptions { MinReadFragment = 1, MaxReadFragment = 4, ReadRandom = new Random(seed) });
        client.Write(input);
        client.CompleteWrites();

        var result = await new BoltHandshakeNegotiator(BoltHandshakeOptions.Create(new[] { V(5, 8) }))
            .NegotiateAsync(server);
        var offer = await ReadExactlyAsync(client, ManifestOffer58.Length);

        Assert.True(result.Accepted);
        Assert.Equal(V(5, 8), result.SelectedVersion);
        Assert.Equal(ManifestOffer58, offer);
    }

    // ---- H02: boundary conditions --------------------------------------------

    [Fact]
    public async Task H02_Manifest_handshake_succeeds_at_every_split()
    {
        var input = HarnessWire.Concat(
            HarnessWire.Preamble(HarnessWire.ManifestMarker),
            HarnessWire.Selection(5, 8, capabilities: 0));

        for (var fragment = 1; fragment <= input.Length; fragment++)
        {
            var (client, server) = HarnessDuplex.Create(
                serverOptions: new HarnessStreamOptions { MaxReadFragment = fragment });
            client.Write(input);
            client.CompleteWrites();

            var result = await new BoltHandshakeNegotiator(BoltHandshakeOptions.Create(new[] { V(5, 8) }))
                .NegotiateAsync(server);
            var offer = await ReadExactlyAsync(client, ManifestOffer58.Length);

            Assert.True(result.Accepted, $"fragment={fragment} was rejected");
            Assert.Equal(V(5, 8), result.SelectedVersion);
            Assert.Equal(ManifestOffer58, offer);
        }
    }

    [Fact]
    public async Task H02_Framed_message_succeeds_at_header_body_and_terminator_boundaries()
    {
        var payload = HarnessWire.Pattern(7);
        var wire = HarnessWire.Frame(payload);

        foreach (var fragment in new[] { 1, 2, 3, 4, 5, 7, 8, 9, 11, 16 })
        {
            var (client, server) = HarnessDuplex.Create(
                serverOptions: new HarnessStreamOptions { MaxReadFragment = fragment });
            client.Write(wire);
            client.CompleteWrites();

            var reader = new BoltMessageReader(server);
            Assert.Equal(payload, await reader.ReadMessageAsync());
        }
    }

    // ---- H03: pipelining -----------------------------------------------------

    [Fact]
    public async Task H03_Pipelined_messages_preserve_order_and_bytes()
    {
        var payloads = new[]
        {
            new byte[] { 0x11 },
            HarnessWire.Pattern(5),
            HarnessWire.Pattern(300),
            new byte[] { 0xFF, 0x00, 0x7F },
        };
        var wire = HarnessWire.Concat(payloads.Select(p => HarnessWire.Frame(p)).ToArray());

        var (client, server) = HarnessDuplex.Create(
            serverOptions: new HarnessStreamOptions { MaxReadFragment = 3 });
        client.Write(wire);
        client.CompleteWrites();

        var reader = new BoltMessageReader(server);
        foreach (var payload in payloads)
        {
            Assert.Equal(payload, await reader.ReadMessageAsync());
        }

        Assert.Null(await reader.ReadMessageAsync());
        Assert.Equal(wire.Length, server.Consumed);
    }

    [Fact]
    public async Task H03_Handshake_leaves_pipelined_frame_for_the_message_reader()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(HarnessWire.Preamble(HarnessWire.Version(5, 8)));
        var payload = HarnessWire.Pattern(9);
        client.Write(HarnessWire.Frame(payload));
        client.CompleteWrites();

        var result = await new BoltHandshakeNegotiator(Legacy(V(5, 8))).NegotiateAsync(server);
        Assert.True(result.Accepted);
        Assert.Equal(20, server.Consumed);

        var reader = new BoltMessageReader(server);
        Assert.Equal(payload, await reader.ReadMessageAsync());
    }

    // ---- H04: cancellation and disconnect ------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(20)]
    public async Task H04_Cancellation_is_deterministic_and_publishes_no_result(int at)
    {
        using var cts = new CancellationTokenSource();
        var (client, server) = HarnessDuplex.Create(
            serverOptions: new HarnessStreamOptions { CancelReadAtByte = at, Cancellation = cts });
        client.Write(HarnessWire.Concat(
            HarnessWire.Preamble(HarnessWire.ManifestMarker),
            HarnessWire.Selection(5, 8, capabilities: 0)));
        client.CompleteWrites();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new BoltHandshakeNegotiator(BoltHandshakeOptions.Create(new[] { V(5, 8) }))
                .NegotiateAsync(server, cts.Token).AsTask());

        Assert.Equal(at, server.Consumed);
        Assert.False(server.Disposed);
    }

    [Fact]
    public async Task H04_Clean_eof_between_messages_is_null()
    {
        var (client, server) = HarnessDuplex.Create();
        client.CompleteWrites();

        var reader = new BoltMessageReader(server);
        Assert.Null(await reader.ReadMessageAsync());
    }

    [Fact]
    public async Task H04_Truncated_header_and_body_are_typed()
    {
        var (client1, server1) = HarnessDuplex.Create();
        client1.Write(new byte[] { 0x00 });
        client1.CompleteWrites();
        await Assert.ThrowsAsync<BoltFramingTruncatedException>(
            () => new BoltMessageReader(server1).ReadMessageAsync().AsTask());

        var (client2, server2) = HarnessDuplex.Create();
        client2.Write(new byte[] { 0x00, 0x05, 0xAA, 0xBB });
        client2.CompleteWrites();
        await Assert.ThrowsAsync<BoltFramingTruncatedException>(
            () => new BoltMessageReader(server2).ReadMessageAsync().AsTask());
    }

    [Fact]
    public async Task H04_Partial_response_write_surfaces_deterministically()
    {
        var (client, server) = HarnessDuplex.Create(
            serverOptions: new HarnessStreamOptions { FailWriteAfterBytes = 2 });
        client.Write(HarnessWire.Preamble(HarnessWire.Version(5, 8)));
        client.CompleteWrites();

        await Assert.ThrowsAsync<IOException>(() =>
            new BoltHandshakeNegotiator(Legacy(V(5, 8))).NegotiateAsync(server).AsTask());

        Assert.Equal(2, server.Written);
        Assert.Equal(2, client.AvailableToRead); // the peer only ever saw the partial prefix
        Assert.False(server.Disposed);
    }

    [Fact]
    public async Task H04_Production_does_not_dispose_the_caller_stream()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(HarnessWire.Preamble(HarnessWire.Version(5, 8)));
        client.CompleteWrites();

        await new BoltHandshakeNegotiator(Legacy(V(5, 8))).NegotiateAsync(server);
        Assert.False(server.Disposed);
    }

    // ---- H05: negative protocol behavior -------------------------------------

    [Fact]
    public async Task H05_Invalid_magic_and_reserved_proposal_are_format_errors()
    {
        var (client1, server1) = HarnessDuplex.Create();
        client1.Write(new byte[] { 0x60, 0x60, 0xB0, 0x16 });
        client1.CompleteWrites();
        await Assert.ThrowsAsync<BoltHandshakeFormatException>(() =>
            new BoltHandshakeNegotiator(Legacy(V(5, 8))).NegotiateAsync(server1).AsTask());

        var (client2, server2) = HarnessDuplex.Create();
        client2.Write(HarnessWire.Preamble(0x0100_0805u)); // reserved first byte set
        client2.CompleteWrites();
        await Assert.ThrowsAsync<BoltHandshakeFormatException>(() =>
            new BoltHandshakeNegotiator(Legacy(V(5, 8))).NegotiateAsync(server2).AsTask());
    }

    [Fact]
    public async Task H05_Unsupported_version_writes_zero_and_requests_close()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(HarnessWire.Preamble(HarnessWire.Version(5, 7)));
        client.CompleteWrites();

        var result = await new BoltHandshakeNegotiator(Legacy(V(5, 8))).NegotiateAsync(server);
        var response = await ReadExactlyAsync(client, 4);

        Assert.False(result.Accepted);
        Assert.True(result.ShouldClose);
        Assert.Null(result.SelectedVersion);
        Assert.Equal(ZeroResponse, response);
    }

    [Fact]
    public async Task H05_Invalid_manifest_selection_is_a_protocol_error()
    {
        var options = BoltHandshakeOptions.Create(new[] { V(5, 8) }, manifestV1Enabled: true, offeredCapabilities: 0x01);

        var (client1, server1) = HarnessDuplex.Create();
        client1.Write(HarnessWire.Concat(
            HarnessWire.Preamble(HarnessWire.ManifestMarker),
            HarnessWire.Selection(5, 7, capabilities: 0x00))); // unoffered version
        client1.CompleteWrites();
        await Assert.ThrowsAsync<BoltHandshakeProtocolException>(() =>
            new BoltHandshakeNegotiator(options).NegotiateAsync(server1).AsTask());

        var (client2, server2) = HarnessDuplex.Create();
        client2.Write(HarnessWire.Concat(
            HarnessWire.Preamble(HarnessWire.ManifestMarker),
            HarnessWire.Selection(5, 8, capabilities: 0x03))); // capability superset
        client2.CompleteWrites();
        await Assert.ThrowsAsync<BoltHandshakeProtocolException>(() =>
            new BoltHandshakeNegotiator(options).NegotiateAsync(server2).AsTask());
    }

    [Fact]
    public async Task H05_Oversized_message_exceeding_the_cap_is_a_format_error()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(HarnessWire.Frame(HarnessWire.Pattern(5)));
        client.CompleteWrites();

        var reader = new BoltMessageReader(server, new BoltFramingLimits(maxMessageSize: 2));
        await Assert.ThrowsAsync<BoltFramingFormatException>(() => reader.ReadMessageAsync().AsTask());
    }

    [Fact]
    public async Task H05_Declared_length_beyond_available_bytes_is_truncation()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(new byte[] { 0x00, 0x0A, 0xAA, 0xBB, 0xCC });
        client.CompleteWrites();

        await Assert.ThrowsAsync<BoltFramingTruncatedException>(
            () => new BoltMessageReader(server).ReadMessageAsync().AsTask());
    }

    // ---- H06: protocol integration -------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task H06_Legacy_handshake_then_two_pipelined_messages(bool async)
    {
        var (client, server) = HarnessDuplex.Create(
            serverOptions: new HarnessStreamOptions { MaxReadFragment = 2 });
        var first = HarnessWire.Pattern(4);
        var second = HarnessWire.Pattern(6);
        client.Write(HarnessWire.Preamble(HarnessWire.Version(5, 8)));
        client.Write(HarnessWire.Concat(HarnessWire.Frame(first), HarnessWire.Frame(second)));
        client.CompleteWrites();

        var negotiator = new BoltHandshakeNegotiator(Legacy(V(5, 8)));
        var result = async ? await negotiator.NegotiateAsync(server) : negotiator.Negotiate(server);
        Assert.Equal(V(5, 8), result.SelectedVersion);
        Assert.Equal(20, server.Consumed);

        var reader = new BoltMessageReader(server);
        Assert.Equal(first, async ? await reader.ReadMessageAsync() : reader.ReadMessage());
        Assert.Equal(second, async ? await reader.ReadMessageAsync() : reader.ReadMessage());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task H06_Manifest_handshake_then_pipelined_message(bool async)
    {
        var (client, server) = HarnessDuplex.Create();
        var payload = HarnessWire.Pattern(5);
        client.Write(HarnessWire.Concat(
            HarnessWire.Preamble(HarnessWire.ManifestMarker),
            HarnessWire.Selection(5, 8, capabilities: 0),
            HarnessWire.Frame(payload)));
        client.CompleteWrites();

        var negotiator = new BoltHandshakeNegotiator(
            BoltHandshakeOptions.Create(new[] { V(5, 8) }, manifestV1Enabled: true, offeredCapabilities: 0));
        var result = async ? await negotiator.NegotiateAsync(server) : negotiator.Negotiate(server);
        Assert.True(result.Accepted);
        Assert.Equal(V(5, 8), result.SelectedVersion);
        Assert.Equal(25, server.Consumed);

        var reader = new BoltMessageReader(server);
        Assert.Equal(payload, async ? await reader.ReadMessageAsync() : reader.ReadMessage());
    }

    // ---- H07: harness discrimination (negative controls) ---------------------

    [Fact]
    public async Task H07_Lost_body_byte_is_detected()
    {
        var payload = HarnessWire.Pattern(5);
        var wire = HarnessWire.Frame(payload);
        var corrupted = new List<byte>(wire);
        corrupted.RemoveAt(3); // drop a declared payload byte

        var (client, server) = HarnessDuplex.Create();
        client.Write(corrupted.ToArray());
        client.CompleteWrites();

        await Assert.ThrowsAsync<BoltFramingTruncatedException>(
            () => new BoltMessageReader(server).ReadMessageAsync().AsTask());
    }

    [Fact]
    public async Task H07_Reordered_bytes_produce_a_different_payload()
    {
        var payload = HarnessWire.Pattern(6);
        var reordered = (byte[])payload.Clone();
        (reordered[0], reordered[1]) = (reordered[1], reordered[0]);
        Assert.NotEqual(payload, reordered);

        var (client, server) = HarnessDuplex.Create();
        client.Write(HarnessWire.Frame(reordered));
        client.CompleteWrites();

        var decoded = await new BoltMessageReader(server).ReadMessageAsync();
        Assert.Equal(reordered, decoded);
        Assert.NotEqual(payload, decoded);
    }

    [Fact]
    public async Task H07_Malformed_length_field_is_detected()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(new byte[] { 0xFF, 0xFF, 0x00, 0x01 }); // declares 65,535 bytes, supplies two
        client.CompleteWrites();

        await Assert.ThrowsAsync<BoltFramingTruncatedException>(
            () => new BoltMessageReader(server).ReadMessageAsync().AsTask());
    }

    [Fact]
    public async Task H07_Broken_eof_mid_header_is_not_a_clean_end()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(new byte[] { 0x00 }); // half a header
        client.CompleteWrites();

        await Assert.ThrowsAsync<BoltFramingTruncatedException>(
            () => new BoltMessageReader(server).ReadMessageAsync().AsTask());
    }

    [Fact]
    public void H07_Bounded_buffer_refuses_unbounded_accumulation()
    {
        var (client, _) = HarnessDuplex.Create(
            new HarnessStreamOptions { MaxBufferedBytes = 4 },
            new HarnessStreamOptions { MaxBufferedBytes = 4 });

        var exception = Assert.Throws<InvalidOperationException>(() => client.Write(new byte[5]));
        Assert.Contains("bounded", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task H07_Fragmentation_policy_is_applied_and_writes_coalesce()
    {
        // One-byte reads: ten bytes are delivered as ten single-byte reads.
        var (client1, server1) = HarnessDuplex.Create(
            serverOptions: new HarnessStreamOptions { MaxReadFragment = 1 });
        client1.Write(HarnessWire.Pattern(10));
        var singles = new List<int>();
        var scratch = new byte[16];
        for (var i = 0; i < 10; i++)
        {
            singles.Add(await server1.ReadAsync(scratch));
        }

        Assert.All(singles, n => Assert.Equal(1, n));

        // Coalescing: three separate writes are available as one read when fragmentation is large.
        var (client2, server2) = HarnessDuplex.Create();
        client2.Write(new byte[] { 0x01 });
        client2.Write(new byte[] { 0x02 });
        client2.Write(new byte[] { 0x03 });
        var buffer = new byte[8];
        var combined = await server2.ReadAsync(buffer);
        Assert.Equal(3, combined);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, buffer[..combined]);
    }
}
