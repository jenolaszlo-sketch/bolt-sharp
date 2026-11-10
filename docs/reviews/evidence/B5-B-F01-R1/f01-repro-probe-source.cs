using System.Net;
using System.Net.Sockets;

using BoltSharp.Framing;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Transport;
using Neo4j.Driver;

// F01-R1 reproduction probe. Runs the same three reproductions before and after the remediation:
//   [A] official Neo4j.Driver 6.4.0 explicit-transaction BEGIN against the real transport
//   [B] raw Bolt 5.8 client BEGIN/ROUTE/COMMIT/ROLLBACK against the real transport
//   [C] direct accepted B3-B session ReceiveBytes classification for the same payloads
//   [D] raw supported scalar control

Console.WriteLine("== F01-R1 reproduction probe ==");
Console.WriteLine();

// ---------- [A] official driver BEGIN ----------
{
    Console.WriteLine("[A] official driver 6.4.0 explicit transaction (BEGIN)");
    var server = BoltTcpServer.Create(new BoltTcpServerOptions { CredentialVerifier = new ProbeVerifier() });
    await server.StartAsync();
    await using var tap = await ProbeTap.StartAsync(server.BoundEndPoint!);
    var driver = GraphDatabase.Driver(new Uri($"bolt://127.0.0.1:{tap.BoundEndPoint.Port}"), AuthTokens.Basic(Probe.User, Probe.Pass));
    try
    {
        await driver.AsyncSession().BeginTransactionAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Console.WriteLine("  driver: transaction BEGIN UNEXPECTEDLY SUCCEEDED");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  driver exception: {ex.GetType().FullName}");
        if (ex is Neo4jException nj)
        {
            Console.WriteLine($"  code={nj.Code ?? "<null>"} gql={nj.GqlStatus ?? "<null>"} retriable={nj.IsRetriable}");
        }
    }

    await driver.DisposeAsync();
    await Task.Delay(300);
    await server.StopAsync();
    Console.WriteLine($"  counters: accepted={server.Diagnostics.ConnectionsAccepted} completed={server.Diagnostics.ConnectionsCompleted} failed={server.Diagnostics.ConnectionsFailed} messages={server.Diagnostics.MessagesProcessed}");
    tap.Dump();
    Console.WriteLine();
}

// ---------- [B] raw client unsupported commands ----------
foreach (var (name, payload) in new (string Name, byte[] Payload)[]
{
    ("BEGIN", Probe.Frame(Probe.Structure(0x11, Probe.Map()))),
    ("COMMIT", Probe.Frame(Probe.Structure(0x12, Probe.Map()))),
    ("ROLLBACK", Probe.Frame(Probe.Structure(0x13, Probe.Map()))),
    ("ROUTE", Probe.Frame(Probe.Structure(0x66, Probe.Map(("address", Probe.Str("127.0.0.1:1"))), Probe.List(), Probe.Map()))),
})
{
    Console.WriteLine($"[B] raw {name}");
    var server = BoltTcpServer.Create(new BoltTcpServerOptions { CredentialVerifier = new ProbeVerifier() });
    await server.StartAsync();
    using (var client = RawClient.Connect(server.BoundEndPoint!))
    {
        client.Handshake58();
        client.Send(Probe.Frame(Probe.HelloStructure()));
        client.ExpectResponse("HELLO");
        client.Send(Probe.Frame(Probe.LogonStructure()));
        client.ExpectResponse("LOGON");
        client.Send(payload);
        var response = client.ReadResponseOrClose(3000, out var closeInfo);
        Console.WriteLine($"  response: {response ?? $"<none: {closeInfo}>"}");
    }

    await Task.Delay(200);
    await server.StopAsync();
    Console.WriteLine($"  counters: accepted={server.Diagnostics.ConnectionsAccepted} completed={server.Diagnostics.ConnectionsCompleted} failed={server.Diagnostics.ConnectionsFailed} messages={server.Diagnostics.MessagesProcessed}");
    Console.WriteLine();
}

// ---------- [C] direct accepted B3-B ReceiveBytes ----------
{
    Console.WriteLine("[C] direct B3-B BoltConnectionSession.ReceiveBytes");
    foreach (var (name, payload) in new (string Name, byte[] Payload)[]
    {
        ("BEGIN", Probe.Structure(0x11, Probe.Map())),
        ("COMMIT", Probe.Structure(0x12, Probe.Map())),
        ("ROLLBACK", Probe.Structure(0x13, Probe.Map())),
        ("ROUTE", Probe.Structure(0x66, Probe.Map(("address", Probe.Str("127.0.0.1:1"))), Probe.List(), Probe.Map())),
        ("MALFORMED-RUN-ARITY", new byte[] { 0xB0, 0x10 }),
        ("UNKNOWN-SIGNATURE", Probe.Structure(0x7B, Probe.Map())),
    })
    {
        var provider = new CountingProvider();
        var session = new BoltConnectionSession(provider, new BoltSessionOptions { Verifier = new ProbeVerifier() });
        var codec = new BoltMessageCodec();
        var result = session.ReceiveBytes(payload);
        var summary = string.Join(", ", result.Responses.Select(r => Probe.Describe(codec, r)));
        Console.WriteLine($"  {name,-20} -> state={result.State} shouldClose={result.ShouldClose} prepareCount={provider.PrepareCount} responses=[{summary}]");
    }

    Console.WriteLine();
}

// ---------- [D] raw supported scalar control ----------
{
    Console.WriteLine("[D] raw supported scalar control (RUN 42 / PULL)");
    var server = BoltTcpServer.Create(new BoltTcpServerOptions { CredentialVerifier = new ProbeVerifier() });
    await server.StartAsync();
    using (var client = RawClient.Connect(server.BoundEndPoint!))
    {
        client.Handshake58();
        client.Send(Probe.Frame(Probe.HelloStructure()));
        client.ExpectResponse("HELLO");
        client.Send(Probe.Frame(Probe.LogonStructure()));
        client.ExpectResponse("LOGON");
        client.Send(Probe.Frame(Probe.RunStructure("RETURN 42 AS answer")));
        Console.WriteLine($"  RUN  -> {client.ExpectResponse("RUN")}");
        client.Send(Probe.Frame(Probe.Structure(0x3F, Probe.Map(("n", Probe.Int(1000))))));
        Console.WriteLine($"  PULL -> {client.ExpectResponse("RECORD")}");
        Console.WriteLine($"       -> {client.ExpectResponse("PULL-terminal")}");
    }

    await Task.Delay(200);
    await server.StopAsync();
    Console.WriteLine($"  counters: accepted={server.Diagnostics.ConnectionsAccepted} completed={server.Diagnostics.ConnectionsCompleted} failed={server.Diagnostics.ConnectionsFailed} messages={server.Diagnostics.MessagesProcessed}");
}

internal static class Probe
{
    public const string User = "f01r1-probe-user";
    public const string Pass = "f01r1-probe-secret";

    public static string Describe(BoltMessageCodec codec, BoltResponse response)
    {
        var bytes = codec.EncodeResponse(response);
        var text = response switch
        {
            SuccessResponse s => "SUCCESS{" + string.Join(",", s.Metadata.Entries.Select(e => e.Key + "=" + (e.Value as PackStreamString)?.Value)) + "}",
            FailureResponse f => "FAILURE{" + string.Join(",", f.Metadata.Entries.Select(e => e.Key + "=" + (e.Value as PackStreamString)?.Value)) + "}",
            IgnoredResponse => "IGNORED",
            RecordResponse r => "RECORD[" + r.Data.Items.Count + "]",
            _ => response.GetType().Name,
        };
        return $"{text} hex={Convert.ToHexString(bytes)}";
    }

    public static PackStreamMap Map(params (string Key, PackStreamValue? Value)[] entries) =>
        new(entries.Select(e => new KeyValuePair<string, PackStreamValue?>(e.Key, e.Value)).ToArray());

    public static PackStreamString Str(string s) => new(s);

    public static PackStreamInteger Int(long v) => new(v);

    public static PackStreamList List() => new(Array.Empty<PackStreamValue?>());

    public static byte[] Structure(byte signature, params PackStreamValue?[] fields)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        writer.WriteStructure(signature, fields);
        return buffer.WrittenSpan.ToArray();
    }

    public static byte[] HelloStructure() => Structure(
        0x01,
        Map(("user_agent", Str("f01r1-probe/1.0")), ("bolt_agent", Map(("product", Str("f01r1-probe/1.0"))))));

    public static byte[] LogonStructure() => Structure(
        0x6A,
        Map(("scheme", Str("basic")), ("principal", Str(User)), ("credentials", Str(Pass))));

    public static byte[] RunStructure(string query) => Structure(0x10, Str(query), Map(), Map());

    public static byte[] Frame(params byte[][] chunks)
    {
        var bytes = new List<byte>();
        foreach (var chunk in chunks)
        {
            bytes.Add((byte)(chunk.Length >> 8));
            bytes.Add((byte)(chunk.Length & 0xFF));
            bytes.AddRange(chunk);
        }

        bytes.Add(0);
        bytes.Add(0);
        return bytes.ToArray();
    }
}

internal sealed class ProbeVerifier : IBoltCredentialVerifier
{
    public bool Verify(LogonRequest logon)
    {
        string? scheme = null, principal = null, credentials = null;
        foreach (var entry in logon.Auth.Entries)
        {
            if (entry.Value is PackStreamString text)
            {
                switch (entry.Key)
                {
                    case "scheme": scheme = text.Value; break;
                    case "principal": principal = text.Value; break;
                    case "credentials": credentials = text.Value; break;
                }
            }
        }

        return scheme == "basic" && principal == Probe.User && credentials == Probe.Pass;
    }
}

internal sealed class CountingProvider : IBoltOperationProvider
{
    public int PrepareCount { get; private set; }

    public IBoltOperation? Prepare(RunRequest run)
    {
        PrepareCount++;
        return null;
    }
}

internal sealed class RawClient : IDisposable
{
    private readonly TcpClient _tcp = new();
    private readonly BoltMessageCodec _codec = new();
    private NetworkStream _stream = null!;
    private BoltMessageReader? _reader;

    public static RawClient Connect(IPEndPoint endpoint)
    {
        var client = new RawClient();
        client._tcp.Connect(endpoint.Address, endpoint.Port);
        client._stream = client._tcp.GetStream();
        return client;
    }

    public void Handshake58()
    {
        var bytes = new byte[] { 0x60, 0x60, 0xB0, 0x17, 0, 0, 8, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        _stream.Write(bytes);
        var reply = ReadExactly(4);
        Console.WriteLine($"  handshake reply: {Convert.ToHexString(reply)}");
        _reader = new BoltMessageReader(_stream, BoltFramingLimits.Default);
    }

    public void Send(byte[] framed) => _stream.Write(framed);

    public byte[]? ReadResponseRaw(int timeoutMs)
    {
        _tcp.ReceiveTimeout = timeoutMs;
        try
        {
            return _reader!.ReadMessage();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string ExpectResponse(string label)
    {
        var message = ReadResponseRaw(3000);
        if (message is null)
        {
            return "<no response>";
        }

        var response = _codec.DecodeResponse(message);
        return Probe.Describe(_codec, response);
    }

    public string? ReadResponseOrClose(int timeoutMs, out string closeInfo)
    {
        var message = ReadResponseRaw(timeoutMs);
        if (message is not null)
        {
            closeInfo = "response received";
            return Probe.Describe(_codec, _codec.DecodeResponse(message));
        }

        closeInfo = WaitForClose(timeoutMs);
        return null;
    }

    public string WaitForClose(int timeoutMs)
    {
        try
        {
            if (!_tcp.Client.Poll(timeoutMs * 1000, SelectMode.SelectRead))
            {
                return "open";
            }

            var buffer = new byte[1];
            return _stream.Read(buffer, 0, 1) == 0 ? "closed-by-server" : "data";
        }
        catch (Exception)
        {
            return "closed-by-server";
        }
    }

    private byte[] ReadExactly(int count)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = _stream.Read(buffer, read, count - read);
            if (n == 0)
            {
                throw new EndOfStreamException();
            }

            read += n;
        }

        return buffer;
    }

    public void Dispose()
    {
        _reader = null;
        _stream.Dispose();
        _tcp.Dispose();
    }
}

internal sealed class ProbeTap : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly IPEndPoint _target;
    private readonly List<string> _lines = new();
    private readonly CancellationTokenSource _cts = new();
    private int _connection;

    private ProbeTap(TcpListener listener, IPEndPoint target)
    {
        _listener = listener;
        _target = target;
        BoundEndPoint = (IPEndPoint)listener.LocalEndpoint;
    }

    public IPEndPoint BoundEndPoint { get; }

    public static Task<ProbeTap> StartAsync(IPEndPoint target)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var tap = new ProbeTap(listener, target);
        _ = Task.Run(() => tap.AcceptLoopAsync());
        return Task.FromResult(tap);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch
            {
                return;
            }

            var id = Interlocked.Increment(ref _connection);
            var upstream = new TcpClient();
            await upstream.ConnectAsync(_target.Address, _target.Port, _cts.Token);
            _ = Task.Run(() => Pump(client.GetStream(), upstream.GetStream(), $"conn{id} C2S"));
            _ = Task.Run(() => Pump(upstream.GetStream(), client.GetStream(), $"conn{id} S2C"));
        }
    }

    private async Task Pump(NetworkStream from, NetworkStream to, string tag)
    {
        var buffer = new byte[65536];
        try
        {
            while (true)
            {
                var read = await from.ReadAsync(buffer, _cts.Token);
                if (read == 0)
                {
                    lock (_lines)
                    {
                        _lines.Add($"{tag} EOF");
                    }

                    break;
                }

                lock (_lines)
                {
                    _lines.Add($"{tag} {Convert.ToHexString(buffer, 0, read)}");
                }

                await to.WriteAsync(buffer.AsMemory(0, read), _cts.Token);
            }
        }
        catch (Exception ex)
        {
            lock (_lines)
            {
                _lines.Add($"{tag} {ex.GetType().Name}");
            }
        }

        try { to.Close(); } catch { }
        try { from.Close(); } catch { }
    }

    public void Dump()
    {
        lock (_lines)
        {
            foreach (var line in _lines)
            {
                // Redact the LOGON payload inside any chunk that contains it (credentials).
                var marker = line.IndexOf("B16A", StringComparison.Ordinal);
                var rendered = marker >= 0 ? line[..marker] + "B16A<redacted>" : line;
                Console.WriteLine($"    {rendered}");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        await Task.Delay(100);
        _cts.Dispose();
    }
}

