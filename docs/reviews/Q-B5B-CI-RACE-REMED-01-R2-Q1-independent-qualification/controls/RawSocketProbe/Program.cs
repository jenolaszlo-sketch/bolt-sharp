using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

internal static class Program
{
    private static async Task Main()
    {
        await Trial("cancel-only", closeListener: false, cancel: true);
        await Trial("close-only", closeListener: true, cancel: false);
        await Trial("cancel+close", closeListener: true, cancel: true);
    }

    private static async Task Trial(string label, bool closeListener, bool cancel)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(128);
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var cts = new CancellationTokenSource();
        var acceptTask = AcceptAsync(listener, cts.Token);

        using var client = new TcpClient();
        client.Connect(endpoint.Address, endpoint.Port);

        if (cancel) cts.Cancel();
        if (closeListener) listener.Stop();

        string accept;
        try
        {
            var accepted = await acceptTask.WaitAsync(TimeSpan.FromSeconds(2));
            accept = accepted is null ? "null" : "socket";
            accepted?.Dispose();
        }
        catch (TimeoutException)
        {
            accept = "HUNG";
        }
        catch (Exception ex)
        {
            accept = ex.GetType().Name;
        }

        Thread.Sleep(250);
        var clientLocal = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
        var connections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
            .Where(c => c.LocalEndPoint.Port == endpoint.Port || c.RemoteEndPoint.Port == clientLocal)
            .Select(c => $"{c.LocalEndPoint}->{c.RemoteEndPoint}:{c.State}")
            .ToArray();

        var readable = client.Client.Poll(500_000, SelectMode.SelectRead);
        var verdict = readable ? "closed" : "OPEN";
        Console.WriteLine($"{label,-14} accept={accept,-24} peer={verdict,-7} serverSide=[{string.Join(", ", connections)}]");
        cts.Dispose();
    }

    private static async Task<TcpClient?> AcceptAsync(TcpListener listener, CancellationToken token)
    {
        var socket = await listener.Server.AcceptAsync(token).ConfigureAwait(false);
        return new TcpClient { Client = socket };
    }
}
