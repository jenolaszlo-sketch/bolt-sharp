using System.Buffers;
using BoltSharp.Framing;
using BoltSharp.Handshake;
using BoltSharp.Tests.Harness;

using Xunit;
using Xunit.Abstractions;

namespace BoltSharp.Qualification.B2CQ1;

/// <summary>
/// Reviewer-owned B2-C-Q1 probes. They exercise the exact producer harness source (linked, not
/// modified) and the accepted production framing/handshake implementations. Categories: A
/// fragmentation, B byte integrity, C lifecycle, D resource limits, E concurrency/determinism,
/// F production round-trip, G driver scaffold inspection.
/// </summary>
public sealed class HarnessReviewProbes(ITestOutputHelper output)
{
    private static readonly byte[] Magic = { 0x60, 0x60, 0xB0, 0x17 };
    private const uint ManifestMarker = 0x0000_01FFu;

    private static uint Version(int major, int minor) => (uint)((minor << 8) | major);
    private static BoltProtocolVersion V(int major, int minor) => new(major, minor);

    private static byte[] Pattern(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)((i * 37) + 13);
        }

        return data;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    private static byte[] Preamble(params uint[] proposals)
    {
        var bytes = new List<byte>(Magic);
        for (var i = 0; i < 4; i++)
        {
            var value = i < proposals.Length ? proposals[i] : 0u;
            bytes.Add((byte)(value >> 24));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }

        return bytes.ToArray();
    }

    private static byte[] VarInt(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
            {
                b |= 0x80;
            }

            bytes.Add(b);
        }
        while (value != 0);

        return bytes.ToArray();
    }

    // Independent chunked framing (does not use the production writer).
    private static byte[] Frame(ReadOnlySpan<byte> payload, int declaredLength = -1)
    {
        var bytes = new List<byte>();
        var length = declaredLength >= 0 ? declaredLength : payload.Length;
        bytes.Add((byte)(length >> 8));
        bytes.Add((byte)length);
        bytes.AddRange(payload.ToArray());
        bytes.Add(0x00);
        bytes.Add(0x00);
        return bytes.ToArray();
    }

    private static async Task<byte[]> ReadExactlyAsync(HarnessStream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));
            if (read == 0)
            {
                throw new Xunit.Sdk.XunitException($"unexpected EOF at {offset}/{count}");
            }

            offset += read;
        }

        return buffer;
    }

    private static async Task<bool> CompletedWithin(Task task, int milliseconds) =>
        await Task.WhenAny(task, Task.Delay(milliseconds)) == task;

    // ---- A. fragmentation ----------------------------------------------------

    [Fact]
    public async Task A1_Single_byte_reads_deliver_every_byte_in_order()
    {
        var payload = Pattern(64);
        var (client, server) = HarnessDuplex.Create(
            serverOptions: new HarnessStreamOptions { MaxReadFragment = 1 });
        client.Write(payload);
        client.CompleteWrites();

        var received = new byte[payload.Length];
        for (var i = 0; i < payload.Length; i++)
        {
            var buffer = new byte[8];
            var read = await server.ReadAsync(buffer);
            Assert.Equal(1, read);
            received[i] = buffer[0];
        }

        Assert.Equal(payload, received);
        Assert.Equal(payload.Length, server.Consumed);
    }

    [Fact]
    public void A2_Seeded_random_fragmentation_is_reproducible()
    {
        var payload = Pattern(80);

        var first = ReadLengths(new Random(12345), payload);
        var second = ReadLengths(new Random(12345), payload);

        Assert.Equal(first, second);
        Assert.Equal(payload.Length, first.Sum());

        static List<int> ReadLengths(Random random, byte[] payload)
        {
            var (client, server) = HarnessDuplex.Create(
                serverOptions: new HarnessStreamOptions { MinReadFragment = 1, MaxReadFragment = 7, ReadRandom = random });
            client.Write(payload);
            client.CompleteWrites();

            var lengths = new List<int>();
            var scratch = new byte[32];
            while (true)
            {
                var read = server.Read(scratch);
                if (read == 0)
                {
                    break;
                }

                lengths.Add(read);
            }

            return lengths;
        }
    }

    [Fact]
    public async Task A3_Handshake_succeeds_at_every_split()
    {
        var input = Concat(Preamble(ManifestMarker), new byte[] { 0x00, 0x00, 0x08, 0x05 }, VarInt(0));
        for (var fragment = 1; fragment <= input.Length; fragment++)
        {
            var (client, server) = HarnessDuplex.Create(
                serverOptions: new HarnessStreamOptions { MaxReadFragment = fragment });
            client.Write(input);
            client.CompleteWrites();

            var result = await new BoltHandshakeNegotiator(BoltHandshakeOptions.Create(new[] { V(5, 8) }))
                .NegotiateAsync(server);
            var offer = await ReadExactlyAsync(client, 10);

            Assert.True(result.Accepted, $"fragment={fragment}");
            Assert.Equal(V(5, 8), result.SelectedVersion);
            Assert.Equal(Convert.FromHexString("000001FF010000080500"), offer);
        }
    }

    [Fact]
    public async Task A4_Multi_byte_manifest_capabilities_survive_single_byte_fragmentation()
    {
        // Offered capabilities 0x80_0000_0000_0000_0001 encodes in two VarInt bytes (81 80 80 80 80 80 80 80 80 01).
        const ulong offered = 0x8000_0000_0000_0001UL;
        const ulong selected = 0x8000_0000_0000_0000UL;
        var selection = new List<byte> { 0x00, 0x00, 0x08, 0x05 };
        selection.AddRange(VarInt(selected));
        var input = Concat(Preamble(ManifestMarker), selection.ToArray());

        var (client, server) = HarnessDuplex.Create(
            serverOptions: new HarnessStreamOptions { MaxReadFragment = 1 });
        client.Write(input);
        client.CompleteWrites();

        var result = await new BoltHandshakeNegotiator(
            BoltHandshakeOptions.Create(new[] { V(5, 8) }, manifestV1Enabled: true, offeredCapabilities: offered))
            .NegotiateAsync(server);

        Assert.True(result.Accepted);
        Assert.Equal(selected, result.NegotiatedCapabilities);
    }

    [Fact]
    public async Task A5_Chunk_header_and_payload_fragmentation()
    {
        var payload = Pattern(37);
        var wire = Frame(payload);
        foreach (var fragment in new[] { 1, 2, 3, 4, 38, 39, 41 })
        {
            var (client, server) = HarnessDuplex.Create(
                serverOptions: new HarnessStreamOptions { MaxReadFragment = fragment });
            client.Write(wire);
            client.CompleteWrites();

            Assert.Equal(payload, await new BoltMessageReader(server).ReadMessageAsync());
        }
    }

    [Fact]
    public async Task A6_Multiple_writes_coalesce_into_ordered_messages()
    {
        var a = Pattern(3);
        var b = Pattern(5);
        var (client, server) = HarnessDuplex.Create();
        client.Write(Frame(a));
        client.Write(Frame(b));
        client.CompleteWrites();

        var reader = new BoltMessageReader(server);
        Assert.Equal(a, await reader.ReadMessageAsync());
        Assert.Equal(b, await reader.ReadMessageAsync());
        Assert.Null(await reader.ReadMessageAsync());
    }

    // ---- B. byte integrity ---------------------------------------------------

    [Fact]
    public async Task B1_Lost_byte_is_detected()
    {
        var wire = Frame(Pattern(6));
        var corrupted = wire.ToList();
        corrupted.RemoveAt(3);
        var (client, server) = HarnessDuplex.Create();
        client.Write(corrupted.ToArray());
        client.CompleteWrites();
        await Assert.ThrowsAsync<BoltFramingTruncatedException>(
            () => new BoltMessageReader(server).ReadMessageAsync().AsTask());
    }

    [Fact]
    public async Task B2_Reordered_bytes_are_detected_by_inequality()
    {
        var payload = Pattern(8);
        var reordered = (byte[])payload.Clone();
        (reordered[2], reordered[5]) = (reordered[5], reordered[2]);
        var (client, server) = HarnessDuplex.Create();
        client.Write(Frame(reordered));
        client.CompleteWrites();
        var decoded = await new BoltMessageReader(server).ReadMessageAsync();
        Assert.Equal(reordered, decoded);
        Assert.NotEqual(payload, decoded);
    }

    [Fact]
    public async Task B3_Duplicated_byte_changes_the_message_or_fails()
    {
        var payload = Pattern(5);
        var wire = Frame(payload);
        var duplicated = new List<byte>(wire);
        duplicated.Insert(3, payload[0]); // duplicate a declared payload byte
        var (client, server) = HarnessDuplex.Create();
        client.Write(duplicated.ToArray());
        client.CompleteWrites();

        // The reader consumes the declared length; the duplicate shifts everything, so the message
        // either differs from the original or the stream ends before the terminator.
        byte[]? decoded = null;
        var faulted = false;
        try
        {
            decoded = await new BoltMessageReader(server).ReadMessageAsync();
        }
        catch (BoltFramingTruncatedException)
        {
            faulted = true;
        }

        Assert.True(faulted || !payload.SequenceEqual(decoded!));
    }

    [Fact]
    public async Task B4_Incorrect_payload_boundary_is_detected()
    {
        // A declared length shorter than the bytes present must not silently yield the intended
        // payload: the extra bytes become a subsequent chunk header, so the outcome differs or
        // truncates.
        var intended = Pattern(6);
        var (client, server) = HarnessDuplex.Create();
        client.Write(Frame(intended, declaredLength: 4)); // 4 declared, 6 supplied, then terminator
        client.CompleteWrites();

        byte[]? decoded = null;
        var faulted = false;
        try
        {
            decoded = await new BoltMessageReader(server).ReadMessageAsync();
        }
        catch (BoltFramingTruncatedException)
        {
            faulted = true;
        }

        Assert.True(faulted || decoded is null || !intended.SequenceEqual(decoded));
    }

    [Fact]
    public async Task B5_Premature_terminator_before_declared_length_is_truncation()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(new byte[] { 0x00, 0x08, 0x01, 0x02 }); // declares 8, supplies 2 then EOF
        client.CompleteWrites();
        await Assert.ThrowsAsync<BoltFramingTruncatedException>(
            () => new BoltMessageReader(server).ReadMessageAsync().AsTask());
    }

    [Fact]
    public async Task B6_Bytes_after_handshake_remain_for_the_reader()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(Preamble(Version(5, 8)));
        var pipelined = Frame(Pattern(7));
        client.Write(pipelined);
        client.CompleteWrites();

        var result = await new BoltHandshakeNegotiator(new(new[] { V(5, 8) }, manifestV1Enabled: false))
            .NegotiateAsync(server);
        Assert.Equal(V(5, 8), result.SelectedVersion);
        Assert.Equal(20, server.Consumed);
        Assert.Equal(pipelined.Length, server.AvailableToRead);
    }

    // ---- C. lifecycle --------------------------------------------------------

    [Fact]
    public async Task C1_Eof_during_handshake_is_truncated()
    {
        for (var length = 0; length < 20; length++)
        {
            var (client, server) = HarnessDuplex.Create();
            client.Write(Preamble(Version(5, 8))[..length]);
            client.CompleteWrites();
            await Assert.ThrowsAsync<BoltHandshakeTruncatedException>(
                () => new BoltHandshakeNegotiator(new(new[] { V(5, 8) }, false)).NegotiateAsync(server).AsTask());
        }
    }

    [Fact]
    public async Task C2_Eof_during_chunk_header_is_truncated()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(new byte[] { 0x00 });
        client.CompleteWrites();
        await Assert.ThrowsAsync<BoltFramingTruncatedException>(
            () => new BoltMessageReader(server).ReadMessageAsync().AsTask());
    }

    [Fact]
    public async Task C3_Eof_during_chunk_body_is_truncated()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(new byte[] { 0x00, 0x05, 0xAA, 0xBB });
        client.CompleteWrites();
        await Assert.ThrowsAsync<BoltFramingTruncatedException>(
            () => new BoltMessageReader(server).ReadMessageAsync().AsTask());
    }

    [Fact]
    public async Task C4_Cancellation_before_the_operation_is_observed()
    {
        var (_, server) = HarnessDuplex.Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => server.ReadAsync(new byte[1], cts.Token).AsTask());
    }

    [Fact]
    public async Task C5_Cancellation_wakes_a_blocked_read()
    {
        var (_, server) = HarnessDuplex.Create();
        using var cts = new CancellationTokenSource();
        var read = server.ReadAsync(new byte[4], cts.Token).AsTask();

        Assert.False(await CompletedWithin(read, 100));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await read);
    }

    [Fact]
    public async Task C6_Dispose_alone_does_not_wake_a_blocked_read_documented()
    {
        // Reviewer limitation probe. The producer harness signals on write/complete, but Dispose
        // does not complete the pending read signal. This is recorded as a MINOR harness
        // limitation; cancellation still releases the read.
        var (_, server) = HarnessDuplex.Create();
        using var cts = new CancellationTokenSource();
        var read = server.ReadAsync(new byte[4], cts.Token).AsTask();

        Assert.False(await CompletedWithin(read, 100));
        server.Dispose();
        var wokeOnDispose = await CompletedWithin(read, 200);
        output.WriteLine($"wokeOnDispose={wokeOnDispose}");
        Assert.False(wokeOnDispose);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await read);
    }

    [Fact]
    public async Task C7_Completion_leaves_buffered_bytes_readable()
    {
        var payload = Pattern(9);
        var (client, server) = HarnessDuplex.Create();
        client.Write(payload);
        client.CompleteWrites();

        Assert.Equal(payload, await ReadExactlyAsync(server, payload.Length));
        Assert.Equal(0, await server.ReadAsync(new byte[4]));
    }

    [Fact]
    public async Task C8_Reader_completion_with_pending_writes_is_bounded()
    {
        // The peer keeps writing while the reader never reads: the bounded buffer must fail fast
        // rather than accumulate without limit.
        var (client, _) = HarnessDuplex.Create(
            new HarnessStreamOptions { MaxBufferedBytes = 16 },
            new HarnessStreamOptions { MaxBufferedBytes = 16 });
        client.Write(new byte[16]);
        var exception = Assert.Throws<InvalidOperationException>(() => client.Write(new byte[1]));
        Assert.Contains("bounded", exception.Message, StringComparison.OrdinalIgnoreCase);
        await Task.CompletedTask;
    }

    // ---- D. resource limits --------------------------------------------------

    [Fact]
    public void D1_Full_buffer_is_rejected_without_partial_acceptance()
    {
        var (client, _) = HarnessDuplex.Create(
            new HarnessStreamOptions { MaxBufferedBytes = 4 },
            new HarnessStreamOptions { MaxBufferedBytes = 4 });
        Assert.Throws<InvalidOperationException>(() => client.Write(new byte[5]));
        Assert.Equal(0, client.BufferedForPeer); // the whole write is refused, none accepted
    }

    [Fact]
    public async Task D2_Oversized_framed_message_is_a_format_error()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(Frame(Pattern(10)));
        client.CompleteWrites();
        await Assert.ThrowsAsync<BoltFramingFormatException>(
            () => new BoltMessageReader(server, new BoltFramingLimits(maxMessageSize: 3)).ReadMessageAsync().AsTask());
    }

    [Fact]
    public async Task D3_Invalid_declared_length_is_truncation()
    {
        var (client, server) = HarnessDuplex.Create();
        client.Write(new byte[] { 0xFF, 0xFF, 0x01, 0x02, 0x03 });
        client.CompleteWrites();
        await Assert.ThrowsAsync<BoltFramingTruncatedException>(
            () => new BoltMessageReader(server).ReadMessageAsync().AsTask());
    }

    [Fact]
    public void D4_Repeated_failures_do_not_accumulate_buffer()
    {
        var (client, _) = HarnessDuplex.Create(
            new HarnessStreamOptions { MaxBufferedBytes = 8 },
            new HarnessStreamOptions { MaxBufferedBytes = 8 });

        for (var i = 0; i < 200; i++)
        {
            Assert.Throws<InvalidOperationException>(() => client.Write(new byte[9]));
        }

        Assert.Equal(0, client.BufferedForPeer);
    }

    // ---- E. concurrency and determinism --------------------------------------

    [Fact]
    public async Task E1_Concurrent_writer_and_reader_complete_without_deadlock()
    {
        var payload = Pattern(512);
        var (client, server) = HarnessDuplex.Create(
            serverOptions: new HarnessStreamOptions { MaxReadFragment = 3 });

        var writer = Task.Run(() =>
        {
            client.Write(payload);
            client.CompleteWrites();
        });
        var reader = Task.Run(async () =>
        {
            var buffer = new byte[payload.Length];
            var offset = 0;
            while (offset < payload.Length)
            {
                var read = await server.ReadAsync(buffer.AsMemory(offset));
                if (read == 0)
                {
                    break;
                }

                offset += read;
            }

            return buffer[..offset];
        });

        var all = Task.WhenAll(writer, reader);
        Assert.True(await CompletedWithin(all, 10_000), "concurrent harness operation did not complete (harness timeout)");
        Assert.Equal(payload, await reader);
    }

    [Fact]
    public async Task E2_Repeated_identical_handshakes_are_deterministic()
    {
        var results = new List<(bool Accepted, string Response)>();
        for (var i = 0; i < 20; i++)
        {
            var (client, server) = HarnessDuplex.Create(
                new HarnessStreamOptions { MaxReadFragment = (i % 5) + 1 },
                new HarnessStreamOptions { MaxReadFragment = (i % 3) + 1 });
            client.Write(Preamble(Version(5, 8)));
            client.CompleteWrites();
            var result = await new BoltHandshakeNegotiator(new(new[] { V(5, 8) }, false)).NegotiateAsync(server);
            var response = await ReadExactlyAsync(client, 4);
            results.Add((result.Accepted, Convert.ToHexString(response)));
        }

        Assert.All(results, r => Assert.True(r.Accepted));
        Assert.All(results, r => Assert.Equal("00000805", r.Response));
    }

    [Fact]
    public async Task E3_Blocked_read_completes_when_data_arrives()
    {
        var (client, server) = HarnessDuplex.Create();
        var read = server.ReadAsync(new byte[3]).AsTask();
        Assert.False(await CompletedWithin(read, 50));
        client.Write(new byte[] { 0x0A, 0x0B, 0x0C });
        Assert.True(await CompletedWithin(read, 2_000));
        Assert.Equal(3, await read);
    }

    // ---- F. production round-trip --------------------------------------------

    [Fact]
    public async Task F1_Production_writer_roundtrips_through_the_harness()
    {
        var messages = new[] { Pattern(1), Pattern(20), Pattern(300) };
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BoltMessageWriter(buffer);
        foreach (var message in messages)
        {
            writer.WriteMessage(message);
        }

        var wire = buffer.WrittenSpan.ToArray();
        var (client, server) = HarnessDuplex.Create(
            serverOptions: new HarnessStreamOptions { MaxReadFragment = 2 });
        client.Write(wire);
        client.CompleteWrites();

        var reader = new BoltMessageReader(server);
        foreach (var message in messages)
        {
            Assert.Equal(message, await reader.ReadMessageAsync());
        }

        Assert.Null(await reader.ReadMessageAsync());
    }

    // ---- G. driver scaffold inspection ---------------------------------------

    [Fact]
    public void G1_Driver_package_is_pinned_and_confined_to_the_integration_project()
    {
        var root = FindRepositoryRoot();
        var driverCsprojPath = Path.Combine(
            root, "tests", "BoltSharp.DriverIntegrationTests", "BoltSharp.DriverIntegrationTests.csproj");

        var driverPackages = PackageReferences(driverCsprojPath);
        Assert.Contains(driverPackages, r => r.Include == "Neo4j.Driver" && r.Version == "6.4.0");

        foreach (var csproj in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.csproj", SearchOption.AllDirectories)))
        {
            if (string.Equals(Path.GetFullPath(csproj), Path.GetFullPath(driverCsprojPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assert.DoesNotContain(PackageReferences(csproj), r => r.Include == "Neo4j.Driver");
        }
    }

    private static List<(string? Include, string? Version)> PackageReferences(string csprojPath)
    {
        var document = System.Xml.Linq.XDocument.Load(csprojPath);
        var ns = document.Root!.Name.Namespace;
        return document.Descendants(ns + "PackageReference")
            .Select(e => ((string?)e.Attribute("Include"), (string?)e.Attribute("Version")))
            .ToList();
    }

    [Fact]
    public void G2_Driver_scaffold_has_ten_gated_tests_and_a_loopback_default()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "tests", "BoltSharp.DriverIntegrationTests", "DriverInteropScaffoldTests.cs"));

        for (var d = 1; d <= 10; d++)
        {
            Assert.Contains($"D{d:00}_", source, StringComparison.Ordinal);
        }

        var skips = source.Split("Skip =").Length - 1;
        Assert.True(skips >= 10, $"expected at least 10 gated tests, found {skips}");

        var settings = File.ReadAllText(Path.Combine(
            root, "tests", "BoltSharp.DriverIntegrationTests", "DriverTestSettings.cs"));
        Assert.Contains("bolt://127.0.0.1:7687", settings, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BoltSharp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("repository root not found");
    }
}
