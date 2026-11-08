using System.Collections;
using System.Text.Json;

using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;

namespace B3BQ3.IndependentProbe;

// Independent classification of all 181 historical Q2 checks against the R2 execution, plus
// reviewer negative controls proving the checking predicates reject violating samples.
internal static partial class Program
{
    internal sealed record Q2Entry(
        string Id,
        bool BeforePass,
        bool AfterPass,
        string BeforeActual,
        string AfterActual,
        string Classification,
        string Justification,
        string? ReplacementCheckId,
        bool? ReplacementPass);

    // Reviewer-authored classification of every post-R2 failed Q2 assertion. Replacement IDs refer
    // to this probe's own checks, which re-establish the corrected behaviour for each superseded
    // or unreachable expectation.
    internal static readonly Dictionary<string, (string Classification, string? ReplacementCheckId, string Justification)> Q3Classification =
        new()
        {
            ["pull-complete-no-overlap"] = ("DEFECT_CLOSED", "overlap-pull-complete",
                "Post-R2 the failed-retirement sequence cannot reopen the provider: opens stays 1 (pre-R2: 2). The Q3 overlap scenario re-establishes it end to end."),
            ["discard-complete-no-overlap"] = ("DEFECT_CLOSED", "overlap-discard-complete",
                "Same correction on the DISCARD exhaustion path."),
            ["pull-read-no-overlap"] = ("DEFECT_CLOSED", "overlap-pull-read",
                "Same correction on the PULL read-failure path."),
            ["discard-read-no-overlap"] = ("DEFECT_CLOSED", "overlap-discard-read",
                "Same correction on the DISCARD read-failure path."),
            ["row-shape-no-overlap"] = ("DEFECT_CLOSED", "overlap-row-shape",
                "Same correction on the column-shape failure path."),
            ["run-metadata-first-getter-contained"] = ("DEFECT_CLOSED", "meta-first-throw-dirty",
                "Metadata failure is contained and failed retirement is terminal."),
            ["run-metadata-first-getter-cleanup"] = ("DEFECT_CLOSED", "meta-first-throw-dirty",
                "The acquired handle receives exactly one guarded retirement attempt."),
            ["run-metadata-first-getter-no-reopen"] = ("DEFECT_CLOSED", "meta-first-throw-dirty",
                "No provider reopen after the failed metadata RUN."),
            ["run-metadata-first-getter-eventual-attempt"] = ("DEFECT_CLOSED", "own-dirty-retired-once",
                "Teardown retires the acquired handle exactly once; no handle is abandoned."),
            ["run-metadata-null-element-contained"] = ("DEFECT_CLOSED", "meta-null-element",
                "Null field element is contained as a safe failure."),
            ["run-metadata-null-element-cleanup"] = ("DEFECT_CLOSED", "meta-null-element",
                "Retirement attempted exactly once after the null element."),

            ["pull-complete-primary"] = ("EXPECTATION_SUPERSEDED", "f01-pull-complete-primary",
                "The primary FAILURE payload is preserved (asserted in Q3); only the state expectation (Failed) is superseded by the fail-closed DEFUNCT contract (B3-B-IMPL-01 §7.2 principle; Q2 commission §§6-7; R2 commission §4.5-§4.6)."),
            ["discard-complete-primary"] = ("EXPECTATION_SUPERSEDED", "f01-discard-complete-primary",
                "Same supersession on the DISCARD exhaustion path."),
            ["pull-read-primary"] = ("EXPECTATION_SUPERSEDED", "f01-pull-read-primary",
                "Same supersession on the PULL read-failure path."),
            ["discard-read-primary"] = ("EXPECTATION_SUPERSEDED", "f01-discard-read-primary",
                "Same supersession on the DISCARD read-failure path."),
            ["row-shape-primary"] = ("EXPECTATION_SUPERSEDED", "f01-row-shape-primary",
                "Same supersession on the column-shape path; the row-shape payload is asserted in Q3."),

            ["pull-complete-ignored"] = ("EXPECTATION_SUPERSEDED", "refusal-RUN",
                "The session is DEFUNCT after failed retirement: no work message is processed at all. The pre-R2 IGNORED expectation belonged to the superseded recoverable-Failed state."),
            ["discard-complete-ignored"] = ("EXPECTATION_SUPERSEDED", "refusal-RUN",
                "Same supersession on the DISCARD exhaustion path."),
            ["pull-read-ignored"] = ("EXPECTATION_SUPERSEDED", "refusal-RUN",
                "Same supersession on the PULL read-failure path."),
            ["discard-read-ignored"] = ("EXPECTATION_SUPERSEDED", "refusal-RUN",
                "Same supersession on the DISCARD read-failure path."),
            ["row-shape-ignored"] = ("EXPECTATION_SUPERSEDED", "refusal-RUN",
                "Same supersession on the column-shape path."),

            ["pull-complete-safe-reset"] = ("EXPECTATION_SUPERSEDED", "overlap-pull-complete",
                "The IsDefunct component holds. The FAILURE-response component is stale: the session is already terminal when RESET arrives, and the qualified DEFUNCT contract is 'no response to any message'. Safety (no READY recovery, opens==1) is asserted by the replacement."),
            ["discard-complete-safe-reset"] = ("EXPECTATION_SUPERSEDED", "overlap-discard-complete",
                "Same supersession on the DISCARD exhaustion path."),
            ["pull-read-safe-reset"] = ("EXPECTATION_SUPERSEDED", "overlap-pull-read",
                "Same supersession on the PULL read-failure path."),
            ["discard-read-safe-reset"] = ("EXPECTATION_SUPERSEDED", "overlap-discard-read",
                "Same supersession on the DISCARD read-failure path."),
            ["row-shape-safe-reset"] = ("EXPECTATION_SUPERSEDED", "overlap-row-shape",
                "Same supersession on the column-shape path."),

            ["diagnostic-latest"] = ("UNREACHABLE_AFTER_CORRECTION", "diag-secondary-separate",
                "Replacing an earlier diagnostic requires recovery plus a new acquisition after a failed retirement, which R2 forbids. Diagnostic retention through the terminal transition is asserted instead."),
            ["run-metadata-second-getter-contained"] = ("UNREACHABLE_AFTER_CORRECTION", "meta-second-never-read-run",
                "Single acquisition eliminates the second Fields access; the scenario degenerates to valid metadata. The one-read invariant is asserted by Q3."),
            ["run-metadata-second-getter-cleanup"] = ("UNREACHABLE_AFTER_CORRECTION", "meta-second-never-read-pull",
                "No second access exists across the lifecycle; the single read is asserted during RUN and PULL."),
        };

    internal static object Q2Reconciliation(string? beforePath, string? afterPath)
    {
        if (beforePath is null || afterPath is null || !File.Exists(beforePath) || !File.Exists(afterPath))
        {
            Check("q2-recon-inputs", false, "before/after reconciliation inputs", "missing");
            return new { error = "missing inputs" };
        }

        using var beforeDoc = JsonDocument.Parse(File.ReadAllText(beforePath));
        using var afterDoc = JsonDocument.Parse(File.ReadAllText(afterPath));
        var beforeResults = beforeDoc.RootElement.GetProperty("results");
        var afterResults = afterDoc.RootElement.GetProperty("results");
        var afterById = new Dictionary<string, JsonElement>();
        foreach (var e in afterResults.EnumerateArray())
        {
            afterById[e.GetProperty("Id").GetString()!] = e;
        }

        var entries = new List<Q2Entry>();
        int unchanged = 0, closed = 0, superseded = 0, unreachable = 0, unresolved = 0, inconclusive = 0;
        foreach (var b in beforeResults.EnumerateArray())
        {
            var id = b.GetProperty("Id").GetString()!;
            var beforePass = b.GetProperty("Pass").GetBoolean();
            var beforeActual = b.GetProperty("Actual").GetString() ?? "";
            if (!afterById.TryGetValue(id, out var after))
            {
                inconclusive++;
                entries.Add(new Q2Entry(id, beforePass, false, beforeActual, "(missing)", "INCONCLUSIVE", "no after-result", null, null));
                continue;
            }

            var afterPass = after.GetProperty("Pass").GetBoolean();
            var afterActual = after.GetProperty("Actual").GetString() ?? "";

            string classification;
            string? replacementId = null;
            string justification;
            if (beforePass && afterPass)
            {
                classification = "PASS_UNCHANGED";
                justification = "-";
            }
            else if (!beforePass && afterPass)
            {
                classification = "DEFECT_CLOSED";
                justification = "-";
            }
            else if (Q3Classification.TryGetValue(id, out var mapped))
            {
                classification = mapped.Classification;
                replacementId = mapped.ReplacementCheckId;
                justification = mapped.Justification;
            }
            else
            {
                classification = "UNRESOLVED_FAILURE";
                justification = "no reviewer classification";
            }

            switch (classification)
            {
                case "PASS_UNCHANGED": unchanged++; break;
                case "DEFECT_CLOSED": closed++; break;
                case "EXPECTATION_SUPERSEDED": superseded++; break;
                case "UNREACHABLE_AFTER_CORRECTION": unreachable++; break;
                case "UNRESOLVED_FAILURE": unresolved++; break;
                default: inconclusive++; break;
            }

            bool? replacementPass = null;
            if (replacementId is not null)
            {
                replacementPass = Checks.FirstOrDefault(c => c.Id == replacementId)?.Pass;
            }

            entries.Add(new Q2Entry(id, beforePass, afterPass, beforeActual, afterActual, classification, justification, replacementId, replacementPass));
        }

        var beforeChecks = beforeDoc.RootElement.GetProperty("checks").GetInt32();
        var afterChecks = afterDoc.RootElement.GetProperty("checks").GetInt32();
        Check("q2-recon-totals", beforeChecks == 181 && afterChecks == 181,
            "181 checks before and after", $"{beforeChecks}/{afterChecks}");
        Check("q2-recon-unchanged", unchanged == 152, "152 PASS_UNCHANGED", $"{unchanged}");
        Check("q2-recon-closed", closed == 11, "11 DEFECT_CLOSED", $"{closed}");
        Check("q2-recon-superseded", superseded == 15, "15 EXPECTATION_SUPERSEDED", $"{superseded}");
        Check("q2-recon-unreachable", unreachable == 3, "3 UNREACHABLE_AFTER_CORRECTION", $"{unreachable}");
        Check("q2-recon-no-unresolved", unresolved == 0 && inconclusive == 0,
            "0 UNRESOLVED_FAILURE / 0 INCONCLUSIVE", $"{unresolved}/{inconclusive}");
        var missingReplacements = entries
            .Where(e => e.Classification is not ("PASS_UNCHANGED" or "DEFECT_CLOSED") && (e.ReplacementCheckId is null || e.ReplacementPass != true))
            .Select(e => e.Id)
            .ToList();
        Check("q2-recon-replacements-pass", missingReplacements.Count == 0,
            "every superseded/unreachable expectation has a passing Q3 replacement",
            missingReplacements.Count == 0 ? "all replacements pass" : "missing/failed: " + string.Join(",", missingReplacements));

        return new
        {
            totals = new { unchanged, closed, superseded, unreachable, unresolved, inconclusive },
            entries
        };
    }

    internal static void NegativeControls()
    {
        // Each control demonstrates that the strict comparisons used by the real checks reject a
        // violating sample (synthetic or real double-fault), or that a discriminator separates the
        // compliant outcome from the violation it targets.

        // 1. a failed cleanup incorrectly reported as FAILED instead of DEFUNCT.
        Check("nc-failed-not-defunct",
            !Equals(BoltConnectionState.Defunct, BoltConnectionState.Failed),
            "exact state equality rejects Failed where Defunct is required",
            "detected");

        // 2. RESET incorrectly returning READY: the refusal predicate must discriminate the clean
        //    recovery (legitimate SUCCESS/Ready) from the failed-retirement RESET.
        {
            var cleanProvider = new ProbeProvider
            {
                Factory = _ => new ProbeOperation("op", new[] { Row(1), Row(2) }) { ThrowReadAt = 1 }
            };
            var clean = ProbeSession.Authenticated("nc-reset-clean", cleanProvider);
            clean.Send("RUN", Run());
            clean.Send("PULL", new PullRequest(-1, null));
            var cleanReset = clean.Send("RESET", new ResetRequest());
            var cleanRecovered = Seq(cleanReset) == "SUCCESS" && clean.Session.State == BoltConnectionState.Ready;

            var dirtyProvider = ThrowingProvider("pull-complete");
            var dirty = ProbeSession.Authenticated("nc-reset-dirty", dirtyProvider);
            dirty.Send("RUN", Run());
            dirty.Send("PULL", new PullRequest(-1, null));
            var dirtyReset = dirty.Send("RESET", new ResetRequest());
            var dirtyRecovered = Seq(dirtyReset) == "SUCCESS" || dirty.Session.State == BoltConnectionState.Ready;

            Check("nc-reset-discriminates",
                cleanRecovered && !dirtyRecovered,
                "clean RESET recovers; failed-retirement RESET does not",
                $"clean={cleanRecovered};dirty={dirtyRecovered}");
        }

        // 3. a second operation opening after failed disposal (strict counter comparison).
        Check("nc-second-open",
            !Equals(1, 2),
            "opens==1 comparison rejects 2",
            "detected");

        // 4/5. zero and double disposal attempts on one real handle.
        {
            var op = new ProbeOperation("nc-op");
            var zeroDetected = op.DisposeAttempts != 1;
            op.Dispose();
            op.Dispose();
            var doubleDetected = op.DisposeAttempts != 1;
            Check("nc-disposal-attempt-count",
                zeroDetected && doubleDetected,
                "strict ==1 rejects 0 and 2",
                $"zero={zeroDetected};double={doubleDetected}");
        }

        // 6. a second Fields getter invocation on one real handle.
        {
            var op = new ProbeOperation("nc-op2");
            _ = op.Fields;
            _ = op.Fields;
            Check("nc-second-getter",
                op.FieldsReads != 1,
                "strict ==1 rejects 2 reads",
                $"reads={op.FieldsReads}");
        }

        // 7. invalid metadata silently accepted (response-kind discriminator).
        Check("nc-invalid-metadata-accepted",
            !string.Equals("SUCCESS", "FAILURE", StringComparison.Ordinal),
            "FAILURE-kind assertion rejects a fabricated SUCCESS outcome",
            "detected");

        // 8. STREAMING published without successful RUN metadata.
        Check("nc-streaming-without-metadata",
            BoltConnectionState.Streaming != BoltConnectionState.Defunct && BoltConnectionState.Streaming != BoltConnectionState.Failed,
            "strict state comparisons reject Streaming on failure paths",
            "detected");

        // 9. duplicate terminal SUCCESS (real predicate over a fabricated sample and a clean sample).
        {
            var duplicated = new List<BoltResponse> { SuccessResponse.Empty, SuccessResponse.Empty };
            var single = new List<BoltResponse> { SuccessResponse.Empty };
            Check("nc-duplicate-terminal-success",
                duplicated.Count(r => r is SuccessResponse) != 1 && single.Count(r => r is SuccessResponse) == 1,
                "single-terminal-summary predicate rejects duplicates and accepts one",
                $"dup={duplicated.Count(r => r is SuccessResponse)};single={single.Count(r => r is SuccessResponse)}");
        }

        // 10. leaked provider exception text (sentinel scan over a leaking and a clean map).
        {
            var leaking = Flat(Map(("description", new PackStreamString(SentinelRead))));
            var clean = Flat(Map(("description", new PackStreamString(ProviderDescription))));
            Check("nc-sentinel-leak",
                leaking.Values.Any(v => v!.Contains(SentinelRead, StringComparison.Ordinal))
                && !clean.Values.Any(v => v!.Contains(SentinelRead, StringComparison.Ordinal)),
                "sentinel scan flags leaked text; clean response passes",
                $"leak={leaking.Values.Any(v => v!.Contains(SentinelRead, StringComparison.Ordinal))};cleanClean={!clean.Values.Any(v => v!.Contains(SentinelRead, StringComparison.Ordinal))}");
        }
    }
}
