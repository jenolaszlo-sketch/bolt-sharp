using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using BoltSharp.Transport;

namespace R1Harness;

using static R1Harness.RecordExtensions;

internal static class Controls
{
    internal static async Task RunNegativeControlsAsync(Context ctx)
    {
        await RunNC1CiphertextCapTamperAsync(ctx);
        await RunNC2DeadlineRenewalAsync(ctx);
    }

    private static async Task RunNC1CiphertextCapTamperAsync(Context ctx)
    {
        // Actual execution (R3): the NC-1 control runs the E1-faithful candidate TLS drain with
        // the declared encrypted cap tampered 32768 -> 131072. The drain terminates at the
        // tampered cap, so the measured ciphertext wire bytes overshoot the registered 32768.
        // Detection: observed encrypted_wire_bytes > registered cap. Mirrors E1 NC-1 evidence
        // (drain outcome "cipher-cap", WireBytes = tampered cap).
        var cell = Campaign.Cells.First(c => c.Id == "L5");
        var profile = Campaign.CapProfile;
        var record = await Candidate.WithWatchdog(ctx, cell, profile, 1, 0, null,
            (token) => Candidate.RunCapAttemptAsync(ctx, cell, profile, 1, 0, null, false, token,
                tamperedCiphertextCap: 131072));
        record["control"] = "NC-1";
        record["tampered_ciphertext_cap"] = 131072;
        record["declared_ciphertext_cap"] = 131072;
        record["registered_ciphertext_cap"] = Campaign.CiphertextLimit;
        var outcome = (string)record["outcome"]!;
        var encryptedWire = record.ContainsKey("encrypted_wire_bytes")
            ? Convert.ToInt64(record["encrypted_wire_bytes"]) : 0L;
        // The control is detected when the tampered drain actually exceeded the registered cap.
        record["tamper_detected"] = encryptedWire > Campaign.CiphertextLimit;
        record["control_failure_observed"] = outcome == "cap-checks-failed" || outcome == "cap-bounded-and-clean";
        ctx.Journal.WriteControl("NC-1", record);
        ctx.Journal.Transcript("NC-1",
            $"outcome={outcome} registered={Campaign.CiphertextLimit} tampered=131072 " +
            $"observed_wire={encryptedWire} detected={record["tamper_detected"]}");
    }

    private static async Task RunNC2DeadlineRenewalAsync(Context ctx)
    {
        var cell = Campaign.Cells.First(c => c.Id == "L1");
        var profile = Campaign.ProfileFor(cell.Seed, 0);
        var record = await Candidate.WithWatchdog(ctx, cell, profile, 1, 0, null,
            (token) => Production.RunTerminalAttemptAsync(ctx, cell, profile, 1, 0, null, true, token));
        record["control"] = "NC-2";
        record["extended_deadline_ms"] = 800;
        record["bound_deadline_ms"] = Campaign.TotalElapsedBoundMs;
        ctx.Journal.WriteControl("NC-2", record);
        var outcome = (string)record["outcome"]!;
        var elapsed = (double)record.GetValueOrDefault("elapsed_ms", 0.0)!;
        ctx.Journal.Transcript("NC-2", $"outcome={outcome} elapsed={elapsed} bound={Campaign.TotalElapsedBoundMs}");
    }
}