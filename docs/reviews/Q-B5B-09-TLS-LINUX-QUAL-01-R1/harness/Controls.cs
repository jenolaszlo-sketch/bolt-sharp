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
        var cell = Campaign.Cells.First(c => c.Id == "L5");
        var profile = new Profile(1, 4096, 4096, 0);
        var record = await Candidate.WithWatchdog(ctx, cell, profile, 1, 0, null,
            (token) => Candidate.RunCapAttemptAsync(ctx, cell, profile, 1, 131072, token));
        record["control"] = "NC-1";
        record["tampered_cap"] = 131072;
        record["declared_cap"] = Campaign.CiphertextLimit;
        ctx.Journal.WriteControl("NC-1", record);
        var outcome = (string)record["outcome"]!;
        ctx.Journal.Transcript("NC-1", $"outcome={outcome} declared={Campaign.CiphertextLimit} tampered=131072");
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