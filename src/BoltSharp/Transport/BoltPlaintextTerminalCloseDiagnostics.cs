using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace BoltSharp.Transport;

internal static class BoltPlaintextTerminalCloseDiagnostics
{
    private static readonly DiagnosticListener Listener = new("BoltSharp.PlaintextTerminalClose");

    // DiagnosticSource.Write<T> is annotated RequiresUnreferencedCode because it reflects over
    // the payload's properties to build the observer payload. The TerminalEvent payload below is
    // preserved explicitly via [DynamicDependency], and diagnostic delivery is exercised by the
    // "Diagnostic Event Delivery" case of tests/BoltSharp.AotHarness under a published Native AOT
    // executable. The suppression is scoped to this one method and justified; it is not global.
    // The warning concerns diagnostic observability only, never protocol or transport behaviour.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "DiagnosticListener.Write<T> is RequiresUnreferencedCode; TerminalEvent is " +
            "preserved via [DynamicDependency] and Native AOT delivery is verified by the AOT harness.")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(TerminalEvent))]
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
