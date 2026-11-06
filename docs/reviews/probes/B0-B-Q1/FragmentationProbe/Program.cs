using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Neo4j.Driver;

namespace B0BQ1.FragmentationProbe;

/// <summary>
/// Independent B0-B-Q1 probe. This is deliberately a standalone minimal TCP responder that does
/// not reference BoltSharp or its codec. It is used to test (a) handshake-response fragmentation,
/// (b) HELLO SUCCESS metadata requirements, (c) FAILURE metadata classification, and (d) other
/// legal protocol-boundary fragmentation. The concatenated response bytes are identical in every
/// split case; only the write segmentation differs.
/// </summary>
internal static class Program
{
    private const string ServerName = "Neo4j/5.26.0";
    private const string ConnectionId = "bolt-q1-1";

    private static async Task<int> Main(string[] args)
    {
        var options = ProbeOptions.Parse(args);
        var definitions = CaseCatalog.All;
        if (!definitions.TryGetValue(options.CaseName, out var definition))
        {
            Console.Error.WriteLine($"Unknown case '{options.CaseName}'. Known: {string.Join(", ", definitions.Keys)}");
            return 2;
        }

        var runs = new List<ProbeRun>();
        for (var repeat = 0; repeat < options.Repeat; repeat++)
        {
            runs.Add(await RunOnceAsync(definition, repeat + 1).ConfigureAwait(false));
        }

        var document = new
        {
            case_name = definition.Name,
            case_description = definition.Description,
            driver_package = "Neo4j.Driver 6.4.0 (exact)",
            driver_assembly = typeof(GraphDatabase).Assembly.GetName().FullName,
            driver_informational_version =
                typeof(GraphDatabase).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion,
            handshake_response_concatenated_hex = Wire.Hex(definition.HandshakeResponse),
            write_segments = definition.Split.Select(s => new { offset = s.Offset, length = s.Length }),
            inter_segment_delay_ms = definition.DelayMs,
            server_handshake_mode = definition.HandshakeMode,
            hello_variant = definition.HelloVariant,
            logon_failure_variant = definition.LogonFailureVariant,
            run_count = runs.Count,
            runs,
        };

        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
            await File.WriteAllTextAsync(options.OutputPath, json + Environment.NewLine).ConfigureAwait(false);
        }

        Console.WriteLine(json);
        return runs.Any(r => r.Outcome == "unexpected") ? 3 : 0;
    }

    private static async Task<ProbeRun> RunOnceAsync(CaseDefinition definition, int iteration)
    {
        await using var server = new MinimalBoltServer(definition);
        var driver = GraphDatabase.Driver(
            new Uri($"bolt://127.0.0.1:{server.Port}"),
            AuthTokens.Basic("neo4j", "qualification-only"),
            o => o
                .WithEncryptionLevel(EncryptionLevel.None)
                .WithConnectionTimeout(TimeSpan.FromSeconds(10))
                .WithMaxConnectionPoolSize(1));

        Exception? failure = null;
        var queryValue = (long?)null;
        Exception? queryFailure = null;
        var verifySucceeded = false;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await driver.VerifyConnectivityAsync().WaitAsync(TimeSpan.FromSeconds(12)).ConfigureAwait(false);
            verifySucceeded = true;

            try
            {
                await using var session = driver.AsyncSession();
                var cursor = await session.RunAsync("RETURN 42 AS answer").WaitAsync(TimeSpan.FromSeconds(8))
                    .ConfigureAwait(false);
                if (await cursor.FetchAsync().WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false))
                {
                    queryValue = cursor.Current["answer"].As<long>();
                }

                await cursor.ConsumeAsync().WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                queryFailure = ex;
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            stopwatch.Stop();
        }

        var serverStillOpenAtFailure = failure is not null && server.OpenConnectionCount > 0;
        try
        {
            await driver.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure ??= ex;
        }

        await server.WaitForIdleAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
        var connections = server.Snapshot();

        return new ProbeRun
        {
            Iteration = iteration,
            Outcome = failure is null ? "success" : "failure",
            VerifyConnectivitySucceeded = verifySucceeded,
            QueryValue = queryValue,
            QueryFailureType = queryFailure?.GetType().FullName,
            QueryFailureMessage = OneLine(queryFailure?.Message),
            ElapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1),
            ExceptionType = failure?.GetType().FullName,
            ExceptionMessage = OneLine(failure?.Message),
            ExceptionStack = failure?.ToString(),
            InnerExceptionChain = Flatten(failure),
            ExceptionProperties = DescribeException(failure),
            ServerStillOpenAtFailure = serverStillOpenAtFailure,
            Connections = connections,
        };
    }

    private static string? OneLine(string? value) =>
        value is null ? null : value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static List<string> Flatten(Exception? exception)
    {
        var result = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            result.Add($"{current.GetType().FullName}: {OneLine(current.Message)}");
        }

        return result;
    }

    private static Dictionary<string, string?>? DescribeException(Exception? exception)
    {
        if (exception is null)
        {
            return null;
        }

        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in new[]
                 {
                     "Code", "GqlStatus", "GqlStatusDescription", "DiagnosticRecord", "ErrorCode",
                 })
        {
            var property = exception.GetType().GetProperty(name);
            if (property is null)
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(exception);
            }
            catch (Exception ex)
            {
                value = $"<reflection error: {ex.GetType().Name}>";
            }

            result[name] = value switch
            {
                null => null,
                string s => s,
                _ => JsonSerializer.Serialize(value),
            };
        }

        return result;
    }
}

internal sealed class ProbeOptions
{
    public required string CaseName { get; init; }

    public string? OutputPath { get; init; }

    public int Repeat { get; init; } = 1;

    public static ProbeOptions Parse(string[] args)
    {
        string? caseName = null;
        string? output = null;
        var repeat = 1;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--case" when i + 1 < args.Length:
                    caseName = args[++i];
                    break;
                case "--out" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--repeat" when i + 1 < args.Length:
                    repeat = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        return new ProbeOptions
        {
            CaseName = caseName ?? throw new ArgumentException("--case is required."),
            OutputPath = output,
            Repeat = repeat,
        };
    }
}

internal sealed record WriteSegment(int Offset, int Length);

internal sealed class CaseDefinition
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string HandshakeMode { get; init; }

    public required byte[] HandshakeResponse { get; init; }

    public required List<WriteSegment> Split { get; init; }

    public int DelayMs { get; init; }

    /// <summary>whole | empty | server-only | connection-id-only | no-hints</summary>
    public string HelloVariant { get; init; } = "whole";

    /// <summary>none | full | no-code | no-gql | empty | old-code | message-only | code-gql-no-description | neo4j-code-only</summary>
    public string LogonFailureVariant { get; init; } = "none";

    /// <summary>whole | perbyte | chunk-header | chunk-body | terminator | two-chunks</summary>
    public string PostHandshakeSplit { get; init; } = "whole";
}

internal static class CaseCatalog
{
    private static readonly byte[] LegacyResponse = { 0x00, 0x00, 0x08, 0x05 };
    private static readonly byte[] ManifestResponse =
    {
        0x00, 0x00, 0x01, 0xFF,
        0x01,
        0x00, 0x00, 0x08, 0x05,
        0x00,
    };

    public static readonly Dictionary<string, CaseDefinition> All = Build();

    private static Dictionary<string, CaseDefinition> Build()
    {
        var cases = new Dictionary<string, CaseDefinition>(StringComparer.Ordinal);

        void Legacy(string name, string description, (int Offset, int Length)[] split, int delay = 0) =>
            cases[name] = new CaseDefinition
            {
                Name = name,
                Description = description,
                HandshakeMode = "legacy",
                HandshakeResponse = LegacyResponse,
                Split = split.Select(s => new WriteSegment(s.Offset, s.Length)).ToList(),
                DelayMs = delay,
            };

        void Post(string name, string description, string postSplit) =>
            cases[name] = new CaseDefinition
            {
                Name = name,
                Description = description,
                HandshakeMode = "legacy",
                HandshakeResponse = LegacyResponse,
                Split = new List<WriteSegment> { new(0, LegacyResponse.Length) },
                PostHandshakeSplit = postSplit,
            };

        void Hello(string name, string description, string variant) =>
            cases[name] = new CaseDefinition
            {
                Name = name,
                Description = description,
                HandshakeMode = "legacy",
                HandshakeResponse = LegacyResponse,
                Split = new List<WriteSegment> { new(0, LegacyResponse.Length) },
                HelloVariant = variant,
            };

        void Logon(string name, string description, string variant) =>
            cases[name] = new CaseDefinition
            {
                Name = name,
                Description = description,
                HandshakeMode = "legacy",
                HandshakeResponse = LegacyResponse,
                Split = new List<WriteSegment> { new(0, LegacyResponse.Length) },
                LogonFailureVariant = variant,
            };

        // Handshake segmentation: identical concatenated bytes (00 00 08 05) in every case.
        Legacy("hs-whole", "4-byte handshake response in one write (control).", new[] { (0, 4) });
        Legacy("hs-1-3", "1 byte then 3 bytes, no delay.", new[] { (0, 1), (1, 3) });
        Legacy("hs-2-2", "2 bytes then 2 bytes, no delay.", new[] { (0, 2), (2, 2) });
        Legacy("hs-3-1", "3 bytes then 1 byte, no delay.", new[] { (0, 3), (3, 1) });
        Legacy("hs-1-1-1-1", "Four separate one-byte writes, no delay.", new[] { (0, 1), (1, 1), (2, 1), (3, 1) });
        Legacy("hs-1-3-d1", "1 byte then 3 bytes, 1 ms delay.", new[] { (0, 1), (1, 3) }, 1);
        Legacy("hs-1-3-d10", "1 byte then 3 bytes, 10 ms delay.", new[] { (0, 1), (1, 3) }, 10);
        Legacy("hs-1-3-d50", "1 byte then 3 bytes, 50 ms delay.", new[] { (0, 1), (1, 3) }, 50);
        Legacy("hs-1-3-d1000", "1 byte then 3 bytes, 1000 ms delay.", new[] { (0, 1), (1, 3) }, 1000);
        Legacy("hs-1-3-d3000", "1 byte then 3 bytes, 3000 ms delay.", new[] { (0, 1), (1, 3) }, 3000);
        Legacy("hs-2-2-d10", "2 bytes then 2 bytes, 10 ms delay.", new[] { (0, 2), (2, 2) }, 10);
        Legacy("hs-1-1-1-1-d10", "Four one-byte writes, 10 ms between writes.", new[] { (0, 1), (1, 1), (2, 1), (3, 1) }, 10);
        Legacy("hs-whole-d0-wait", "Whole response; control for socket staying open.", new[] { (0, 4) });
        cases["hs-manifest-whole"] = new CaseDefinition
        {
            Name = "hs-manifest-whole",
            Description = "Manifest v1 handshake response in one write (control).",
            HandshakeMode = "manifest",
            HandshakeResponse = ManifestResponse,
            Split = new List<WriteSegment> { new(0, ManifestResponse.Length) },
        };

        // Post-handshake boundaries: full valid protocol, only write segmentation differs.
        Post("ph-whole", "Framed HELLO SUCCESS in one write (control).", "whole");
        Post("ph-perbyte", "Every framed byte written separately.", "perbyte");
        Post("ph-chunk-header", "Chunk header (2 bytes) separated from the chunk body.", "chunk-header");
        Post("ph-chunk-body", "Chunk body separated from header and terminator.", "chunk-body");
        Post("ph-terminator", "Two-byte message terminator written separately.", "terminator");
        Post("ph-two-chunks", "HELLO SUCCESS split into two legal PackStream chunks.", "two-chunks");
        Post("ph-header-slow", "Chunk header separated from body with a 100 ms delay.", "chunk-header-slow");

        // HELLO SUCCESS metadata variants.
        Hello("hello-full", "HELLO SUCCESS {server, connection_id, hints} (control).", "whole");
        Hello("hello-empty", "HELLO SUCCESS {} (no metadata).", "empty");
        Hello("hello-server-only", "HELLO SUCCESS {server} only.", "server-only");
        Hello("hello-connection-id-only", "HELLO SUCCESS {connection_id} only.", "connection-id-only");
        Hello("hello-no-hints", "HELLO SUCCESS {server, connection_id} without hints.", "no-hints");
        Hello("hello-hints-only", "HELLO SUCCESS {hints} only (no server, no connection_id).", "hints-only");

        // LOGON FAILURE metadata variants.
        Logon("logon-failure-full", "LOGON FAILURE with gql_status, neo4j_code, description, diagnostic_record.", "full");
        Logon("logon-failure-no-code", "LOGON FAILURE without neo4j_code.", "no-code");
        Logon("logon-failure-no-gql", "LOGON FAILURE without gql_status.", "no-gql");
        Logon("logon-failure-empty", "LOGON FAILURE {}.", "empty");
        Logon("logon-failure-old-code", "LOGON FAILURE {code, message} (v1-v5.6 style).", "old-code");
        Logon("logon-failure-message-only", "LOGON FAILURE {message} only.", "message-only");
        Logon("logon-failure-code-gql-no-description", "LOGON FAILURE with gql_status and neo4j_code, no description.", "code-gql-no-description");
        Logon("logon-failure-neo4j-code-only", "LOGON FAILURE with neo4j_code only.", "neo4j-code-only");

        return cases;
    }
}

internal sealed class MinimalBoltServer : IAsyncDisposable
{
    private const string ServerName = "Neo4j/5.26.0";
    private const string ConnectionId = "bolt-q1-1";
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);
    private readonly TcpListener _listener;
    private readonly CaseDefinition _definition;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _acceptLoop;
    private readonly object _gate = new();
    private readonly List<Task> _handlers = new();
    private readonly List<TcpClient> _clients = new();
    private readonly List<ConnectionRecord> _records = new();
    private int _accepted;
    private int _disposed;

    public MinimalBoltServer(CaseDefinition definition)
    {
        _definition = definition;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start(8);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync();
    }

    public int Port { get; }

    public int OpenConnectionCount
    {
        get
        {
            lock (_gate)
            {
                return _clients.Count;
            }
        }
    }

    public IReadOnlyList<ConnectionRecord> Snapshot()
    {
        lock (_gate)
        {
            return _records
                .OrderBy(r => r.ConnectionIndex)
                .Select(r => r.Snapshot())
                .ToList();
        }
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested && Volatile.Read(ref _accepted) < 8)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                var index = Interlocked.Increment(ref _accepted);
                lock (_gate)
                {
                    _clients.Add(client);
                }

                var handler = HandleConnectionAsync(index, client);
                lock (_gate)
                {
                    _handlers.Add(handler);
                }
            }
        }
        finally
        {
            try
            {
                _listener.Stop();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public async Task WaitForIdleAsync(TimeSpan timeout)
    {
        Task[] handlers;
        lock (_gate)
        {
            handlers = _handlers.ToArray();
        }

        if (handlers.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(handlers).WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        catch (Exception)
        {
        }
    }

    private async Task HandleConnectionAsync(int index, TcpClient client)
    {
        var record = new ConnectionRecord { ConnectionIndex = index };
        lock (_gate)
        {
            _records.Add(record);
        }

        using var bound = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        bound.CancelAfter(TimeSpan.FromSeconds(15));
        var token = bound.Token;
        try
        {
            client.NoDelay = true;
            using var stream = client.GetStream();

            var preamble = await ReadExactAsync(stream, 20, token).ConfigureAwait(false);
            record.ClientHandshakeHex = Wire.Hex(preamble);

            if (_definition.HandshakeMode == "manifest")
            {
                await WriteSplitAsync(client, stream, _definition.HandshakeResponse, token, record).ConfigureAwait(false);
                var selection = await ReadExactAsync(stream, 4, token).ConfigureAwait(false);
                record.ManifestSelectionHex = Wire.Hex(selection);
                // Read the client's capabilities VarInt.
                await ReadVarIntAsync(stream, token).ConfigureAwait(false);
            }
            else
            {
                await WriteSplitAsync(client, stream, _definition.HandshakeResponse, token, record).ConfigureAwait(false);
            }

            while (!token.IsCancellationRequested)
            {
                byte[]? message;
                try
                {
                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    readTimeout.CancelAfter(ReadTimeout);
                    message = await ReadMessageAsync(stream, readTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    record.Notes.Add("read timeout");
                    break;
                }
                catch (IOException ex)
                {
                    record.Notes.Add($"read io error: {ex.GetType().Name}: {OneLine(ex.Message)}");
                    break;
                }

                if (message is null || message.Length < 2 || (message[0] & 0xF0) != 0xB0)
                {
                    record.Notes.Add(message is null ? "client closed" : "unrecognised message");
                    break;
                }

                var name = Wire.MsgName(message[1]);
                record.Requests.Add(name);
                record.RequestTimesMs.Add(record.Stopwatch.Elapsed.TotalMilliseconds);

                switch (name)
                {
                    case "HELLO":
                        await WriteMessageAsync(stream, HelloSuccess(_definition.HelloVariant), token, record)
                            .ConfigureAwait(false);
                        break;
                    case "LOGON":
                        await WriteMessageAsync(stream, LogonResponse(_definition.LogonFailureVariant), token, record)
                            .ConfigureAwait(false);
                        if (_definition.LogonFailureVariant != "none")
                        {
                            record.Notes.Add("logon failure sent; connection kept open for observation");
                        }

                        break;
                    case "RUN":
                        await WriteMessageAsync(stream, RunSuccess(), token, record).ConfigureAwait(false);
                        break;
                    case "PULL":
                        await WriteMessageAsync(stream, Record42(), token, record).ConfigureAwait(false);
                        await WriteMessageAsync(stream, SuccessHasMore(false), token, record).ConfigureAwait(false);
                        break;
                    case "DISCARD":
                    case "RESET":
                        await WriteMessageAsync(stream, SuccessEmpty(), token, record).ConfigureAwait(false);
                        break;
                    case "GOODBYE":
                        record.Notes.Add("GOODBYE received");
                        client.Close();
                        return;
                    default:
                        record.Notes.Add($"unhandled request {name}");
                        client.Close();
                        return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            record.Notes.Add("connection bound elapsed");
        }
        catch (SocketException ex)
        {
            record.Notes.Add($"socket error: {ex.SocketErrorCode}");
        }
        catch (IOException ex)
        {
            record.Notes.Add($"io error: {ex.GetType().Name}: {OneLine(ex.Message)}");
        }
        catch (Exception ex)
        {
            record.Notes.Add($"error: {ex.GetType().Name}: {OneLine(ex.Message)}");
        }
        finally
        {
            record.Stopwatch.Stop();
            record.DurationMs = Math.Round(record.Stopwatch.Elapsed.TotalMilliseconds, 1);
            try
            {
                client.Close();
            }
            catch (ObjectDisposedException)
            {
            }

            lock (_gate)
            {
                _clients.Remove(client);
            }
        }
    }

    private async Task WriteMessageAsync(
        Stream stream,
        byte[] message,
        CancellationToken token,
        ConnectionRecord record)
    {
        var framed = Wire.Frame(message);
        switch (_definition.PostHandshakeSplit)
        {
            case "perbyte":
                await WriteSplitAsync(
                    null,
                    stream,
                    framed,
                    token,
                    record,
                    Enumerable.Range(0, framed.Length).Select(i => new WriteSegment(i, 1)).ToList())
                    .ConfigureAwait(false);
                break;
            case "chunk-header":
                await WriteSplitAsync(
                    null,
                    stream,
                    framed,
                    token,
                    record,
                    new List<WriteSegment> { new(0, 2), new(2, framed.Length - 2) })
                    .ConfigureAwait(false);
                break;
            case "chunk-body":
                await WriteSplitAsync(
                    null,
                    stream,
                    framed,
                    token,
                    record,
                    new List<WriteSegment>
                    {
                        new(0, 2),
                        new(2, framed.Length - 4),
                        new(framed.Length - 2, 2),
                    })
                    .ConfigureAwait(false);
                break;
            case "terminator":
                await WriteSplitAsync(
                    null,
                    stream,
                    framed,
                    token,
                    record,
                    new List<WriteSegment> { new(0, framed.Length - 2), new(framed.Length - 2, 2) })
                    .ConfigureAwait(false);
                break;
            case "two-chunks":
            {
                var bodyLength = framed.Length - 4;
                var firstLength = bodyLength / 2;
                var chunk1 = Wire.FrameChunk(framed.AsSpan(2, firstLength));
                var chunk2 = Wire.FrameChunk(framed.AsSpan(2 + firstLength, bodyLength - firstLength));
                var combined = Wire.Concat(chunk1, chunk2, new byte[] { 0x00, 0x00 });
                await WriteSplitAsync(
                    null,
                    stream,
                    combined,
                    token,
                    record,
                    new List<WriteSegment> { new(0, combined.Length) })
                    .ConfigureAwait(false);
                break;
            }
            case "chunk-header-slow":
                await WriteSplitAsync(
                    null,
                    stream,
                    framed,
                    token,
                    record,
                    new List<WriteSegment> { new(0, 2), new(2, framed.Length - 2) },
                    delayMs: 100)
                    .ConfigureAwait(false);
                break;
            default:
                await WriteSplitAsync(
                    null,
                    stream,
                    framed,
                    token,
                    record,
                    new List<WriteSegment> { new(0, framed.Length) })
                    .ConfigureAwait(false);
                break;
        }
    }

    private async Task WriteSplitAsync(
        TcpClient? client,
        Stream stream,
        byte[] bytes,
        CancellationToken token,
        ConnectionRecord record,
        List<WriteSegment>? overrideSegments = null,
        int? delayMs = null)
    {
        var segments = overrideSegments ?? _definition.Split;
        var delay = delayMs ?? _definition.DelayMs;
        for (var i = 0; i < segments.Count; i++)
        {
            if (i > 0 && delay > 0)
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
            }

            var segment = segments[i];
            record.WriteSegments.Add(new WriteSegmentRecord
            {
                Offset = segment.Offset,
                Length = segment.Length,
                AtMs = Math.Round(record.Stopwatch.Elapsed.TotalMilliseconds, 1),
            });

            if (client is not null)
            {
                client.Client.Send(bytes, segment.Offset, segment.Length, SocketFlags.None);
            }
            else
            {
                await stream.WriteAsync(bytes.AsMemory(segment.Offset, segment.Length), token)
                    .ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();
        try
        {
            _listener.Stop();
        }
        catch (ObjectDisposedException)
        {
        }

        TcpClient[] clients;
        lock (_gate)
        {
            clients = _clients.ToArray();
        }

        foreach (var client in clients)
        {
            try
            {
                client.Close();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        try
        {
            await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        await WaitForIdleAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        _shutdown.Dispose();
    }

    // ---- Minimal wire helpers (independent of BoltSharp) ----

    private static async Task<byte[]?> ReadMessageAsync(Stream stream, CancellationToken token)
    {
        using var body = new MemoryStream();
        while (true)
        {
            var header = await ReadExactAsync(stream, 2, token).ConfigureAwait(false);
            if (header.Length < 2)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(header);
            if (length == 0)
            {
                if (body.Length == 0)
                {
                    continue; // NOOP chunk
                }

                return body.ToArray();
            }

            var payload = await ReadExactAsync(stream, length, token).ConfigureAwait(false);
            if (payload.Length < length)
            {
                return null;
            }

            body.Write(payload);
        }
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken token)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, count - read), token).ConfigureAwait(false);
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        return read == count ? buffer : buffer.AsSpan(0, read).ToArray();
    }

    private static async Task<ulong> ReadVarIntAsync(Stream stream, CancellationToken token)
    {
        ulong value = 0;
        var shift = 0;
        while (shift < 70)
        {
            var one = new byte[1];
            var n = await stream.ReadAsync(one, token).ConfigureAwait(false);
            if (n <= 0)
            {
                break;
            }

            value |= (ulong)(one[0] & 0x7F) << shift;
            if ((one[0] & 0x80) == 0)
            {
                break;
            }

            shift += 7;
        }

        return value;
    }

    private static byte[] HelloSuccess(string variant)
    {
        var fields = variant switch
        {
            "empty" => Array.Empty<(string, byte[])>(),
            "server-only" => new[] { ("server", Wire.Str(ServerName)) },
            "connection-id-only" => new[] { ("connection_id", Wire.Str(ConnectionId)) },
            "hints-only" => new[] { ("hints", Wire.Map()) },
            "no-hints" => new[] { ("server", Wire.Str(ServerName)), ("connection_id", Wire.Str(ConnectionId)) },
            _ => new[]
            {
                ("server", Wire.Str(ServerName)),
                ("connection_id", Wire.Str(ConnectionId)),
                ("hints", Wire.Map()),
            },
        };
        return Wire.Struct(0x70, Wire.Map(fields));
    }

    private static byte[] LogonResponse(string failureVariant)
    {
        if (failureVariant == "none")
        {
            return SuccessEmpty();
        }

        var fields = failureVariant switch
        {
            "full" => new (string, byte[])[]
            {
                ("gql_status", Wire.Str("50N42")),
                ("neo4j_code", Wire.Str("Neo.ClientError.Security.Unauthorized")),
                ("description", Wire.Str("The client is unauthorized due to authentication failure.")),
                ("diagnostic_record", Wire.Map()),
            },
            "no-code" => new (string, byte[])[]
            {
                ("gql_status", Wire.Str("50N42")),
                ("description", Wire.Str("The client is unauthorized due to authentication failure.")),
                ("diagnostic_record", Wire.Map()),
            },
            "no-gql" => new (string, byte[])[]
            {
                ("neo4j_code", Wire.Str("Neo.ClientError.Security.Unauthorized")),
                ("description", Wire.Str("The client is unauthorized due to authentication failure.")),
            },
            "empty" => Array.Empty<(string, byte[])>(),
            "old-code" => new (string, byte[])[]
            {
                ("code", Wire.Str("Neo.ClientError.Security.Unauthorized")),
                ("message", Wire.Str("The client is unauthorized due to authentication failure.")),
            },
            "message-only" => new (string, byte[])[]
            {
                ("message", Wire.Str("The client is unauthorized due to authentication failure.")),
            },
            "code-gql-no-description" => new (string, byte[])[]
            {
                ("gql_status", Wire.Str("50N42")),
                ("neo4j_code", Wire.Str("Neo.ClientError.Security.Unauthorized")),
            },
            "neo4j-code-only" => new (string, byte[])[]
            {
                ("neo4j_code", Wire.Str("Neo.ClientError.Security.Unauthorized")),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(failureVariant)),
        };
        return Wire.Struct(0x7F, Wire.Map(fields));
    }

    private static byte[] RunSuccess() => Wire.Struct(0x70, Wire.Map(("fields", Wire.List(Wire.Str("answer")))));

    private static byte[] Record42() => Wire.Struct(0x71, Wire.List(Wire.Int(42)));

    private static byte[] SuccessHasMore(bool value) =>
        Wire.Struct(0x70, Wire.Map(("has_more", Wire.Bool(value))));

    private static byte[] SuccessEmpty() => Wire.Struct(0x70, Wire.Map());

    private static string? OneLine(string? value) =>
        value is null ? null : value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}

internal sealed class ConnectionRecord
{
    public int ConnectionIndex { get; init; }

    public string? ClientHandshakeHex { get; set; }

    public string? ManifestSelectionHex { get; set; }

    public List<string> Requests { get; } = new();

    public List<double> RequestTimesMs { get; } = new();

    public List<WriteSegmentRecord> WriteSegments { get; } = new();

    public List<string> Notes { get; } = new();

    public double DurationMs { get; set; }

    public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();

    public ConnectionRecord Snapshot()
    {
        var copy = new ConnectionRecord
        {
            ConnectionIndex = ConnectionIndex,
            ClientHandshakeHex = ClientHandshakeHex,
            ManifestSelectionHex = ManifestSelectionHex,
            DurationMs = DurationMs,
        };
        copy.Requests.AddRange(Requests);
        copy.RequestTimesMs.AddRange(RequestTimesMs);
        copy.WriteSegments.AddRange(WriteSegments.Select(s => s with { }));
        copy.Notes.AddRange(Notes);
        return copy;
    }
}

internal sealed record WriteSegmentRecord
{
    public required int Offset { get; init; }

    public required int Length { get; init; }

    public required double AtMs { get; init; }
}

internal sealed class ProbeRun
{
    public required int Iteration { get; init; }

    public required string Outcome { get; init; }

    public required bool VerifyConnectivitySucceeded { get; init; }

    public long? QueryValue { get; init; }

    public string? QueryFailureType { get; init; }

    public string? QueryFailureMessage { get; init; }

    public required double ElapsedMs { get; init; }

    public string? ExceptionType { get; init; }

    public string? ExceptionMessage { get; init; }

    public string? ExceptionStack { get; init; }

    public List<string>? InnerExceptionChain { get; init; }

    public Dictionary<string, string?>? ExceptionProperties { get; init; }

    public required bool ServerStillOpenAtFailure { get; init; }

    public required IReadOnlyList<ConnectionRecord> Connections { get; init; }
}

internal static class Wire
{
    public static byte[] Str(string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value);
        if (utf8.Length <= 15)
        {
            return Concat(new[] { (byte)(0x80 | utf8.Length) }, utf8);
        }

        if (utf8.Length <= byte.MaxValue)
        {
            return Concat(new byte[] { 0xD0, (byte)utf8.Length }, utf8);
        }

        var header = new byte[3];
        header[0] = 0xD1;
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(1), (ushort)utf8.Length);
        return Concat(header, utf8);
    }

    public static byte[] Int(long value)
    {
        if (value is >= -16 and <= 127)
        {
            return new[] { (byte)value };
        }

        if (value is >= sbyte.MinValue and <= sbyte.MaxValue)
        {
            return new byte[] { 0xC8, (byte)value };
        }

        if (value is >= short.MinValue and <= short.MaxValue)
        {
            var b = new byte[3];
            b[0] = 0xC9;
            BinaryPrimitives.WriteInt16BigEndian(b.AsSpan(1), (short)value);
            return b;
        }

        if (value is >= int.MinValue and <= int.MaxValue)
        {
            var b = new byte[5];
            b[0] = 0xCA;
            BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(1), (int)value);
            return b;
        }

        var o = new byte[9];
        o[0] = 0xCB;
        BinaryPrimitives.WriteInt64BigEndian(o.AsSpan(1), value);
        return o;
    }

    public static byte[] Bool(bool value) => new[] { value ? (byte)0xC3 : (byte)0xC2 };

    public static byte[] List(params byte[][] items)
    {
        var header = items.Length <= 15
            ? new[] { (byte)(0x90 | items.Length) }
            : new[] { (byte)0xD4, (byte)items.Length };
        return Concat(header, items.SelectMany(x => x).ToArray());
    }

    public static byte[] Map(params (string Key, byte[] Value)[] entries)
    {
        if (entries.Length == 0)
        {
            return new[] { (byte)0xA0 };
        }

        var parts = new List<byte>();
        parts.Add((byte)(0xA0 | entries.Length));
        foreach (var (key, value) in entries)
        {
            parts.AddRange(Str(key));
            parts.AddRange(value);
        }

        return parts.ToArray();
    }

    public static byte[] Struct(byte tag, params byte[][] fields) =>
        Concat(new[] { (byte)(0xB0 | fields.Length), tag }, fields.SelectMany(x => x).ToArray());

    public static byte[] Frame(byte[] message) =>
        Concat(new[] { (byte)(message.Length >> 8), (byte)message.Length }, message, new byte[] { 0x00, 0x00 });

    public static byte[] FrameChunk(ReadOnlySpan<byte> payload)
    {
        var header = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)payload.Length);
        return Concat(header, payload.ToArray());
    }

    public static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    public static string Hex(byte[] bytes) => Convert.ToHexString(bytes) is { } hex && hex.Length > 0
        ? string.Join(' ', Enumerable.Range(0, hex.Length / 2).Select(i => hex.Substring(i * 2, 2)))
        : string.Empty;

    public static string MsgName(byte signature) => signature switch
    {
        0x01 => "HELLO",
        0x02 => "GOODBYE",
        0x0F => "RESET",
        0x10 => "RUN",
        0x11 => "BEGIN",
        0x12 => "COMMIT",
        0x13 => "ROLLBACK",
        0x2F => "DISCARD",
        0x3F => "PULL",
        0x54 => "TELEMETRY",
        0x66 => "ROUTE",
        0x6A => "LOGON",
        0x6B => "LOGOFF",
        _ => $"UNKNOWN_0x{signature:X2}",
    };
}
