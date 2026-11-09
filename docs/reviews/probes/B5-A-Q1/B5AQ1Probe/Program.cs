using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using BoltSharp.Handshake;
using BoltSharp.PackStream;
using BoltSharp.Messages;
using BoltSharp.Sessions;
using BoltSharp.Transport;

namespace B5AQ1Probe;

/// <summary>
/// B5-A-Q1 independent reviewer probe: reviewer-controlled loopback clients with own
/// PackStream encoding and own chunked-framing reader, so no oracle derives from the code
/// under review. Each subcommand exercises one qualification area and prints RESULT lines.
/// </summary>
internal static class Program
{
    public static int Failures;
    public static int Inconclusives;
    public static readonly List<string> Results = new();
    public static readonly List<string> Trace = new();

    public static void Section(string name)
    {
        var line = $"SECTION | {name}";
        Results.Add(line);
        Console.WriteLine($"== {name} ==");
    }

    public static void Check(string name, bool pass, string detail = "")
    {
        var kind = pass ? "PASS" : "FAIL";
        if (!pass) { Interlocked.Increment(ref Failures); }
        var line = $"RESULT | {name} | {kind} | {detail}";
        Results.Add(line);
        Console.WriteLine(line);
    }

    public static void Inconclusive(string name, string detail)
    {
        Interlocked.Increment(ref Inconclusives);
        var line = $"RESULT | {name} | INCONCLUSIVE | {detail}";
        Results.Add(line);
        Console.WriteLine(line);
    }

    public static void TraceLine(string detail)
    {
        Trace.Add(detail);
        Console.WriteLine("  " + detail);
    }

    public static int Main(string[] args)
    {
        var command = args.Length > 0 ? args[0] : "help";
        switch (command)
        {
            case "listener": ProbeListener.Run(); break;
            case "manifest": ProbeManifest.Run(); break;
            case "tls": ProbeTls.Run(); break;
            case "backpressure": ProbeBackpressure.Run(); break;
            case "shutdown": ProbeShutdown.Run(); break;
            case "protocol": ProbeProtocol.Run(); break;
            case "negative": ProbeNegative.Run(); break;
            case "all":
                ProbeListener.Run();
                ProbeManifest.Run();
                ProbeTls.Run();
                ProbeBackpressure.Run();
                ProbeShutdown.Run();
                ProbeProtocol.Run();
                ProbeNegative.Run();
                break;
            default:
                Console.WriteLine("usage: B5AQ1Probe <listener|manifest|tls|backpressure|shutdown|protocol|negative|all>");
                return 2;
        }

        Console.WriteLine();
        Console.WriteLine($"SUMMARY | failures={Failures} inconclusive={Inconclusives}");
        return Failures == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ shared helpers

    /// <summary>Sync-disposable wrapper for probe convenience (StopAsync + DisposeAsync).</summary>
    internal sealed class ProbeServer : IDisposable
    {
        public ProbeServer(BoltTcpServer server)
        {
            Server = server;
            EndPoint = server.BoundEndPoint!;
        }

        public BoltTcpServer Server { get; }
        public IPEndPoint EndPoint { get; }

        public void Dispose()
        {
            Server.StopAsync().GetAwaiter().GetResult();
            Server.DisposeAsync().GetAwaiter().GetResult();
            GC.SuppressFinalize(this);
        }
    }

    internal static ProbeServer StartServerWith(Func<BoltTcpServerOptions> factory)
    {
        var server = BoltTcpServer.Create(factory());
        server.StartAsync().GetAwaiter().GetResult();
        return new ProbeServer(server);
    }

    internal static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) { return true; }
            Thread.Sleep(10);
        }

        return condition();
    }

    internal static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

    /// <summary>Deterministic reviewer verifier: accepts the basic scheme with fixed sentinels.</summary>
    internal sealed class ReviewVerifier : IBoltCredentialVerifier
    {
        private readonly bool _throwOnCall;
        public int Calls;
        public string LastPrincipal = string.Empty;

        private ReviewVerifier(bool throwOnCall) => _throwOnCall = throwOnCall;

        public static ReviewVerifier AcceptBasic() => new(false);
        public static ReviewVerifier Thrower() => new(true);

        public bool Verify(LogonRequest logon)
        {
            Interlocked.Increment(ref Calls);
            if (_throwOnCall)
            {
                throw new InvalidOperationException("reviewer verifier exploded");
            }

            foreach (var entry in logon.Auth.Entries)
            {
                if (entry.Key == "principal" && entry.Value is PackStreamString s)
                {
                    LastPrincipal = s.Value;
                }
            }

            return logon.RecognizedScheme == BoltAuthScheme.Basic;
        }
    }
}
