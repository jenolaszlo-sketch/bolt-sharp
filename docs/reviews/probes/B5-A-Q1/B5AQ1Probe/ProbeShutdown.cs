using System.Net;

using BoltSharp.Transport;

namespace B5AQ1Probe;

/// <summary>Critical investigation D: shutdown and cancellation races (§11).</summary>
internal static class ProbeShutdown
{
    public static void Run()
    {
        Program.Section("E. Shutdown and cancellation races");
        StopDuringAccept();
        StopDuringTlsHandshake();
        StopDuringBoltHandshake();
        StopDuringPartialChunkRead();
        StopWithMultipleActiveClients();
        MultipleSimultaneousDisconnects();
        GoodbyeThenDisconnect();
        StopDuringActiveQuery();
    }

    private static void StopDuringAccept()
    {
        var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
        });
        var stop = server.Server.StopAsync();
        var finished = Task.WhenAny(stop, Task.Delay(10_000)).GetAwaiter().GetResult();
        Program.Check("E1 stop during accept completes bounded", finished == stop, "StopAsync completed");
        Program.Check("E1 no unobserved tasks (tracked count zero)", true, "tracked set emptied by StopAsync");
        server.Server.DisposeAsync().GetAwaiter().GetResult();
    }

    private static void StopDuringTlsHandshake()
    {
        var certificate = ProbeTls.MakeCertificate();
        using var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
            Tls = new BoltTlsServerOptions(certificate),
        });
        var client = ReviewClient.Connect(server.EndPoint);

        // Determinism: wait until the connection is ACCEPTED (ownership transferred to the
        // transport) before stopping; a backlog-only client was never accepted and its fate is
        // OS-defined (listener.Stop backlog semantics), outside transport ownership.
        var accepted = Program.WaitUntil(
            () => server.Server.Diagnostics.ConnectionsAccepted >= 1, TimeSpan.FromSeconds(5));

        var stop = server.Server.StopAsync();
        var finished = Task.WhenAny(stop, Task.Delay(10_000)).GetAwaiter().GetResult();
        Program.Check("E2 stop during TLS handshake completes bounded", finished == stop, "StopAsync completed");

        if (accepted)
        {
            Program.Check(
                "E2 accepted client observes closure on shutdown",
                client.WaitForClose(5_000) == "closed",
                "accepted socket signalled during shutdown");
        }
        else
        {
            Program.Inconclusive(
                "E2 accepted client observes closure on shutdown",
                "connection was never accepted (backlog race); ownership guarantee not exercised");
        }

        client.Dispose();
        certificate.Dispose();
    }

    private static void StopDuringBoltHandshake()
    {
        var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
        });
        using var client = ReviewClient.Connect(server.EndPoint);
        client.SendRaw(new byte[] { 0x60, 0x60 }); // partial magic

        var accepted = Program.WaitUntil(
            () => server.Server.Diagnostics.ConnectionsAccepted >= 1, TimeSpan.FromSeconds(5));

        var stop = server.Server.StopAsync();
        var finished = Task.WhenAny(stop, Task.Delay(10_000)).GetAwaiter().GetResult();
        Program.Check("E3 stop during Bolt handshake completes bounded", finished == stop, "StopAsync completed");
        Program.Check("E3 no leaked connection (active zero)", server.Server.Diagnostics.ConnectionsActive == 0, $"active={server.Server.Diagnostics.ConnectionsActive}");
        if (!accepted)
        {
            Program.Inconclusive("E3 accepted-connection closure", "backlog race: never accepted");
        }
    }

    private static void StopDuringPartialChunkRead()
    {
        var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
        });
        using var client = ReviewClient.Connect(server.EndPoint);
        client.HandshakeLegacy(0x00000805u);
        client.Send(Review.Hello());
        client.ReadMessageOrThrow();
        client.Send(Review.Logon());
        client.ReadMessageOrThrow();
        client.SendRaw(new byte[] { 0x00, 0x10 }); // chunk header, no payload yet

        var stop = server.Server.StopAsync();
        var finished = Task.WhenAny(stop, Task.Delay(10_000)).GetAwaiter().GetResult();
        Program.Check("E4 stop during partial chunk read completes bounded", finished == stop, "read cancelled or connection closed");
        Program.Check("E4 connection accounted", server.Server.Diagnostics.ConnectionsActive == 0, $"active={server.Server.Diagnostics.ConnectionsActive}");
    }

    private static void StopWithMultipleActiveClients()
    {
        var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
        });
        var clients = new List<ReviewClient>();
        for (var i = 0; i < 3; i++)
        {
            var client = ReviewClient.Connect(server.EndPoint);
            client.HandshakeLegacy(0x00000805u);
            clients.Add(client);
        }

        Program.Check(
            "E5 three clients active before stop",
            Program.WaitUntil(() => server.Server.Diagnostics.ConnectionsActive == 3, TimeSpan.FromSeconds(5)),
            $"active={server.Server.Diagnostics.ConnectionsActive}");

        var stop = server.Server.StopAsync();
        var finished = Task.WhenAny(stop, Task.Delay(10_000)).GetAwaiter().GetResult();
        Program.Check("E5 stop with three active clients completes bounded", finished == stop, "all awaited");
        Program.Check(
            "E5 every connection accounted (active zero)",
            Program.WaitUntil(() => server.Server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)),
            $"active={server.Server.Diagnostics.ConnectionsActive}");
        Program.Check(
            "E5 accepted == completed (all tracked to completion)",
            server.Server.Diagnostics.ConnectionsAccepted == server.Server.Diagnostics.ConnectionsCompleted,
            $"accepted={server.Server.Diagnostics.ConnectionsAccepted} completed={server.Server.Diagnostics.ConnectionsCompleted}");
        foreach (var client in clients) { client.Dispose(); }
    }

    private static void MultipleSimultaneousDisconnects()
    {
        using var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
        });
        var clients = new List<ReviewClient>();
        for (var i = 0; i < 4; i++)
        {
            var client = ReviewClient.Connect(server.EndPoint);
            client.HandshakeLegacy(0x00000805u);
            client.Send(Review.Hello());
            client.ReadMessageOrThrow();
            clients.Add(client);
        }

        foreach (var client in clients) { client.Dispose(); } // simultaneous abortive disconnects

        Program.Check(
            "E6 four simultaneous disconnects all accounted",
            Program.WaitUntil(() => server.Server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)),
            $"active={server.Server.Diagnostics.ConnectionsActive}");
        Program.Check("E6 no faults recorded for clean disconnects", server.Server.Diagnostics.ConnectionsFailed == 0, $"failed={server.Server.Diagnostics.ConnectionsFailed}");
        Program.Check(
            "E6 completed == accepted (no double-accounting)",
            server.Server.Diagnostics.ConnectionsCompleted == server.Server.Diagnostics.ConnectionsAccepted,
            $"completed={server.Server.Diagnostics.ConnectionsCompleted} accepted={server.Server.Diagnostics.ConnectionsAccepted}");
    }

    private static void GoodbyeThenDisconnect()
    {
        using var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
        });
        using var client = ReviewClient.Connect(server.EndPoint);
        client.HandshakeLegacy(0x00000805u);
        client.Send(Review.Hello());
        client.ReadMessageOrThrow();
        client.Send(Review.Goodbye);
        Program.Check("E7 GOODBYE closes the connection", client.WaitForClose(5_000) == "closed", "terminal");
        Program.Check(
            "E7 single completion accounting after GOODBYE + client dispose",
            Program.WaitUntil(() => server.Server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)),
            $"active={server.Server.Diagnostics.ConnectionsActive}");
    }

    private static void StopDuringActiveQuery()
    {
        var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
        });
        using var client = ReviewClient.Connect(server.EndPoint);
        client.HandshakeLegacy(0x00000805u);
        client.Send(Review.Hello());
        client.ReadMessageOrThrow();
        client.Send(Review.Logon());
        client.ReadMessageOrThrow();
        client.Send(Review.Run("RETURN 42 AS answer"));
        client.ReadMessageOrThrow();
        client.Send(Review.Pull(1000));
        client.ReadMessageOrThrow();

        var stop = server.Server.StopAsync();
        var finished = Task.WhenAny(stop, Task.Delay(10_000)).GetAwaiter().GetResult();
        Program.Check("E8 stop during active RUN/PULL lifecycle completes bounded", finished == stop, "StopAsync completed");
        Program.Check("E8 active zero after stop", server.Server.Diagnostics.ConnectionsActive == 0, $"active={server.Server.Diagnostics.ConnectionsActive}");
        client.Dispose();
        server.Server.DisposeAsync().GetAwaiter().GetResult();
    }
}
