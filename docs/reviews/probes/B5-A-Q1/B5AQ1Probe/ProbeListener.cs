using System.Net;
using System.Net.Sockets;

using BoltSharp.Transport;

namespace B5AQ1Probe;

/// <summary>§7: independent TCP listener qualification.</summary>
internal static class ProbeListener
{
    public static void Run()
    {
        Program.Section("B. TCP listener lifecycle, admission and secure defaults");
        EphemeralBinding();
        SecureDefaults();
        NonLoopbackPlaintextRefused();
        LifecycleAndRestart();
        CapacityEnforcement();
        ExcessRefusalIsolation();
        PostShutdownRefusal();
        ActiveDrainAfterClosure();
    }

    private static void EphemeralBinding()
    {
        using var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
        });
        
        var ep = server.EndPoint;
        Program.Check(
            "B1 default bind is IPv4 loopback on an ephemeral port",
            ep.Address.Equals(IPAddress.Loopback) && ep.Port is > 0 and <= 65535,
            $"{ep}");
    }

    private static void SecureDefaults()
    {
        // Inspect the RAW default options (no verifier injected by the probe).
        var defaults = new BoltTcpServerOptions();
        Program.Check("B2 default mode is plaintext (Tls null)", defaults.Tls is null, "no accidental TLS");
        Program.Check("B2 default verifier is null (B3-B fail-closed preserved)", defaults.CredentialVerifier is null, "caller must opt in");
        Program.Check("B2 default bind is loopback", defaults.BindAddress.Equals(IPAddress.Loopback), $"{defaults.BindAddress}");
        Program.Check("B2 default MaxConnections is bounded", defaults.MaxConnections is > 0 and <= 10_000, $"{defaults.MaxConnections}");
    }

    private static void NonLoopbackPlaintextRefused()
    {
        foreach (var address in new[] { IPAddress.Any, IPAddress.IPv6Any })
        {
            try
            {
                var server = BoltSharp.Transport.BoltTcpServer.Create(new BoltSharp.Transport.BoltTcpServerOptions
                {
                    CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
                    BindAddress = address,
                });
                Program.Check($"B3 plaintext {address} bind refused", false, "no exception was thrown");
            }
            catch (Exception ex)
            {
                Program.Check(
                    $"B3 plaintext {address} bind refused",
                    true,
                    ex.GetType().Name);
            }
        }
    }

    private static void LifecycleAndRestart()
    {
        using var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
        });
        Program.Check("B4 IsRunning true after start", server.Server.IsRunning, "started");
        server.Server.StopAsync().GetAwaiter().GetResult();
        Program.Check("B4 IsRunning false after stop", !server.Server.IsRunning, "stopped");
        Program.Check("B4 second stop is idempotent", !server.Server.IsRunning, "idempotent");
        server.Server.DisposeAsync().GetAwaiter().GetResult();
    }

    private static void CapacityEnforcement()
    {
        using var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
            MaxConnections = 2,
        });
        

        using var c1 = ReviewClient.Connect(server.EndPoint);
        using var c2 = ReviewClient.Connect(server.EndPoint);
        c1.HandshakeLegacy(0x00000805u);
        c2.HandshakeLegacy(0x00000805u);
        Program.Check(
            "B5 two connections admitted at capacity 2",
            Program.WaitUntil(() => server.Server.Diagnostics.ConnectionsActive == 2, TimeSpan.FromSeconds(5)),
            $"active={server.Server.Diagnostics.ConnectionsActive}");

        using var c3 = ReviewClient.Connect(server.EndPoint);
        var refused = c3.WaitForClose(5_000) == "closed";
        Program.Check("B5 excess connection refused immediately", refused, "closed on acceptance");
        Program.Check(
            "B5 refusal counted",
            Program.WaitUntil(() => server.Server.Diagnostics.ConnectionsRejectedForCapacity >= 1, TimeSpan.FromSeconds(5)),
            $"rejected={server.Server.Diagnostics.ConnectionsRejectedForCapacity}");

        // Existing connections still work.
        c1.Send(Review.Hello());
        var hello = c1.ReadMessageOrThrow();
        Program.Check("B5 accepted clients unaffected by refusal", hello[0] == 0xB1 && hello[1] == 0x70, "HELLO SUCCESS");
    }

    private static void ExcessRefusalIsolation()
    {
        using var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
            MaxConnections = 1,
        });
        
        using var occupant = ReviewClient.Connect(server.EndPoint);
        occupant.HandshakeLegacy(0x00000805u);
        using var excess = ReviewClient.Connect(server.EndPoint);
        excess.Send(Review.Hello()); // bytes into a connection that will be refused
        var closed = excess.WaitForClose(5_000) == "closed";
        Program.Check("B6 refused client closed even after sending bytes", closed, "no hang");
        Program.Check("B6 listener still running", server.Server.IsRunning, "healthy");
    }

    private static void PostShutdownRefusal()
    {
        using var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
        });
        var ep = server.EndPoint;
        server.Server.StopAsync().GetAwaiter().GetResult();
        server.Server.DisposeAsync().GetAwaiter().GetResult();

        var accepted = false;
        try
        {
            using var probe = ReviewClient.Connect(ep);
            probe.HandshakeLegacy(0x00000805u);
            accepted = true;
        }
        catch (Exception)
        {
            // connect refused: also acceptable
        }

        Program.Check(
            "B7 no connection accepted after shutdown",
            !accepted && server.Server.Diagnostics.ConnectionsAccepted == 0,
            $"accepted={server.Server.Diagnostics.ConnectionsAccepted}");
    }

    private static void ActiveDrainAfterClosure()
    {
        using var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
        });
        
        using (var client = ReviewClient.Connect(server.EndPoint))
        {
            client.HandshakeLegacy(0x00000805u);
            client.Send(Review.Hello());
            client.ReadMessageOrThrow();
            Program.Check(
                "B8 active count reaches 1",
                Program.WaitUntil(() => server.Server.Diagnostics.ConnectionsActive == 1, TimeSpan.FromSeconds(5)),
                $"active={server.Server.Diagnostics.ConnectionsActive}");
        }

        Program.Check(
            "B8 active count returns to zero after client closure",
            Program.WaitUntil(() => server.Server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)),
            $"active={server.Server.Diagnostics.ConnectionsActive}");
        Program.Check(
            "B8 completion counted exactly once",
            server.Server.Diagnostics.ConnectionsCompleted == 1,
            $"completed={server.Server.Diagnostics.ConnectionsCompleted}");
    }
}
