// B5-B-F01-R1-Q1 reviewer probe — server host helper and evidence writer.
//
// Evidence is written to docs/reviews/evidence/B5-B-F01-R1-Q1/. All captured output is
// credential-free: LOGON payloads are never written (see RecordingProxy redaction) and no
// verifier state contains secrets.
using System.Net;
using System.Text;

using BoltSharp.Framing;
using BoltSharp.Handshake;
using BoltSharp.Sessions;
using BoltSharp.Transport;

namespace BoltSharp.B5BF01R1Q1.Probes.Infra;

public static class ProbeHost
{
    public static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BoltSharp.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("repository root not found");
        }

        return dir.FullName;
    }

    public static string EvidenceDirectory() =>
        Path.Combine(RepositoryRoot(), "docs", "reviews", "evidence", "B5-B-F01-R1-Q1");

    public static async Task<BoltTcpServer> StartServerAsync(IBoltCredentialVerifier? verifier = null, Func<IBoltOperationProvider>? providerFactory = null, BoltFramingLimits? framing = null)
    {
        var server = BoltTcpServer.Create(new BoltTcpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            HandshakeOptions = BoltHandshakeOptions.Create(new[] { new BoltProtocolVersion(5, 8) }),
            CredentialVerifier = verifier,
            OperationProviderFactory = providerFactory ?? (() => new ProbeProvider()),
            FramingLimits = framing ?? BoltFramingLimits.Default,
        });
        await server.StartAsync();
        return server;
    }

    /// <summary>Opens a connection, performs the reviewer handshake, HELLO and LOGON, asserting
    /// the SUCCESS responses, and returns the connected client.</summary>
    public static async Task<ReviewerBoltClient> ConnectAndAuthorizeAsync(BoltTcpServer server)
    {
        var client = new ReviewerBoltClient();
        await client.ConnectAsync((IPEndPoint)server.BoundEndPoint!);
        var reply = await client.HandshakeAsync();
        if (reply != "00000805")
        {
            throw new InvalidOperationException($"handshake reply {reply} did not select 5.8");
        }

        client.SendFramed(ProbeMessages.Hello());
        var hello = client.ReadMessage();
        if (hello is null || hello[1] != 0x70)
        {
            throw new InvalidOperationException("HELLO was not answered with SUCCESS");
        }

        client.SendFramed(ProbeMessages.Logon());
        var logon = client.ReadMessage();
        if (logon is null || logon[1] != 0x70)
        {
            throw new InvalidOperationException("LOGON was not answered with SUCCESS");
        }

        return client;
    }

    public static string Hex(byte[]? bytes) => bytes is null ? "<null>" : Convert.ToHexString(bytes);

    public static void WriteEvidence(string fileName, string content)
    {
        var dir = EvidenceDirectory();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), content, new UTF8Encoding(false));
    }

    public static void AppendEvidence(string fileName, string content)
    {
        var dir = EvidenceDirectory();
        Directory.CreateDirectory(dir);
        File.AppendAllText(Path.Combine(dir, fileName), content, new UTF8Encoding(false));
    }
}
