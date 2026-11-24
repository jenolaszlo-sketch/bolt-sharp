using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoltSharp.Framing;
using BoltSharp.Handshake;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Transport;

namespace R1Harness;

internal sealed record Profile(int Ordinal, int PrecedingBytes, int PeerReceive, int TargetSend);

internal sealed record CellSpec(string Id, string Kind, int Seed, int DiscoveryBound, int MaxRuns, int Repeats);

internal static class Campaign
{
    public const string Id = "Q-B5B-09-TLS-LINUX-QUAL-01-R1";
    public const int AttemptLifetimeMs = 3500;
    public const int MinimumPendingMs = 20;
    public const int TerminalPendingBoundMs = 310;
    public const int CloseNotifyPendingBoundMs = 120;
    public const int TotalElapsedBoundMs = 500;
    public const int CiphertextLimit = 32768;
    public const int PlaintextLimit = 8192;
    /// <summary>Independent budget for the close-notify preceding fixture write (E1: 750 ms).</summary>
    public const int PrecedingWriteTimeoutMs = 750;
    /// <summary>Close-notify preceding fixture size: written with the default send buffer, then
    /// SO_SNDBUF is shrunk below it so the close_notify write cannot be admitted (E1 probe
    /// mechanism). Needed above any doubled SO_SNDBUF (8192 > 2*4096).</summary>
    public const int CloseNotifyFixtureBytes = 8192;
    /// <summary>L4 adaptive fill bound: the proxied send path absorbs a bounded amount, so the
    /// close-notify fixture is written in chunks until the path saturates or this bound. The
    /// bound exceeds the whole chain capacity (sender send buffer + proxy receive buffer), so
    /// the send buffer is genuinely saturated when the alert is written; the subsequent shrink
    /// below the queued bytes is the second enforcement of the same fact (E1 mechanism).</summary>
    public const int CloseNotifyFillBoundBytes = 128 * 1024;
    /// <summary>L4 gated-proxy forwarding bound: the proxy engages the gate before forwarding
    /// anything, so the sender's held data is never drained until the peer listener is
    /// cancelled. This is what keeps the sender's close_notify pending (the sender keeps its
    /// own queued bytes while SO_SNDBUF sits below them).</summary>
    public const int ProxyForwardBoundBytes = 0;
    /// <summary>E1 cap profile: preceding fixture 4096 bytes, peer receive 4096, no send shrink.</summary>
    public static readonly Profile CapProfile = new(1, 4096, 4096, 0);

    public static readonly Profile[] Profiles =
    [
        new(1, 36864, 2048, 2048),
        new(2, 45056, 1024, 1024),
        new(3, 49152, 512, 512),
        new(4, 262144, 1024, 2048)
    ];

    public static readonly CellSpec[] Cells =
    [
        new("L1", "production-terminal-deadline", 1001, 4, 6, 2),
        new("L2", "production-terminal-listener", 1002, 4, 6, 2),
        new("L3", "candidate-close-notify-phase", 1003, 4, 6, 2),
        new("L4", "candidate-close-notify-proxy-listener", 1004, 4, 6, 2),
        new("L5", "candidate-ciphertext-cap", 1005, 3, 3, 0),
        new("L6", "candidate-plaintext-cap", 1006, 3, 3, 0),
        new("L7", "production-peer-receipt", 1007, 4, 6, 2)
    ];

    public static readonly CellSpec L6Production = new("L6-PROD", "production-plaintext-cap", 1006, 3, 3, 0);

    public static Profile ProfileFor(int seed, int discoveryIndex)
    {
        // The registered seed replays the ordered discovery profiles deterministically.
        _ = seed;
        return Profiles[Math.Clamp(discoveryIndex, 0, Profiles.Length - 1)];
    }
}

internal sealed class Journal : IDisposable
{
    /// <summary>BOM-less UTF-8: Python json/verifier tooling must parse every byte stream.</summary>
    internal static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly object _gate = new();
    private readonly string _perCellDir;
    private readonly string _transcriptDir;
    private readonly string _controlsDir;
    private readonly string _peerDir;
    private readonly string _combined;
    private readonly string _runId;
    private readonly long _started = Stopwatch.GetTimestamp();
    private bool _disposed;

    internal Journal(string directory, string runId)
    {
        Directory = directory;
        _runId = runId;
        _perCellDir = Path.Combine(directory, "per-cell");
        _transcriptDir = Path.Combine(directory, "transcripts");
        _controlsDir = Path.Combine(directory, "negative-controls");
        _peerDir = Path.Combine(directory, "peer");
        _combined = Path.Combine(directory, "campaign-journal.jsonl");
        System.IO.Directory.CreateDirectory(_perCellDir);
        System.IO.Directory.CreateDirectory(_transcriptDir);
        System.IO.Directory.CreateDirectory(_controlsDir);
        System.IO.Directory.CreateDirectory(_peerDir);
        // CreateNew is deliberate: a fresh run must never append to an earlier journal.
        using (File.Open(_combined, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
    }

    internal string Directory { get; }

    internal string RunId => _runId;

    internal string PeerDir => _peerDir;

    internal double ElapsedMs => Stopwatch.GetElapsedTime(_started).TotalMilliseconds;

    internal Dictionary<string, object?> Record(string recordType, string? cell = null, int? attempt = null)
        => new()
        {
            ["run_id"] = _runId,
            ["record_type"] = recordType,
            ["cell"] = cell,
            ["attempt"] = attempt,
            ["ts_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["monotonic_ms"] = Math.Round(ElapsedMs, 3)
        };

    internal void Write(Dictionary<string, object?> record)
    {
        record.TryAdd("run_id", _runId);
        record.TryAdd("ts_utc", DateTimeOffset.UtcNow.ToString("O"));
        record.TryAdd("monotonic_ms", Math.Round(ElapsedMs, 3));
        var line = JsonSerializer.Serialize(record);
        lock (_gate)
        {
            File.AppendAllText(_combined, line + "\n", Utf8NoBom);
            if (record.TryGetValue("cell", out var cell) && cell is string cellId && cellId.Length > 0)
            {
                var path = Path.Combine(_perCellDir, cellId + ".jsonl");
                File.AppendAllText(path, line + "\n", Utf8NoBom);
            }
        }
    }

    internal void WriteControl(string control, Dictionary<string, object?> record)
    {
        record["control"] = control;
        record.TryAdd("run_id", _runId);
        record.TryAdd("ts_utc", DateTimeOffset.UtcNow.ToString("O"));
        record.TryAdd("monotonic_ms", Math.Round(ElapsedMs, 3));
        var line = JsonSerializer.Serialize(record);
        lock (_gate)
        {
            File.AppendAllText(Path.Combine(_controlsDir, control + ".jsonl"), line + "\n", Utf8NoBom);
        }
    }

    internal void Transcript(string cell, string line)
    {
        lock (_gate)
        {
            File.AppendAllText(Path.Combine(_transcriptDir, cell + ".log"),
                $"{DateTimeOffset.UtcNow:O} {line}\n", Utf8NoBom);
        }
    }

    internal void WriteCellSummary(string cell, Dictionary<string, object?> summary)
    {
        summary["run_id"] = _runId;
        var text = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        lock (_gate)
        {
            File.WriteAllText(Path.Combine(_perCellDir, cell + ".summary.json"), text + "\n", Utf8NoBom);
        }
    }

    internal void WriteMeta(Dictionary<string, object?> meta)
    {
        meta["run_id"] = _runId;
        var text = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
        lock (_gate)
        {
            File.WriteAllText(Path.Combine(Directory, "run-meta.json"), text + "\n", Utf8NoBom);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}

internal static class Certificates
{
    internal static X509Certificate2 Create(out string certPem, out string keyPem)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var generated = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
        var pfx = generated.Export(X509ContentType.Pfx);
        var persisted = new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.Exportable);
        certPem = persisted.ExportCertificatePem();
        using var key = persisted.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("generated certificate has no RSA private key");
        keyPem = key.ExportPkcs8PrivateKeyPem();
        return persisted;
    }
}

internal static class Wire
{
    internal static readonly byte[] Handshake =
    [
        0x60, 0x60, 0xB0, 0x17,
        0x00, 0x00, 0x08, 0x05,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    ];

    internal static readonly byte[] Selected = [0x00, 0x00, 0x08, 0x05];

    internal static readonly byte[] TerminalBegin = [0xB1, 0x11, 0xA0];

    internal static byte[] Frame(byte[] payload)
    {
        var bytes = new byte[payload.Length + 4];
        bytes[0] = (byte)(payload.Length >> 8);
        bytes[1] = (byte)(payload.Length & 0xFF);
        payload.CopyTo(bytes, 2);
        bytes[^2] = 0;
        bytes[^1] = 0;
        return bytes;
    }

    internal static byte[] Encode(BoltRequest request)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        switch (request)
        {
            case HelloRequest hello:
                writer.WriteStructure(BoltMessageSignatures.Hello, new PackStreamValue?[] { hello.Extra });
                break;
            case LogonRequest logon:
                writer.WriteStructure(BoltMessageSignatures.Logon, new PackStreamValue?[] { logon.Auth });
                break;
            case RunRequest run:
                writer.WriteStructure(BoltMessageSignatures.Run,
                    new PackStreamValue?[] { new PackStreamString(run.Query), run.Parameters, run.Extra });
                break;
            case PullRequest pull:
                writer.WriteStructure(BoltMessageSignatures.Pull,
                    new PackStreamValue?[] { NAndQid(pull.N, pull.Qid) });
                break;
            case GoodbyeRequest:
                writer.WriteStructure(BoltMessageSignatures.Goodbye, Array.Empty<PackStreamValue?>());
                break;
            default:
                throw new NotSupportedException("harness encoder does not support " + request.GetType().Name);
        }

        return buffer.WrittenSpan.ToArray();
    }

    internal static PackStreamMap Map(params (string Key, PackStreamValue? Value)[] entries)
    {
        var list = new List<KeyValuePair<string, PackStreamValue?>>(entries.Length);
        foreach (var (key, value) in entries)
            list.Add(new KeyValuePair<string, PackStreamValue?>(key, value));
        return new PackStreamMap(list);
    }

    internal static PackStreamMap EmptyMap => Map();

    internal static HelloRequest Hello() => new(Map(
        ("user_agent", new PackStreamString("r1-harness/1.0")),
        ("bolt_agent", Map(("product", new PackStreamString("r1-harness/1.0"))))));

    internal static LogonRequest Logon() => new(Map(
        ("scheme", new PackStreamString("basic")),
        ("principal", new PackStreamString("neo4j")),
        ("credentials", new PackStreamString("secret"))),
        BoltAuthScheme.Basic);

    internal static RunRequest Run() => new("RETURN 1", EmptyMap, EmptyMap);

    internal static PullRequest Pull(long n) => new(n, null);

    internal static GoodbyeRequest Goodbye() => new();

    private static PackStreamMap NAndQid(long n, long? qid)
    {
        var entries = new List<KeyValuePair<string, PackStreamValue?>>
        {
            new("n", new PackStreamInteger(n))
        };
        if (qid is { } qidValue)
            entries.Add(new KeyValuePair<string, PackStreamValue?>("qid", new PackStreamInteger(qidValue)));
        return new PackStreamMap(entries);
    }

    internal static byte[] ExpectedTerminalFailure()
    {
        var failure = FailureResponse.FromEntries(
            ("gql_status", new PackStreamString("08N06")),
            ("neo4j_code", new PackStreamString("Neo.ClientError.Request.Invalid")),
            ("description", new PackStreamString(
                "Explicit transactions and routing are not supported by this server.")),
            ("diagnostic_record", EmptyMap));
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BoltMessageWriter(buffer);
        // BoltMessageWriter.WriteMessage already emits the complete chunked transfer encoding
        // ([2-byte chunk length][payload][0x0000]); wrapping its output in Wire.Frame again
        // would double-frame the expectation and never match a peer's framing-aware receipt.
        writer.WriteMessage(new BoltMessageCodec().EncodeResponse(failure));
        return buffer.WrittenSpan.ToArray();
    }

    internal static string Sha256Hex(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal sealed class ScriptedOperation : IBoltOperation
{
    private readonly Queue<IReadOnlyList<PackStreamValue?>> _rows;

    internal ScriptedOperation(IReadOnlyList<string> fields, params IReadOnlyList<PackStreamValue?>[] rows)
    {
        Fields = fields;
        _rows = new Queue<IReadOnlyList<PackStreamValue?>>(rows);
    }

    public IReadOnlyList<string> Fields { get; }

    public int ReadCount { get; private set; }

    public IReadOnlyList<PackStreamValue?>? ReadRow()
    {
        ReadCount++;
        return _rows.Count > 0 ? _rows.Dequeue() : null;
    }

    public void Dispose()
    {
    }
}

internal sealed class ScriptedProvider : IBoltOperationProvider
{
    private readonly Func<RunRequest, IBoltOperation?> _factory;
    private int _prepareCount;

    internal ScriptedProvider(IBoltOperation? operation) => _factory = _ => operation;

    internal int PrepareCount => Volatile.Read(ref _prepareCount);

    public IBoltOperation? Prepare(RunRequest run)
    {
        Interlocked.Increment(ref _prepareCount);
        return _factory(run);
    }

    internal static ScriptedProvider WithRow(int bytes)
    {
        var row = new PackStreamValue?[] { new PackStreamString(new string('x', bytes)) };
        return new ScriptedProvider(new ScriptedOperation(["value"], row));
    }
}

internal sealed class AcceptAllVerifier : IBoltCredentialVerifier
{
    public bool Verify(LogonRequest logon) => true;
}

internal sealed class BoltPeer : IDisposable
{
    private readonly BoltMessageReader _reader;
    private readonly BoltMessageCodec _codec = new();

    private BoltPeer(TcpClient tcp, NetworkStream raw, SslStream tls, BoltMessageReader reader)
    {
        Tcp = tcp;
        Raw = raw;
        Tls = tls;
        _reader = reader;
    }

    internal TcpClient Tcp { get; }

    internal NetworkStream Raw { get; }

    internal SslStream Tls { get; }

    internal int Available
    {
        get
        {
            try { return Tcp.Available; }
            catch (Exception) { return -1; }
        }
    }

    internal int LocalPort => ((IPEndPoint)Tcp.Client.LocalEndPoint!).Port;

    internal bool SenderWritable()
    {
        try { return Tcp.Client.Poll(0, SelectMode.SelectWrite); }
        catch (Exception) { return false; }
    }

    internal static async Task<BoltPeer> ConnectAsync(IPEndPoint endpoint, X509Certificate2 expected,
        int receiveBuffer, CancellationToken token, System.Security.Authentication.SslProtocols? protocols = null)
    {
        var tcp = new TcpClient(AddressFamily.InterNetwork);
        if (receiveBuffer > 0)
            tcp.ReceiveBufferSize = receiveBuffer;
        await tcp.ConnectAsync(endpoint, token).ConfigureAwait(false);
        var raw = tcp.GetStream();
        var tls = new SslStream(raw, leaveInnerStreamOpen: true);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "localhost",
            EnabledSslProtocols = protocols ?? System.Security.Authentication.SslProtocols.None,
            RemoteCertificateValidationCallback = (_, cert, _, _) =>
                cert?.GetCertHashString() == expected.Thumbprint
        }, token).ConfigureAwait(false);
        return new BoltPeer(tcp, raw, tls, new BoltMessageReader(tls));
    }

    internal async Task AuthenticateAsync(CancellationToken token)
    {
        await Tls.WriteAsync(Wire.Handshake, token).ConfigureAwait(false);
        var selected = new byte[4];
        await ReadExactlyAsync(selected, token).ConfigureAwait(false);
        if (!selected.SequenceEqual(Wire.Selected))
            throw new InvalidOperationException("unexpected handshake selection");
        foreach (var request in new BoltRequest[] { Wire.Hello(), Wire.Logon(), Wire.Run() })
        {
            await SendAsync(Wire.Frame(Wire.Encode(request)), token).ConfigureAwait(false);
            await ReadSuccessAsync(token).ConfigureAwait(false);
        }
    }

    internal Task SendAsync(byte[] bytes, CancellationToken token) => Tls.WriteAsync(bytes, token).AsTask();

    internal async Task ReadSuccessAsync(CancellationToken token)
    {
        var message = await _reader.ReadMessageAsync(token).ConfigureAwait(false)
            ?? throw new EndOfStreamException("connection closed before a response");
        var response = _codec.DecodeResponse(message);
        if (response is not SuccessResponse)
            throw new InvalidOperationException("expected SUCCESS, got " + response.GetType().Name);
    }

    internal async Task<BoltResponse?> ReadResponseAsync(CancellationToken token)
    {
        var message = await _reader.ReadMessageAsync(token).ConfigureAwait(false);
        return message is null ? null : _codec.DecodeResponse(message);
    }

    internal async Task<string> ReadUntilEofAsync(TimeSpan timeout, CancellationToken token)
    {
        var total = 0;
        try
        {
            var buffer = new byte[4096];
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeout);
            while (true)
            {
                var read = await Tls.ReadAsync(buffer, cts.Token).ConfigureAwait(false);
                if (read == 0)
                    return $"clean-eof after {total} bytes";
                total += read;
            }
        }
        catch (OperationCanceledException)
        {
            return $"read-timeout after {total} bytes";
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name} after {total} bytes";
        }
    }

    private async Task ReadExactlyAsync(byte[] destination, CancellationToken token)
    {
        var read = 0;
        while (read < destination.Length)
        {
            var count = await Tls.ReadAsync(destination.AsMemory(read), token).ConfigureAwait(false);
            if (count == 0)
                throw new EndOfStreamException("connection closed during read");
            read += count;
        }
    }

    public void Dispose()
    {
        Tls.Dispose();
        Raw.Dispose();
        Tcp.Dispose();
    }
}

internal sealed class PythonProcess : IDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _stderr = new();

    private PythonProcess(Process process)
    {
        _process = process;
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (_stderr) _stderr.AppendLine(e.Data);
            }
        };
        _process.BeginErrorReadLine();
    }

    internal int Id => _process.Id;

    internal int? ExitCode => _process.HasExited ? _process.ExitCode : null;

    internal string StandardError
    {
        get
        {
            lock (_stderr) return _stderr.ToString();
        }
    }

    internal static PythonProcess Start(string python, string script, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo(python)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(script);
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);
        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException("could not start " + python);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"could not start python peer ({python} {script}): {ex.GetType().Name}:{ex.Message}", ex);
        }
        return new PythonProcess(process);
    }

    /// <summary>
    /// Read the peer's stdout port line. Returns null on timeout, and surfaces the peer's
    /// stderr instead of a null-dereference crash when the peer exits without printing
    /// (the R3 L4 failure mode: int.Parse(null) masked the real launch fault).
    /// </summary>
    internal async Task<string?> ReadLineAsync(TimeSpan timeout, CancellationToken token)
    {
        if (_process.HasExited)
            throw new InvalidOperationException(
                $"python peer exited before reporting (exit {_process.ExitCode}): {StandardError.Trim()}");
        var read = _process.StandardOutput.ReadLineAsync();
        try
        {
            return await read.WaitAsync(timeout, token).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            if (_process.HasExited)
                throw new InvalidOperationException(
                    $"python peer exited before reporting (exit {_process.ExitCode}): {StandardError.Trim()}");
            return null;
        }
    }

    internal async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken token)
    {
        try
        {
            await _process.WaitForExitAsync(token).WaitAsync(timeout, token).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
        }

        _process.Dispose();
    }
}

internal sealed class PlaintextCloseCapture : IDisposable
{
    private readonly IDisposable _all;
    private IDisposable? _events;
    private readonly ConcurrentQueue<(string Name, int Bytes, bool Admitted, string? Reason)> _records = new();

    internal PlaintextCloseCapture()
    {
        _all = DiagnosticListener.AllListeners.Subscribe(new Observer<DiagnosticListener>(listener =>
        {
            if (listener.Name == "BoltSharp.PlaintextTerminalClose")
                _events = listener.Subscribe(new Observer<KeyValuePair<string, object?>>(pair =>
                {
                    if (pair.Key is not ("drain-end" or "disposed" or "drain-admitted" or "write-failed"))
                        return;
                    var value = pair.Value;
                    var bytes = Read<int>(value, "Bytes");
                    var admitted = Read<bool>(value, "Admitted");
                    var reason = Read<string?>(value, "Reason");
                    _records.Enqueue((pair.Key, bytes, admitted, reason));
                }));
        }));
    }

    internal int Admitted => _records.Count(r => r.Name == "drain-admitted" && r.Admitted);

    internal int Released => _records.Count(r => r.Name == "disposed");

    internal (string Name, int Bytes, string? Reason)? DrainEnd
        => _records.Where(r => r.Name == "drain-end").Select(r => ((string, int, string?)?)(r.Name, r.Bytes, r.Reason)).LastOrDefault();

    internal string[] Names => _records.Select(r => r.Name).ToArray();

    private static T Read<T>(object? value, string property)
    {
        if (value is null) return default!;
        var info = value.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return info is null ? default! : (T)(info.GetValue(value) ?? default(T)!);
    }

    public void Dispose()
    {
        _events?.Dispose();
        _all.Dispose();
    }

    private sealed class Observer<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => onNext(value);
    }
}

internal static class OperatingSystemProbe
{
    internal static string Kernel()
    {
        try { return File.ReadAllText("/proc/sys/kernel/osrelease").Trim(); }
        catch (Exception) { return Environment.OSVersion.VersionString; }
    }

    internal static bool IsWsl()
    {
        try
        {
            var version = File.ReadAllText("/proc/version");
            return version.Contains("microsoft", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static bool IsNativeLinux()
        => Environment.OSVersion.Platform == PlatformID.Unix && !IsWsl();

    internal static (long TxQueue, long RxQueue)? ProcNetTcp(int localPort, int remotePort)
    {
        foreach (var file in new[] { "/proc/net/tcp", "/proc/net/tcp6" })
        {
            try
            {
                foreach (var line in File.ReadLines(file).Skip(1))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 5) continue;
                    var local = parts[1].Split(':');
                    var remote = parts[2].Split(':');
                    if (local.Length != 2 || remote.Length != 2) continue;
                    if (!int.TryParse(local[1], System.Globalization.NumberStyles.HexNumber, null, out var lp)) continue;
                    if (!int.TryParse(remote[1], System.Globalization.NumberStyles.HexNumber, null, out var rp)) continue;
                    if (lp != localPort || rp != remotePort) continue;
                    var queue = parts[4].Split(':');
                    if (queue.Length != 2) continue;
                    if (!long.TryParse(queue[0], System.Globalization.NumberStyles.HexNumber, null, out var tx)) continue;
                    if (!long.TryParse(queue[1], System.Globalization.NumberStyles.HexNumber, null, out var rx)) continue;
                    return (tx, rx);
                }
            }
            catch (Exception)
            {
            }
        }

        return null;
    }

    internal static string SendBufferDefault()
    {
        try { return File.ReadAllText("/proc/sys/net/ipv4/tcp_wmem").Trim(); }
        catch (Exception) { return "unavailable"; }
    }
}

internal static class Account
{
    internal static Dictionary<string, object?> Snapshot(BoltTcpServer server, PlaintextCloseCapture? capture = null)
        => new()
        {
            ["accepted"] = server.Diagnostics.ConnectionsAccepted,
            ["active"] = server.Diagnostics.ConnectionsActive,
            ["completed"] = server.Diagnostics.ConnectionsCompleted,
            ["failed"] = server.Diagnostics.ConnectionsFailed,
            ["messages"] = server.Diagnostics.MessagesProcessed,
            ["tls_handshake_failures"] = server.Diagnostics.TlsHandshakeFailures,
            ["bolt_handshake_failures"] = server.Diagnostics.BoltHandshakeFailures,
            ["permits"] = capture?.Admitted ?? 0,
            ["released_permits"] = capture?.Released ?? 0,
            ["plaintext_events"] = capture?.Names ?? Array.Empty<string>()
        };
}

/// <summary>
/// R4 dispatch-coverage audit. Every registered cell and negative control maps to exactly one
/// intended implementation; each implementation reports its invocation here, and the campaign
/// end journal carries the full expected-versus-observed table. The verifier fails closed when
/// an implementation that a registered cell requires was never invoked (the R2/R3 dead-code
/// failure mode: RunCapAttemptAsync, then RunReceiptAttemptAsync).
/// </summary>
internal static class DispatchAudit
{
    internal static readonly (string Target, string[] Cells)[] Registry =
    [
        ("Production.RunTerminalAttemptAsync", ["L1", "L2"]),
        ("Production.RunReceiptAttemptAsync", ["L7"]),
        ("Production.RunPlaintextCapAttemptAsync", ["L6-PROD"]),
        ("Candidate.RunCloseNotifyAttemptAsync", ["L3", "L4"]),
        ("Candidate.RunCapAttemptAsync", ["L5", "L6"]),
        ("Controls.RunNC1CiphertextCapTamperAsync", ["NC-1"]),
        ("Controls.RunNC2DeadlineRenewalAsync", ["NC-2"])
    ];

    private static readonly ConcurrentDictionary<string, int> Counts = new();

    internal static void Invoke(string target)
        => Counts.AddOrUpdate(target, 1, (_, n) => n + 1);

    internal static int Count(string target) => Counts.TryGetValue(target, out var n) ? n : 0;

    /// <summary>The dispatch table with observed invocation counts, for the campaign journal.</summary>
    internal static Dictionary<string, object?> Snapshot(bool fullMode)
    {
        var rows = new List<Dictionary<string, object?>>();
        foreach (var (target, cells) in Registry)
        {
            var count = Count(target);
            rows.Add(new Dictionary<string, object?>
            {
                ["implementation"] = target,
                ["cells"] = cells,
                ["invocations"] = count,
                ["invoked"] = count > 0,
                ["required"] = fullMode
            });
        }
        return new Dictionary<string, object?>
        {
            ["record_type"] = "dispatch-audit",
            ["mode_requires_all"] = fullMode,
            ["implementations"] = rows,
            ["all_required_invoked"] = rows.All(r => !((bool)r["required"]!) || (bool)r["invoked"]!)
        };
    }
}

internal static class Json
{
    internal static Dictionary<string, object?> Object(params (string Key, object? Value)[] entries)
    {
        var value = new Dictionary<string, object?>(entries.Length);
        foreach (var (key, item) in entries)
            value[key] = item;
        return value;
    }

    internal static string Write(object value, bool indented = false)
        => JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = indented });

    internal static JsonNode? Read(string path) => JsonNode.Parse(File.ReadAllText(path));

    /// <summary>
    /// Convert a System.Text.Json deserialized value (JsonElement for object? fields) into a
    /// plain CLR scalar. The L7 receipt journal reader used to compare JsonElement values
    /// directly against strings/longs, which never matched.
    /// </summary>
    internal static object? Scalar(object? value) => value switch
    {
        JsonElement e when e.ValueKind == JsonValueKind.String => e.GetString(),
        JsonElement e when e.ValueKind == JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        JsonElement e when e.ValueKind == JsonValueKind.True => true,
        JsonElement e when e.ValueKind == JsonValueKind.False => false,
        JsonElement e when e.ValueKind == JsonValueKind.Null => null,
        _ => value
    };
}

internal static class RecordExtensions
{
    internal static Dictionary<string, object?> With(this Dictionary<string, object?> record,
        params (string Key, object? Value)[] entries)
    {
        foreach (var (key, value) in entries)
            record[key] = value;
        return record;
    }
}

