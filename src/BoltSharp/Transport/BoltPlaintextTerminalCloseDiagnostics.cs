using System.Diagnostics;

namespace BoltSharp.Transport;

internal static class BoltPlaintextTerminalCloseDiagnostics
{
    private static readonly DiagnosticListener Listener = new("BoltSharp.PlaintextTerminalClose");

    internal static void Report(
        string connectionId,
        string name,
        string? phase = null,
        string? reason = null,
        int bytes = 0,
        bool admitted = false,
        double elapsedMilliseconds = 0)
    {
        try
        {
            if (Listener.IsEnabled(name))
            {
                Listener.Write(name, new TerminalEvent(connectionId, phase, reason, bytes, admitted, elapsedMilliseconds));
            }
        }
        catch (Exception)
        {
            // Observer failures cannot change connection ownership or protocol behavior.
        }
    }

    private sealed record TerminalEvent(string ConnectionId, string? Phase, string? Reason,
        int Bytes, bool Admitted, double ElapsedMilliseconds);
}
