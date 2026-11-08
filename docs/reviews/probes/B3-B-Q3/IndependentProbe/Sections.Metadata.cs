using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;

namespace B3BQ3.IndependentProbe;

// Q2-F02 independent closure: metadata acquisition/ownership, snapshot correctness, publication
// ordering, combined adversarial failures, the X04 contract, and diagnostics/confidentiality.
internal static partial class Program
{
    internal static void MetadataScenarios()
    {
        // 1. first getter throws; clean retirement.
        {
            var provider = new ProbeProvider
            {
                Factory = _ => new ProbeOperation("op") { FieldsFactory = _ => throw new InvalidOperationException(SentinelMeta) }
            };
            var s = ProbeSession.Authenticated("meta-first-throw-clean", provider);
            var r = s.Send("RUN", Run());
            var op = provider.Operations[0];
            Check("meta-first-throw-clean",
                Seq(r) == "FAILURE" && s.Session.State == BoltConnectionState.Failed && r!.ShouldClose == false
                && op.DisposeAttempts == 1 && op.FieldsReads == 1 && !s.Session.HasActiveOperation
                && s.Session.LastCleanupFailure is null && provider.Prepares == 1 && s.Escaped is null,
                "FAILURE/Failed;one attempt;one read;no diagnostic;opens=1",
                $"{Seq(r)}/{s.Session.State};attempts={op.DisposeAttempts};reads={op.FieldsReads};opens={provider.Prepares}");
            var ignored = s.Send("PULL in Failed", new PullRequest(-1, null));
            Check("meta-first-throw-clean-not-streaming",
                Seq(ignored) == "IGNORED" && s.Session.State == BoltConnectionState.Failed,
                "IGNORED proves STREAMING was never published",
                $"{Seq(ignored)}/{s.Session.State}");
            var reset = s.Send("RESET", new ResetRequest());
            Check("meta-first-throw-clean-recovery",
                Seq(reset) == "SUCCESS" && s.Session.State == BoltConnectionState.Ready,
                "clean retirement keeps failure recoverable",
                $"{Seq(reset)}/{s.Session.State}");
            var run2 = s.Send("RUN", Run());
            Check("meta-first-throw-clean-reacquire",
                Seq(run2) == "FAILURE" && provider.Prepares == 2 && provider.Operations[1].DisposeAttempts == 1,
                "reacquisition permitted after clean retirement",
                $"opens={provider.Prepares};attempts2={provider.Operations[1].DisposeAttempts}");
        }

        // 2. first getter throws; retirement throws.
        {
            var provider = new ProbeProvider
            {
                Factory = _ => new ProbeOperation("op")
                {
                    FieldsFactory = _ => throw new InvalidOperationException(SentinelMeta),
                    ThrowOnDispose = true
                }
            };
            var s = ProbeSession.Authenticated("meta-first-throw-dirty", provider);
            var r = s.Send("RUN", Run());
            var op = provider.Operations[0];
            Check("meta-first-throw-dirty",
                Seq(r) == "FAILURE" && s.Session.State == BoltConnectionState.Defunct && r!.ShouldClose == true
                && op.DisposeAttempts == 1 && op.FieldsReads == 1
                && ReferenceEquals(s.Session.LastCleanupFailure, op.CleanupError),
                "FAILURE/Defunct;one attempt;diagnostic",
                $"{Seq(r)}/{s.Session.State};attempts={op.DisposeAttempts}");
            var reset = s.Send("RESET", new ResetRequest());
            var run = s.Send("RUN", Run());
            Check("meta-first-throw-dirty-no-recovery",
                reset!.Responses.Count == 0 && run!.Responses.Count == 0 && provider.Prepares == 1 && op.DisposeAttempts == 1,
                "no recovery;no reopen",
                $"reset='{Seq(reset)}';run='{Seq(run)}';opens={provider.Prepares};attempts={op.DisposeAttempts}");
            s.Call("Disconnect", () => s.Session.Disconnect());
            s.Call("Dispose", () => s.Session.Dispose());
            Check("meta-first-throw-dirty-no-retry",
                op.DisposeAttempts == 1,
                "one attempt total",
                $"attempts={op.DisposeAttempts}");
        }

        // 3. getter succeeds once, would throw on a second access: the second access must never happen.
        {
            var reads = 0;
            var provider = new ProbeProvider
            {
                Factory = _ => new ProbeOperation("op", new[] { Row(1), Row(2) })
                {
                    FieldsFactory = _ => ++reads == 1
                        ? new[] { "x" }
                        : throw new InvalidOperationException("Q3 second metadata access")
                }
            };
            var s = ProbeSession.Authenticated("meta-second-never-read", provider);
            var run = s.Send("RUN", Run());
            Check("meta-second-never-read-run",
                Seq(run) == "SUCCESS" && reads == 1 && s.Session.State == BoltConnectionState.Streaming,
                "SUCCESS/Streaming;one getter read",
                $"{Seq(run)};reads={reads}");
            var pull = s.Send("PULL", new PullRequest(-1, null));
            Check("meta-second-never-read-pull",
                Seq(pull) == "RECORD,RECORD,SUCCESS" && reads == 1 && s.Session.State == BoltConnectionState.Ready && s.Escaped is null,
                "no second access during PULL;normal completion",
                $"{Seq(pull)};reads={reads};escaped={s.Escaped?.GetType().Name}");
        }

        // 4. null Fields.
        {
            var provider = new ProbeProvider { Factory = _ => new ProbeOperation("op") { FieldsFactory = _ => null! } };
            var s = ProbeSession.Authenticated("meta-null-fields", provider);
            var r = s.Send("RUN", Run());
            var op = provider.Operations[0];
            Check("meta-null-fields",
                Seq(r) == "FAILURE" && s.Session.State == BoltConnectionState.Failed && op.DisposeAttempts == 1
                && op.FieldsReads == 1 && !s.Session.HasActiveOperation,
                "safe failure;one attempt",
                $"{Seq(r)}/{s.Session.State};attempts={op.DisposeAttempts}");
        }

        // 5. null field element.
        {
            var provider = new ProbeProvider { Factory = _ => new ProbeOperation("op") { FieldsFactory = _ => new string[] { null! } } };
            var s = ProbeSession.Authenticated("meta-null-element", provider);
            var r = s.Send("RUN", Run());
            var op = provider.Operations[0];
            Check("meta-null-element",
                Seq(r) == "FAILURE" && s.Session.State == BoltConnectionState.Failed && op.DisposeAttempts == 1 && op.FieldsReads == 1,
                "safe failure;one attempt",
                $"{Seq(r)}/{s.Session.State};attempts={op.DisposeAttempts}");
        }

        // 6. Count access throws.
        {
            var list = new FaultyList("x") { ThrowOnCount = true };
            var provider = new ProbeProvider { Factory = _ => new ProbeOperation("op") { FieldsFactory = _ => list } };
            var s = ProbeSession.Authenticated("meta-count-throws", provider);
            var r = s.Send("RUN", Run());
            var op = provider.Operations[0];
            Check("meta-count-throws",
                Seq(r) == "FAILURE" && s.Session.State == BoltConnectionState.Failed && op.DisposeAttempts == 1
                && list.CountReads == 1 && op.FieldsReads == 1,
                "contained;one attempt",
                $"{Seq(r)}/{s.Session.State};countReads={list.CountReads};attempts={op.DisposeAttempts}");
        }

        // 7. indexer access throws.
        {
            var list = new FaultyList("x") { ThrowOnIndexer = true };
            var provider = new ProbeProvider { Factory = _ => new ProbeOperation("op") { FieldsFactory = _ => list } };
            var s = ProbeSession.Authenticated("meta-indexer-throws", provider);
            var r = s.Send("RUN", Run());
            var op = provider.Operations[0];
            Check("meta-indexer-throws",
                Seq(r) == "FAILURE" && s.Session.State == BoltConnectionState.Failed && op.DisposeAttempts == 1
                && list.IndexerReads == 1,
                "contained;one attempt",
                $"{Seq(r)}/{s.Session.State};indexerReads={list.IndexerReads};attempts={op.DisposeAttempts}");
        }

        // 8. metadata changes between reads: RUN-time snapshot governs column checks; no re-read.
        {
            var reads = 0;
            var provider = new ProbeProvider
            {
                Factory = _ => new ProbeOperation("op", new[] { TwoValues() })
                {
                    FieldsFactory = _ => ++reads == 1 ? new[] { "x" } : new[] { "y", "z" }
                }
            };
            var s = ProbeSession.Authenticated("meta-changing", provider);
            var run = s.Send("RUN", Run());
            Check("meta-changing-run",
                Seq(run) == "SUCCESS" && reads == 1 && (run!.Responses[0] as SuccessResponse)!.Fields!.Single() == "x",
                "RUN uses the first validated snapshot",
                $"{Seq(run)};reads={reads}");
            var pull = s.Send("PULL", new PullRequest(-1, null));
            Check("meta-changing-snapshot-column-check",
                Seq(pull) == "FAILURE" && reads == 1 && s.Session.State == BoltConnectionState.Failed
                && (pull!.Responses[0] as FailureResponse)!.Description == RowDescription,
                "column check uses the RUN-time snapshot (1 field) against the 2-value row;no re-read",
                $"{Seq(pull)};reads={reads}");
        }

        // 9. valid metadata unchanged.
        {
            var provider = new ProbeProvider { Factory = _ => new ProbeOperation("op", new[] { Row(1) }) };
            var s = ProbeSession.Authenticated("meta-valid", provider);
            var run = s.Send("RUN", Run());
            Check("meta-valid",
                Seq(run) == "SUCCESS" && s.Session.State == BoltConnectionState.Streaming
                && (run!.Responses[0] as SuccessResponse)!.Fields!.Single() == "x",
                "SUCCESS fields/STREAMING unchanged",
                $"{Seq(run)}/{s.Session.State}");
        }

        // 10. valid empty metadata: supported zero-column semantics unchanged.
        {
            var provider = new ProbeProvider { Factory = _ => new ProbeOperation("op") { FieldsFactory = _ => Array.Empty<string>() } };
            var s = ProbeSession.Authenticated("meta-empty", provider);
            var run = s.Send("RUN", Run());
            Check("meta-empty-run",
                Seq(run) == "SUCCESS" && ((run!.Responses[0] as SuccessResponse)!.Fields?.Count ?? -1) == 0
                && s.Session.State == BoltConnectionState.Streaming,
                "SUCCESS empty fields/STREAMING",
                $"{Seq(run)}/{s.Session.State}");
            var pull = s.Send("PULL", new PullRequest(-1, null));
            Check("meta-empty-pull",
                Seq(pull) == "SUCCESS" && s.Session.State == BoltConnectionState.Ready,
                "summary/Ready",
                $"{Seq(pull)}/{s.Session.State}");
        }
    }

    internal static void OwnershipTiming()
    {
        // Ownership timing: the session owns the handle before metadata access, so a first-access
        // failure still retires it exactly once and teardown cannot leak it.
        {
            var provider = new ProbeProvider
            {
                Factory = _ => new ProbeOperation("op")
                {
                    FieldsFactory = _ => throw new InvalidOperationException(SentinelMeta),
                    ThrowOnDispose = true
                }
            };
            var s = ProbeSession.Authenticated("own-dirty", provider);
            s.Send("RUN", Run());
            var op = provider.Operations[0];
            s.Call("Disconnect", () => s.Session.Disconnect());
            s.Call("Dispose", () => s.Session.Dispose());
            Check("own-dirty-retired-once",
                op.DisposeAttempts == 1 && !s.Session.HasActiveOperation && provider.Operations.Count == 1,
                "exactly one retirement attempt;no unaccounted handle",
                $"attempts={op.DisposeAttempts};ops={provider.Operations.Count}");
            Check("own-dirty-no-reopen",
                provider.Prepares == 1 && s.Session.IsDefunct,
                "no new operation possible after failed retirement",
                $"opens={provider.Prepares};state={s.Session.State}");
        }
        {
            var provider = new ProbeProvider
            {
                Factory = _ => new ProbeOperation("op") { FieldsFactory = _ => throw new InvalidOperationException(SentinelMeta) }
            };
            var s = ProbeSession.Authenticated("own-clean", provider);
            s.Send("RUN", Run());
            var op = provider.Operations[0];
            s.Call("Disconnect", () => s.Session.Disconnect());
            Check("own-clean-retired-once",
                op.DisposeAttempts == 1 && op.ResourceLive == false && s.Session.IsDefunct,
                "attempted exactly once;released",
                $"attempts={op.DisposeAttempts};live={op.ResourceLive}");
        }
    }

    internal static void SnapshotAndPublication()
    {
        // Metadata withdrawn after the first successful snapshot: PULL must never re-read it.
        {
            var reads = 0;
            var provider = new ProbeProvider
            {
                Factory = _ => new ProbeOperation("op", new[] { Row(1), Row(2) })
                {
                    FieldsFactory = _ => ++reads == 1
                        ? new[] { "x" }
                        : throw new InvalidOperationException("Q3 metadata withdrawn")
                }
            };
            var s = ProbeSession.Authenticated("snap-withdrawn", provider);
            var run = s.Send("RUN", Run());
            var p1 = s.Send("PULL(1)", new PullRequest(1, null));
            var p2 = s.Send("PULL(-1)", new PullRequest(-1, null));
            Check("snap-withdrawn",
                Seq(run) == "SUCCESS" && Seq(p1) == "RECORD,SUCCESS" && Seq(p2) == "RECORD,SUCCESS"
                && reads == 1 && s.Session.State == BoltConnectionState.Ready && s.Escaped is null,
                "snapshot only;no later metadata access;normal completion",
                $"run='{Seq(run)}';p1='{Seq(p1)}';p2='{Seq(p2)}';reads={reads}");
        }

        // Publication ordering: invalid metadata cannot publish STREAMING; single FAILURE; one attempt.
        {
            var provider = new ProbeProvider
            {
                Factory = _ => new ProbeOperation("op") { FieldsFactory = _ => null!, ThrowOnDispose = true }
            };
            var s = ProbeSession.Authenticated("pub-invalid", provider);
            var r = s.Send("RUN", Run());
            Check("pub-invalid",
                Seq(r) == "FAILURE" && r!.Responses.Count == 1 && s.Session.State == BoltConnectionState.Defunct
                && !s.Session.HasActiveOperation && provider.Operations[0].DisposeAttempts == 1,
                "single FAILURE;no STREAMING;one attempt",
                $"{Seq(r)}/{s.Session.State};attempts={provider.Operations[0].DisposeAttempts}");
        }

        // RECORD precedes FAILURE; exactly one terminal summary.
        {
            var provider = ThrowingProvider("pull-read");
            var s = ProbeSession.Authenticated("pub-ordering", provider);
            s.Send("RUN", Run());
            var pull = s.Send("PULL", new PullRequest(-1, null));
            Check("pub-ordering",
                pull!.Responses.Count == 2 && pull.Responses[0] is RecordResponse && pull.Responses[1] is FailureResponse,
                "RECORD then FAILURE;one terminal summary",
                Seq(pull));
        }
    }

    internal static void CombinedAdversarial()
    {
        // c1: getter throws + Dispose throws.
        CombinedCase("comb-c1", _ => new ProbeOperation("op")
        {
            FieldsFactory = _ => throw new InvalidOperationException(SentinelMeta),
            ThrowOnDispose = true
        }, "RUN", null, "FAILURE");

        // c2: Count throws + Dispose throws.
        CombinedCase("comb-c2", _ => new ProbeOperation("op")
        {
            FieldsFactory = _ => new FaultyList("x") { ThrowOnCount = true },
            ThrowOnDispose = true
        }, "RUN", null, "FAILURE");

        // c3: indexer throws + Dispose throws.
        CombinedCase("comb-c3", _ => new ProbeOperation("op")
        {
            FieldsFactory = _ => new FaultyList("x") { ThrowOnIndexer = true },
            ThrowOnDispose = true
        }, "RUN", null, "FAILURE");

        // c4: null element + Dispose throws.
        CombinedCase("comb-c4", _ => new ProbeOperation("op")
        {
            FieldsFactory = _ => new string[] { null! },
            ThrowOnDispose = true
        }, "RUN", null, "FAILURE");

        // c5: read exception + Dispose throws.
        CombinedCase("comb-c5", _ => new ProbeOperation("op", new[] { Row(1), Row(2) })
        {
            ThrowReadAt = 2,
            ThrowOnDispose = true
        }, "PULL", new PullRequest(-1, null), "RECORD,FAILURE");

        // c6: malformed RECORD + Dispose throws.
        CombinedCase("comb-c6", _ => new ProbeOperation("op", new[] { TwoValues() })
        {
            ThrowOnDispose = true
        }, "PULL", new PullRequest(-1, null), "FAILURE");

        // c7a: successful partial DISCARD (no retirement yet) then GOODBYE cleanup exception.
        {
            var provider = new ProbeProvider
            {
                Factory = _ => new ProbeOperation("op", new[] { Row(1), Row(2) }) { ThrowOnDispose = true }
            };
            var s = ProbeSession.Authenticated("comb-c7a", provider);
            s.Send("RUN", Run());
            var partial = s.Send("DISCARD(1)", new DiscardRequest(1, null));
            Check("comb-c7a-partial",
                Seq(partial) == "SUCCESS" && s.Session.State == BoltConnectionState.Streaming
                && provider.Operations[0].DisposeAttempts == 0,
                "partial discard retains ownership;no retirement yet",
                $"{Seq(partial)};attempts={provider.Operations[0].DisposeAttempts}");
            var goodbye = s.Send("GOODBYE", new GoodbyeRequest());
            Check("comb-c7a-goodbye",
                goodbye!.Responses.Count == 0 && s.Session.State == BoltConnectionState.Defunct
                && provider.Operations[0].DisposeAttempts == 1,
                "retirement at GOODBYE fails;DEFUNCT;no fabricated response",
                $"responses={goodbye.Responses.Count};attempts={provider.Operations[0].DisposeAttempts}");
        }

        // c7b: partial PULL then RESET cleanup exception.
        {
            var provider = new ProbeProvider
            {
                Factory = _ => new ProbeOperation("op", new[] { Row(1), Row(2) }) { ThrowOnDispose = true }
            };
            var s = ProbeSession.Authenticated("comb-c7b", provider);
            s.Send("RUN", Run());
            s.Send("PULL(1)", new PullRequest(1, null));
            var reset = s.Send("RESET", new ResetRequest());
            Check("comb-c7b",
                Seq(reset) == "FAILURE" && s.Session.State == BoltConnectionState.Defunct && reset!.ShouldClose
                && provider.Operations[0].DisposeAttempts == 1,
                "FAILURE/Defunct;one attempt",
                $"{Seq(reset)}/{s.Session.State};attempts={provider.Operations[0].DisposeAttempts}");
        }

        // c8: failure during cleanup followed by RESET and another RUN (discard variant).
        {
            var provider = ThrowingProvider("discard-complete");
            var s = ProbeSession.Authenticated("comb-c8", provider);
            s.Send("RUN", Run());
            s.Send("DISCARD", new DiscardRequest(-1, null));
            var reset = s.Send("RESET", new ResetRequest());
            var run = s.Send("RUN", Run());
            Check("comb-c8",
                provider.Prepares == 1 && provider.Operations[0].DisposeAttempts == 1
                && reset!.Responses.Count == 0 && run!.Responses.Count == 0 && s.Session.IsDefunct,
                "no recovery;no second operation",
                $"opens={provider.Prepares};attempts={provider.Operations[0].DisposeAttempts};reset='{Seq(reset)}';run='{Seq(run)}'");
        }
    }

    internal static void CombinedCase(
        string id,
        Func<ProbeProvider, ProbeOperation> factory,
        string action,
        BoltRequest? request,
        string expectedSequence)
    {
        var provider = new ProbeProvider { Factory = factory };
        var s = ProbeSession.Authenticated(id, provider);
        BoltSessionResult? r;
        if (request is null)
        {
            // RUN-based: the acquired operation fails during metadata handling.
            r = s.Send(action, Run());
        }
        else
        {
            // PULL-based: RUN first (the same defective handle is acquired and published),
            // then the failing pull/discard against it.
            s.Send("RUN", Run());
            r = s.Send(action, request);
        }

        var op = provider.Operations[0];
        Check(id + "-contained",
            Seq(r) == expectedSequence && s.Session.State == BoltConnectionState.Defunct && r!.ShouldClose
            && op.DisposeAttempts == 1 && !s.Session.HasActiveOperation && s.Escaped is null,
            $"FAILURE;DEFUNCT;one attempt;contained (expected {expectedSequence})",
            $"{Seq(r)}/{s.Session.State};attempts={op.DisposeAttempts};escaped={s.Escaped?.GetType().Name}");
        var reset = s.Send("RESET", new ResetRequest());
        var run = s.Send("RUN", Run());
        Check(id + "-no-recovery",
            reset!.Responses.Count == 0 && run!.Responses.Count == 0 && provider.Prepares == 1 && op.DisposeAttempts == 1,
            "no recovery;no reopen;one attempt",
            $"reset='{Seq(reset)}';run='{Seq(run)}';opens={provider.Prepares};attempts={op.DisposeAttempts}");
    }

    internal static void X04Contract()
    {
        // Historical X04 contract: RUN null Fields + throwing disposal -> FAILURE/Defunct,
        // one attempt, no RESET recovery, no second operation.
        var provider = new ProbeProvider
        {
            Factory = _ => new ProbeOperation("op") { FieldsFactory = _ => null!, ThrowOnDispose = true }
        };
        var s = ProbeSession.Authenticated("x04", provider);
        var r = s.Send("RUN", Run());
        var op = provider.Operations[0];
        Check("x04-contained",
            Seq(r) == "FAILURE" && s.Session.State == BoltConnectionState.Defunct && r!.ShouldClose && op.DisposeAttempts == 1,
            "FAILURE/Defunct;one attempt",
            $"{Seq(r)}/{s.Session.State};attempts={op.DisposeAttempts}");
        var reset = s.Send("RESET", new ResetRequest());
        var run = s.Send("RUN", Run());
        Check("x04-no-recovery",
            reset!.Responses.Count == 0 && run!.Responses.Count == 0 && provider.Prepares == 1 && op.DisposeAttempts == 1,
            "no recovery;no second disposal;opens=1",
            $"reset='{Seq(reset)}';run='{Seq(run)}';opens={provider.Prepares};attempts={op.DisposeAttempts}");
    }

    internal static void DiagnosticsAndConfidentiality()
    {
        // Primary failure preserved; secondary cleanup failure recorded separately; no text leak.
        var provider = ThrowingProvider("pull-read");
        var s = ProbeSession.Authenticated("diag", provider);
        s.Send("RUN", Run());
        var pull = s.Send("PULL", new PullRequest(-1, null));
        var f = (FailureResponse)pull!.Responses[^1];
        Check("diag-primary-preserved",
            f.GqlStatus == "50N00" && f.Neo4jCode == "Neo.DatabaseError.General.UnknownError",
            "primary provider failure classification",
            $"{f.GqlStatus}/{f.Neo4jCode}");
        var cleanupDiagnostic = s.Session.LastCleanupFailure;
        Check("diag-secondary-separate",
            ReferenceEquals(cleanupDiagnostic, provider.Operations[0].CleanupError)
            && cleanupDiagnostic!.Message == CleanupText
            && f.Description != cleanupDiagnostic.Message,
            "cleanup diagnostic separate from the primary response",
            $"primary='{f.Description}';cleanup='{cleanupDiagnostic!.Message}'");
        var leaked = Flat(f.Metadata).Values.Any(v => v is not null && (v.Contains(SentinelRead) || v.Contains(CleanupText)));
        Check("diag-no-sentinel-in-response",
            !leaked,
            "no provider/cleanup text in response metadata",
            leaked ? "LEAKED" : "clean");

        // Mutating the recorded exception cannot alter ownership, state or counters.
        var op = provider.Operations[0];
        op.CleanupError.Data["q3-mutation"] = "mutated";
        var reset = s.Send("RESET", new ResetRequest());
        var run = s.Send("RUN", Run());
        Check("diag-mutation-inert",
            s.Session.State == BoltConnectionState.Defunct && provider.Prepares == 1
            && op.DisposeAttempts == 1 && ReferenceEquals(s.Session.LastCleanupFailure, op.CleanupError),
            "exception mutation cannot alter ownership/state",
            $"state={s.Session.State};opens={provider.Prepares};attempts={op.DisposeAttempts}");
    }
}
