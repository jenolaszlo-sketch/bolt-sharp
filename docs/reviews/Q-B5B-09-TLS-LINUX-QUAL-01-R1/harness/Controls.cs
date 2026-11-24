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
        DispatchAudit.Invoke("Controls." + nameof(RunNC1CiphertextCapTamperAsync));
        // Actual execution (R3): the NC-1 control runs the E1-faithful candidate TLS drain with
        // the declared encrypted cap tampered 32768 -> 131072. With the strict transport-boundary
        // enforcement (R4), the drain terminates exactly at the tampered cap, so the measured
        // ciphertext wire bytes equal 131072 and exceed the registered 32768.
        // Detection: observed encrypted_wire_bytes > registered cap. Mirrors E1 NC-1 evidence
        // (drain outcome "cipher-cap", WireBytes = tampered cap).
        var cell = Campaign.Cells.First(c => c.Id == "L5");
        var profile = Campaign.CapProfile;
        var record = await Candidate.WithWatchdog(ctx, cell, profile, 1, 0, null,
            (token) => Candidate.RunCapAttemptAsync(ctx, cell, profile, 1, 0, null, false, token,
                tamperedCiphertextCap: 131072));
        record["control"] = "NC-1";
        record["dispatch_target"] = "Controls.RunNC1CiphertextCapTamperAsync";
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
        DispatchAudit.Invoke("Controls." + nameof(RunNC2DeadlineRenewalAsync));
        // The NC-2 tamper extends the total deadline beyond the registered bound on a profile
        // that actually induces the terminal-write pendency (the L1 prerequisite). R3 ran it on
        // discovery profile 1 only, which does not induce pendency on every platform; the
        // control then reported "precursor-not-observed" with elapsed ~2.6 ms and the extended
        // deadline was never exercised. R4 reuses the L1 qualifying profile when L1 qualified,
        // and otherwise walks the discovery profiles (registered attempt bound 3).
        var cell = Campaign.Cells.First(c => c.Id == "L1");
        Profile profile = Campaign.ProfileFor(cell.Seed, 0);
        var prereqOrdinal = 1;
        var prereqSource = "discovery profile 1 (L1 summary unavailable or unqualified)";
        var summaryPath = Path.Combine(ctx.Journal.Directory, "per-cell", "L1.summary.json");
        if (File.Exists(summaryPath))
        {
            try
            {
                var summary = JsonSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(summaryPath));
                var qualified = summary is not null
                    && Convert.ToBoolean(Json.Scalar(summary.GetValueOrDefault("qualified") ?? false) ?? false);
                var ordinalRaw = Json.Scalar(summary?.GetValueOrDefault("qualifying_profile_ordinal"));
                if (qualified && ordinalRaw is not null
                    && Convert.ToInt64(ordinalRaw) is { } ordinal && ordinal >= 1)
                {
                    prereqOrdinal = (int)ordinal;
                    profile = Campaign.Profiles[Math.Clamp(prereqOrdinal - 1, 0, Campaign.Profiles.Length - 1)];
                    prereqSource = $"L1.summary.json qualifying profile ordinal {prereqOrdinal}";
                }
            }
            catch (Exception)
            {
            }
        }

        Dictionary<string, object?> record = new();
        var tried = 0;
        for (var ordinal = prereqOrdinal; tried < 3 && ordinal <= Campaign.Profiles.Length; ordinal++, tried++)
        {
            profile = Campaign.Profiles[ordinal - 1];
            record = await Candidate.WithWatchdog(ctx, cell, profile, tried + 1, tried, null,
                (token) => Production.RunTerminalAttemptAsync(ctx, cell, profile, tried + 1, tried, null,
                    extendedDeadline: true, token));
            ctx.Journal.Transcript("NC-2",
                $"attempt {tried + 1}: profile ordinal {profile.Ordinal} outcome={record["outcome"]} " +
                $"confirmed={record.GetValueOrDefault("confirmed")} elapsed={record.GetValueOrDefault("elapsed_ms")}");
            if ((string)record["outcome"]! != "precursor-not-observed")
                break;
        }

        record["control"] = "NC-2";
        record["dispatch_target"] = "Controls.RunNC2DeadlineRenewalAsync";
        record["extended_deadline_ms"] = 800;
        record["bound_deadline_ms"] = Campaign.TotalElapsedBoundMs;
        record["prerequisite_profile_ordinal"] = profile.Ordinal;
        record["prerequisite_source"] = prereqSource;
        record["prerequisite_confirmed"] = record.GetValueOrDefault("confirmed");
        var outcome = (string)record["outcome"]!;
        var elapsed = Convert.ToDouble(Json.Scalar(record.GetValueOrDefault("elapsed_ms", 0.0)) ?? 0.0);
        var detected = outcome == "induced-but-checks-failed" && elapsed > Campaign.TotalElapsedBoundMs;
        record["deadline_tamper_detected"] = detected;
        // Persist the control record only after every detection field is set: the detection
        // flag must be part of the auditable evidence, not just the transcript.
        ctx.Journal.WriteControl("NC-2", record);
        ctx.Journal.Transcript("NC-2",
            $"outcome={outcome} elapsed={elapsed} bound={Campaign.TotalElapsedBoundMs} " +
            $"profile={profile.Ordinal} source={prereqSource} detected={detected}");
    }
}
