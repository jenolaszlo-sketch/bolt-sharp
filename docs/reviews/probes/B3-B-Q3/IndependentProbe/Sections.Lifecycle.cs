using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;

namespace B3BQ3.IndependentProbe;

// Q2-F01 independent closure: failure matrix, the critical overlap scenario, and refusal of every
// subsequent message after failed retirement.
internal static partial class Program
{
    internal static ProbeProvider ThrowingProvider(string mode) => new()
    {
        Factory = _ =>
        {
            var op = mode switch
            {
                "pull-complete" => new ProbeOperation("op1", new[] { Row(1) }),
                "pull-read" => new ProbeOperation("op1", new[] { Row(1), Row(2) }) { ThrowReadAt = 2 },
                "row-shape" => new ProbeOperation("op1", new[] { TwoValues() }),
                "discard-complete" => new ProbeOperation("op1", new[] { Row(1) }),
                "discard-read" => new ProbeOperation("op1", new[] { Row(1), Row(2) }) { ThrowReadAt = 2 },
                _ => throw new ArgumentException(mode)
            };
            op.ThrowOnDispose = true;
            return op;
        }
    };

    internal static bool IsDiscard(string mode) => mode.StartsWith("discard", StringComparison.Ordinal);

    internal static BoltRequest RequestFor(string mode) =>
        IsDiscard(mode) ? new DiscardRequest(-1, null) : new PullRequest(-1, null);

    internal static void F01Matrix()
    {
        foreach (var mode in new[] { "pull-complete", "pull-read", "row-shape", "discard-complete", "discard-read" })
        {
            var provider = ThrowingProvider(mode);
            var s = ProbeSession.Authenticated("f01-" + mode, provider);
            var run = s.Send("RUN", Run());
            Check("f01-" + mode + "-run",
                Seq(run) == "SUCCESS" && s.Session.State == BoltConnectionState.Streaming && provider.Prepares == 1,
                "SUCCESS/Streaming;opens=1",
                $"{Seq(run)}/{s.Session.State};opens={provider.Prepares}");

            var failure = s.Send(mode, RequestFor(mode));
            var f = failure?.Responses.LastOrDefault() as FailureResponse;
            Check("f01-" + mode + "-primary",
                f is not null
                && f.GqlStatus == (mode == "row-shape" ? "50N42" : "50N00")
                && f.Neo4jCode == "Neo.DatabaseError.General.UnknownError"
                && f.Description == (mode == "row-shape" ? RowDescription : ProviderDescription),
                "primary FAILURE payload preserved",
                $"{f?.GqlStatus}/{f?.Neo4jCode}/{f?.Description}");
            Check("f01-" + mode + "-no-success",
                failure!.Responses.All(r => r is not SuccessResponse),
                "no SUCCESS summary after failed retirement",
                Seq(failure));
            Check("f01-" + mode + "-defunct",
                s.Session.State == BoltConnectionState.Defunct && failure.ShouldClose == true && s.Escaped is null,
                "DEFUNCT;close;no escape",
                $"{s.Session.State};close={failure.ShouldClose};escaped={s.Escaped?.GetType().Name}");
            var op = provider.Operations[0];
            Check("f01-" + mode + "-attempt",
                op.DisposeAttempts == 1 && op.ResourceLive && !s.Session.HasActiveOperation
                && ReferenceEquals(s.Session.LastCleanupFailure, op.CleanupError),
                "one attempt;resource live;detached;diagnostic",
                $"attempts={op.DisposeAttempts};live={op.ResourceLive};owned={s.Session.HasActiveOperation};diag={(ReferenceEquals(s.Session.LastCleanupFailure, op.CleanupError) ? "same" : "diff")}");

            var reset = s.Send("RESET", new ResetRequest());
            Check("f01-" + mode + "-reset-refused",
                reset!.Responses.Count == 0 && s.Session.State == BoltConnectionState.Defunct && reset.ShouldClose == true,
                "no response;DEFUNCT;close",
                $"{Seq(reset)}/{s.Session.State};close={reset.ShouldClose}");

            var run2 = s.Send("RUN", Run());
            Check("f01-" + mode + "-run-refused",
                Seq(run2) == "" && s.Session.State == BoltConnectionState.Defunct && provider.Prepares == 1,
                "no response;no provider call;opens=1",
                $"{Seq(run2)};opens={provider.Prepares}");

            s.Call("Disconnect", () => s.Session.Disconnect());
            s.Call("Dispose", () => s.Session.Dispose());
            Check("f01-" + mode + "-no-retry",
                op.DisposeAttempts == 1 && s.Session.IsDefunct,
                "exactly one attempt total",
                $"attempts={op.DisposeAttempts};state={s.Session.State}");
        }

        // RESET cleanup failure: FAILURE, DEFUNCT, close, one attempt, diagnostic.
        {
            var provider = ThrowingProvider("pull-complete");
            var s = ProbeSession.Authenticated("f01-reset-cleanup", provider);
            s.Send("RUN", Run());
            var reset = s.Send("RESET", new ResetRequest());
            var op = provider.Operations[0];
            Check("f01-reset-cleanup",
                Seq(reset) == "FAILURE" && s.Session.State == BoltConnectionState.Defunct && reset!.ShouldClose == true
                && op.DisposeAttempts == 1 && ReferenceEquals(s.Session.LastCleanupFailure, op.CleanupError) && s.Escaped is null,
                "FAILURE/Defunct;close;one attempt;diagnostic",
                $"{Seq(reset)}/{s.Session.State};attempts={op.DisposeAttempts}");
            var reset2 = s.Send("RESET", new ResetRequest());
            Check("f01-reset-cleanup-second",
                reset2!.Responses.Count == 0 && s.Session.IsDefunct && op.DisposeAttempts == 1,
                "no second attempt;no recovery",
                $"{Seq(reset2)};attempts={op.DisposeAttempts}");
        }

        // GOODBYE cleanup failure: no fabricated response, DEFUNCT, one attempt.
        {
            var provider = ThrowingProvider("pull-complete");
            var s = ProbeSession.Authenticated("f01-goodbye-cleanup", provider);
            s.Send("RUN", Run());
            var goodbye = s.Send("GOODBYE", new GoodbyeRequest());
            var op = provider.Operations[0];
            Check("f01-goodbye-cleanup",
                goodbye!.Responses.Count == 0 && s.Session.State == BoltConnectionState.Defunct && goodbye.ShouldClose == true
                && op.DisposeAttempts == 1 && s.Escaped is null,
                "no response;DEFUNCT;one attempt",
                $"responses={goodbye.Responses.Count};state={s.Session.State};attempts={op.DisposeAttempts}");
            var late = s.Send("late RUN", Run());
            Check("f01-goodbye-cleanup-late",
                Seq(late) == "" && provider.Prepares == 1 && op.DisposeAttempts == 1,
                "no processing after DEFUNCT",
                $"{Seq(late)};opens={provider.Prepares};attempts={op.DisposeAttempts}");
        }

        // Disconnect cleanup failure: DEFUNCT, one attempt, idempotent.
        {
            var provider = ThrowingProvider("pull-complete");
            var s = ProbeSession.Authenticated("f01-disconnect-cleanup", provider);
            s.Send("RUN", Run());
            s.Call("Disconnect", () => s.Session.Disconnect());
            var op = provider.Operations[0];
            Check("f01-disconnect-cleanup",
                s.Session.State == BoltConnectionState.Defunct && op.DisposeAttempts == 1 && s.Escaped is null
                && s.Session.LastCleanupFailure == op.CleanupError,
                "DEFUNCT;one attempt;no escape;diagnostic",
                $"state={s.Session.State};attempts={op.DisposeAttempts}");
            s.Call("Disconnect again", () => s.Session.Disconnect());
            Check("f01-disconnect-cleanup-idempotent",
                op.DisposeAttempts == 1 && s.Session.IsDefunct,
                "no retry",
                $"attempts={op.DisposeAttempts}");
        }

        // Session Dispose cleanup failure: terminal and idempotent.
        {
            var provider = ThrowingProvider("pull-complete");
            var s = ProbeSession.Authenticated("f01-dispose-cleanup", provider);
            s.Send("RUN", Run());
            s.Call("Dispose", () => s.Session.Dispose());
            var op = provider.Operations[0];
            Check("f01-dispose-cleanup",
                s.Session.State == BoltConnectionState.Defunct && op.DisposeAttempts == 1 && s.Escaped is null,
                "terminal;one attempt;no escape",
                $"state={s.Session.State};attempts={op.DisposeAttempts}");
            s.Call("Dispose again", () => s.Session.Dispose());
            Check("f01-dispose-cleanup-idempotent",
                op.DisposeAttempts == 1,
                "idempotent",
                $"attempts={op.DisposeAttempts}");
            ObjectDisposedException? ode = null;
            try { s.Session.Receive(Run()); }
            catch (ObjectDisposedException ex) { ode = ex; }
            Check("f01-dispose-cleanup-ode", ode is not null, "ObjectDisposedException after dispose", ode?.GetType().Name ?? "none");
        }

        // Successful retirement after recoverable error: FAILED/RESET recovery preserved.
        {
            var provider = new ProbeProvider
            {
                Factory = _ => new ProbeOperation("op", new[] { Row(1), Row(2) }) { ThrowReadAt = 1 }
            };
            var s = ProbeSession.Authenticated("f01-clean-recovery", provider);
            s.Send("RUN", Run());
            var failed = s.Send("PULL", new PullRequest(-1, null));
            var op = provider.Operations[0];
            Check("f01-clean-recovery-failed",
                Seq(failed) == "FAILURE" && s.Session.State == BoltConnectionState.Failed && failed!.ShouldClose == false
                && op.DisposeAttempts == 1 && op.ResourceLive == false && s.Session.LastCleanupFailure is null,
                "FAILURE/Failed;clean retirement;no diagnostic",
                $"{Seq(failed)}/{s.Session.State};attempts={op.DisposeAttempts};live={op.ResourceLive}");
            var reset = s.Send("RESET", new ResetRequest());
            Check("f01-clean-recovery-reset",
                Seq(reset) == "SUCCESS" && s.Session.State == BoltConnectionState.Ready,
                "SUCCESS/Ready",
                $"{Seq(reset)}/{s.Session.State}");
            var run2 = s.Send("RUN", Run());
            Check("f01-clean-recovery-run",
                Seq(run2) == "SUCCESS" && s.Session.State == BoltConnectionState.Streaming && provider.Prepares == 2,
                "recovery permitted;opens=2",
                $"{Seq(run2)};opens={provider.Prepares}");
        }
    }

    internal static void OverlapScenarios()
    {
        // The critical overlap sequence for every failed-retirement mode:
        // RUN -> failure -> RESET -> RUN -> late RESET -> Disconnect -> Dispose.
        foreach (var mode in new[] { "pull-complete", "pull-read", "row-shape", "discard-complete", "discard-read" })
        {
            var provider = ThrowingProvider(mode);
            var s = ProbeSession.Authenticated("overlap-" + mode, provider);
            s.Send("RUN", Run());
            s.Send(mode, RequestFor(mode));
            var reset = s.Send("RESET", new ResetRequest());
            var runAfter = s.Send("RUN", Run());
            var lateReset = s.Send("late RESET", new ResetRequest());
            s.Call("Disconnect", () => s.Session.Disconnect());
            s.Call("Dispose", () => s.Session.Dispose());
            var op = provider.Operations[0];
            Check("overlap-" + mode,
                provider.Prepares == 1 && op.DisposeAttempts == 1 && op.ResourceLive
                && s.Session.State == BoltConnectionState.Defunct
                && reset!.Responses.Count == 0 && runAfter!.Responses.Count == 0 && lateReset!.Responses.Count == 0
                && ReferenceEquals(s.Session.LastCleanupFailure, op.CleanupError)
                && s.Escaped is null,
                "opens=1;one attempt;resource live;DEFUNCT;no recovery responses;diagnostic",
                $"opens={provider.Prepares};attempts={op.DisposeAttempts};live={op.ResourceLive};state={s.Session.State};reset='{Seq(reset)}';run='{Seq(runAfter)}'");
        }
    }

    internal static void RecoveryRefusal()
    {
        // After failed retirement, every message class must be refused without state change,
        // provider activity, re-authentication or disposal retry.
        var provider = ThrowingProvider("pull-complete");
        var s = ProbeSession.Authenticated("refusal", provider);
        s.Send("RUN", Run());
        s.Send("PULL", new PullRequest(-1, null));
        var op = provider.Operations[0];

        var reset = s.Send("RESET", new ResetRequest());
        Check("refusal-RESET",
            reset!.Responses.Count == 0 && s.Session.IsDefunct && reset.ShouldClose == true,
            "no response;DEFUNCT;close",
            $"{Seq(reset)};state={s.Session.State}");
        var run = s.Send("RUN", Run());
        Check("refusal-RUN",
            run!.Responses.Count == 0 && s.Session.IsDefunct && provider.Prepares == 1,
            "no response;no provider open",
            $"{Seq(run)};opens={provider.Prepares}");
        var pull = s.Send("PULL", new PullRequest(-1, null));
        Check("refusal-PULL",
            pull!.Responses.Count == 0 && s.Session.IsDefunct,
            "no response",
            Seq(pull));
        var discard = s.Send("DISCARD", new DiscardRequest(-1, null));
        Check("refusal-DISCARD",
            discard!.Responses.Count == 0 && s.Session.IsDefunct,
            "no response",
            Seq(discard));
        var logoff = s.Send("LOGOFF", new LogoffRequest());
        Check("refusal-LOGOFF",
            logoff!.Responses.Count == 0 && s.Session.IsDefunct,
            "no response",
            Seq(logoff));
        var telemetry = s.Send("TELEMETRY", new TelemetryRequest(1));
        Check("refusal-TELEMETRY",
            telemetry!.Responses.Count == 0 && s.Session.IsDefunct,
            "no response",
            Seq(telemetry));
        var goodbye = s.Send("GOODBYE", new GoodbyeRequest());
        Check("refusal-GOODBYE",
            goodbye!.Responses.Count == 0 && s.Session.IsDefunct,
            "no response",
            Seq(goodbye));
        var logon = s.Send("LOGON", Logon());
        Check("refusal-LOGON",
            logon!.Responses.Count == 0 && s.Session.IsDefunct && s.Verifier.Calls == 1,
            "no reactivation;verifier not re-invoked",
            $"{Seq(logon)};verifierCalls={s.Verifier.Calls}");
        Check("refusal-all",
            provider.Prepares == 1 && op.DisposeAttempts == 1 && s.Session.IsDefunct && s.Escaped is null,
            "no provider open;no retry;still DEFUNCT;no escape",
            $"opens={provider.Prepares};attempts={op.DisposeAttempts};state={s.Session.State};escaped={s.Escaped?.GetType().Name}");
    }
}
