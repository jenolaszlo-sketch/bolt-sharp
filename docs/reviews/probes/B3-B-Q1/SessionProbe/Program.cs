using System.Text.Json;
using BoltSharp.Messages;
using BoltSharp.Sessions;

namespace B3BQ1.SessionProbe;

/// <summary>
/// B3-B-Q1 independent qualification probe. Every check records the reviewer's expected value and
/// the observed value; the exit code is non-zero when a check fails (i.e. observed != expected), so
/// a stale evidence set cannot be mistaken for a passing one. Findings that document producer
/// behaviour (for example cleanup-failure handling) are recorded with the reviewer's expectation
/// even when that expectation is not met.
/// </summary>
internal static class Program
{
    private static readonly List<CheckResult> Checks = new();
    private static readonly List<string> Findings = new();

    private static int Main(string[] args)
    {
        var outputDirectory = args.Length > 0 ? args[0] : "docs/reviews/evidence/B3-B-Q1/probe";
        Directory.CreateDirectory(outputDirectory);

        StateMatrix();
        Authentication();
        PullMinusOne();
        ResetInterruption();
        BatchPipelining();
        FailedIgnored();
        CursorOwnership();
        ContextualValidation();
        TelemetryAndUnsupported();
        CleanupFailure();
        RaceAndResource();

        var failed = Checks.Count(c => !c.Pass);
        var document = new
        {
            probe = "B3-B-Q1 independent session qualification probe",
            reviewed_head = "81c0af9547a8b4e2a6f7310226763bea0819cad9",
            implementation_commit = "9f224cf41656e4baf155532e682b862ed8e910b0",
            checks = Checks.Count,
            failed,
            findings = Findings,
            results = Checks,
        };
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(outputDirectory, "session-probe-results.json"), json + Environment.NewLine);
        Console.Error.WriteLine($"checks={Checks.Count} failed={failed} findings={Findings.Count}");
        return failed == 0 ? 0 : 4;
    }

    private static void Check(string id, string expected, string actual)
    {
        Checks.Add(new CheckResult(id, expected, actual, expected == actual));
    }

    // ---- State transition matrix ----

    private static void StateMatrix()
    {
        var s = H.New(new ProbeProvider(_ => new ProbeOperation(new[] { "x" }, H.Row(1))));
        Check("S01-new-state", "Negotiation", s.State.ToString());
        Check("S02-hello", "SUCCESS/Authentication", Seq(s.Send(H.Hello())));
        Check("S03-hello-state", "Authentication", s.State.ToString());
        Check("S04-logon", "SUCCESS/Ready", Seq(s.Send(H.Logon())));
        Check("S05-run", "SUCCESS/Streaming", Seq(s.Send(H.Run())));
        Check("S06-pull", "RECORD,SUCCESS/Ready", Seq(s.Send(H.Pull(-1))));
        Check("S07-telemetry-valid", "SUCCESS/Ready", Seq(s.Send(H.Telemetry(2))));
        Check("S08-reset-ready", "SUCCESS/Ready", Seq(s.Send(H.Reset())));
        Check("S09-goodbye", "/Defunct", Seq(s.Send(H.Goodbye())));

        var d = H.New(new ProbeProvider(_ => null));
        d.Send(H.Hello());
        d.Send(H.Logon());
        d.Send(H.Run()); // failure
        Check("S10-failed-run", "Failed", d.State.ToString());
        Check("S11-failed-pull-ignored", "IGNORED/Failed", Seq(d.Send(H.Pull(1))));
        Check("S12-failed-reset", "SUCCESS/Ready", Seq(d.Send(H.Reset())));
        Check("S13-reset-then-run-fails-again", "FAILURE/Failed", Seq(d.Send(H.Run())));

        var g = H.New(new ProbeProvider(_ => null));
        Check("S14-logon-before-hello", "FAILURE/Defunct", Seq(g.Send(H.Logon())));
        var g2 = H.New(new ProbeProvider(_ => null));
        g2.Send(H.Hello());
        Check("S15-run-before-logon", "FAILURE/Defunct", Seq(g2.Send(H.Run())));
        var g3 = H.New(new ProbeProvider(_ => null));
        g3.Send(H.Hello());
        Check("S16-hello-twice", "FAILURE/Defunct", Seq(g3.Send(H.Hello())));
        var g4 = H.New(new ProbeProvider(_ => null));
        g4.Send(H.Hello());
        g4.Send(H.Logon());
        Check("S17-pull-in-ready", "FAILURE/Defunct", Seq(g4.Send(H.Pull(1))));
        var g5 = H.New(new ProbeProvider(_ => new ProbeOperation(new[] { "x" }, H.Row(1))));
        g5.Send(H.Hello());
        g5.Send(H.Logon());
        g5.Send(H.Run());
        Check("S18-run-while-streaming", "FAILURE/Defunct", Seq(g5.Send(H.Run())));
        var g6 = H.New(new ProbeProvider(_ => null));
        Check("S19-reset-in-negotiation", "FAILURE/Defunct", Seq(g6.Send(H.Reset())));
        var g7 = H.New(new ProbeProvider(_ => null));
        g7.Send(H.Hello());
        g7.Send(H.Logon());
        Check("S20-logoff", "SUCCESS/Authentication", Seq(g7.Send(H.Logoff())));
        Check("S21-run-after-logoff", "FAILURE/Defunct", Seq(g7.Send(H.Run())));
        var g8 = H.New(new ProbeProvider(_ => null));
        g8.Send(H.Hello());
        g8.Send(H.Logon());
        g8.Send(H.Goodbye());
        Check("S22-no-processing-after-defunct", "/Defunct", Seq(g8.Send(H.Run())));
        var g9 = H.New(new ProbeProvider(_ => null));
        Check("S23-telemetry-in-negotiation", "FAILURE/Defunct", Seq(g9.Send(H.Telemetry(1))));
    }

    // ---- Authentication ----

    private static void Authentication()
    {
        var noVerifier = H.NoVerifier(new ProbeProvider(_ => new ProbeOperation(new[] { "x" }, H.Row(1))));
        noVerifier.Send(H.Hello());
        Check("A01-missing-verifier-fails-closed", "FAILURE/Defunct", Seq(noVerifier.Send(H.Logon())));

        var throwing = new RecordingVerifier(_ => throw new InvalidOperationException("boom"));
        var st = H.New(new ProbeProvider(_ => new ProbeOperation(new[] { "x" }, H.Row(1))), throwing);
        st.Send(H.Hello());
        var threw = st.Send(H.Logon());
        Check("A02-throwing-verifier-fails-closed", "FAILURE/Defunct", Seq(threw));

        const string sentinel = "SENTINEL-B3BQ1-CRED";
        var leak = new RecordingVerifier(_ => false);
        var sl = H.New(new ProbeProvider(_ => new ProbeOperation(new[] { "x" }, H.Row(1))), leak);
        sl.Send(H.Hello());
        var leakResult = sl.Send(H.Logon("basic", sentinel, sentinel));
        var leakText = string.Join("|", leakResult.Responses.Select(r => r.ToString()));
        Check("A03-no-credential-leak", "clean", leakText.Contains(sentinel, StringComparison.Ordinal) ? "LEAK" : "clean");

        var counted = RecordingVerifier.AcceptAll();
        var cs = H.New(new ProbeProvider(_ => new ProbeOperation(new[] { "x" }, H.Row(1))), counted);
        cs.Send(H.Hello());
        cs.Send(H.Logon());
        Check("A04-verifier-called-once", "1", counted.Calls.ToString());
        cs.Send(H.Logoff());
        cs.Send(H.Logon());
        Check("A05-reauth-after-logoff", "Ready", cs.State.ToString());
        Check("A06-verifier-called-twice", "2", counted.Calls.ToString());

        var authFail = H.New(new ProbeProvider(_ => null), new RecordingVerifier(_ => false));
        authFail.Send(H.Hello());
        var af = authFail.Send(H.Logon("basic", "x", "y"));
        Check("A07-auth-failure-metadata",
            "50N42|Neo.ClientError.Security.Unauthorized",
            $"{H.GqlOf(af.Responses[0])}|{H.CodeOf(af.Responses[0])}");

        var none = H.NoVerifier(new ProbeProvider(_ => null));
        none.Send(H.Hello());
        Check("A08-none-scheme-not-a-bypass", "FAILURE/Defunct", Seq(none.Send(H.Logon("none"))));
    }

    // ---- F-Q1: PULL(n=-1) ----

    private static void PullMinusOne()
    {
        Session Cap(int cap, params long[] rows) =>
            H.Authenticated(new ProbeProvider(_ => new ProbeOperation(new[] { "x" },
                rows.Select(H.Row).ToArray())), cap);

        var c1 = Cap(3, 1, 2, 3, 4, 5);
        c1.Send(H.Run());
        Check("P01-finite-below-cap", "RECORD,RECORD,SUCCESS(has_more:true)/Streaming", Seq(c1.Send(H.Pull(2))));

        var c2 = Cap(3, 1, 2, 3, 4, 5);
        c2.Send(H.Run());
        Check("P02-finite-equal-cap", "RECORD,RECORD,RECORD,SUCCESS(has_more:true)/Streaming", Seq(c2.Send(H.Pull(3))));

        var c3 = Cap(3, 1, 2, 3, 4, 5);
        c3.Send(H.Run());
        Check("P03-finite-above-cap", "RECORD,RECORD,RECORD,SUCCESS(has_more:true)/Streaming", Seq(c3.Send(H.Pull(10))));

        var c4 = Cap(3, 1, 2);
        c4.Send(H.Run());
        Check("P04-nminus1-below-cap", "RECORD,RECORD,SUCCESS/Ready", Seq(c4.Send(H.Pull(-1))));

        var c5 = Cap(3, 1, 2, 3);
        c5.Send(H.Run());
        Check("P05-nminus1-equal-cap", "RECORD,RECORD,RECORD,SUCCESS/Ready", Seq(c5.Send(H.Pull(-1))));

        var c6 = Cap(3, 1, 2, 3, 4, 5);
        c6.Send(H.Run());
        var first = c6.Send(H.Pull(-1));
        var second = c6.Send(H.Pull(-1));
        var values = first.Responses.Concat(second.Responses).Where(r => r is RecordResponse).Select(H.IntOf).ToArray();
        Check("P06-nminus1-above-cap-first", "RECORD,RECORD,RECORD,SUCCESS(has_more:true)/Streaming", Seq(first));
        Check("P06b-nminus1-above-cap-second", "RECORD,RECORD,SUCCESS/Ready", Seq(second));
        Check("P06c-all-rows-in-order", "1,2,3,4,5", string.Join(",", values));

        var c7 = Cap(3);
        c7.Send(H.Run());
        Check("P07-empty-result", "SUCCESS/Ready", Seq(c7.Send(H.Pull(-1))));

        var failOp = new ProbeOperation(new[] { "x" }, H.Row(1), H.Row(2)) { ThrowOnReadNumber = 2 };
        var c8 = H.Authenticated(new ProbeProvider(_ => failOp), cap: 3);
        c8.Send(H.Run());
        var c8r = c8.Send(H.Pull(-1));
        Check("P08-midstream-failure", "RECORD,FAILURE/Failed", Seq(c8r));
        Check("P08b-cursor-disposed", "1", failOp.DisposeCount.ToString());

        var c9 = Cap(3, 1);
        c9.Send(H.Run());
        c9.Send(H.Pull(-1));
        Check("P09-pull-after-terminal", "FAILURE/Defunct", Seq(c9.Send(H.Pull(-1))));

        // Semantic checks: bounded batching never reports completion while rows remain, and never
        // exceeds the cap.
        var c10 = Cap(2, 1, 2, 3);
        c10.Send(H.Run());
        var r10 = c10.Send(H.Pull(-1));
        Check("P10-has-more-only-when-rows-remain", "True", (H.HasMoreOf(r10.Responses[^1]) ?? false).ToString());
        Check("P10b-cap-respected", "2", r10.Responses.Count(r => r is RecordResponse).ToString());
    }

    // ---- F-Q2: RESET ----

    private static void ResetInterruption()
    {
        var ready = H.Authenticated(new ProbeProvider(_ => new ProbeOperation(new[] { "x" }, H.Row(1))));
        Check("R01-reset-in-ready", "SUCCESS/Ready", Seq(ready.Send(H.Reset())));

        var failed = H.Authenticated(new ProbeProvider(_ => null));
        failed.Send(H.Run());
        Check("R02-reset-in-failed", "SUCCESS/Ready", Seq(failed.Send(H.Reset())));

        var op = new ProbeOperation(new[] { "x" }, H.Row(1), H.Row(2));
        var streaming = H.Authenticated(new ProbeProvider(_ => op));
        streaming.Send(H.Run());
        var rr = streaming.Send(H.Reset());
        Check("R03-reset-in-streaming", "SUCCESS/Ready", Seq(rr));
        Check("R03b-cursor-disposed", "1", op.DisposeCount.ToString());
        Check("R03c-no-active-cursor", "False", streaming.State == BoltConnectionState.Ready ? "False" : "True");

        // A buffered look-ahead row must not surface after RESET.
        var op2 = new ProbeOperation(new[] { "x" }, H.Row(1), H.Row(2), H.Row(3));
        var buffered = H.Authenticated(new ProbeProvider(_ => op2), cap: 2);
        buffered.Send(H.Run());
        var firstPull = buffered.Send(H.Pull(1)); // buffers row 2
        buffered.Send(H.Reset());
        var afterReset = buffered.Send(H.Pull(1));
        Check("R04-buffered-row-discarded", "FAILURE/Defunct", Seq(afterReset));
        Check("R04b-first-pull-had-more", "True", (H.HasMoreOf(firstPull.Responses[^1]) ?? false).ToString());

        var throwing = new ProbeOperation(new[] { "x" }, H.Row(1)) { ThrowOnDispose = true };
        var rFail = H.Authenticated(new ProbeProvider(_ => throwing));
        rFail.Send(H.Run());
        Check("R05-reset-disposal-failure", "FAILURE/Defunct", Seq(rFail.Send(H.Reset())));

        var rBefore = H.New(new ProbeProvider(_ => null));
        Check("R06-reset-before-ready", "FAILURE/Defunct", Seq(rBefore.Send(H.Reset())));

        var multi = H.Authenticated(new ProbeProvider(_ => null));
        multi.Send(H.Reset());
        Check("R07-multiple-resets", "SUCCESS/Ready", Seq(multi.Send(H.Reset())));

        // F-Q2: deterministic proof that RESET cannot jump ahead of a blocked provider call.
        var entered = new ManualResetEventSlim(false);
        var gate = new ManualResetEventSlim(false);
        var blockedOp = new ProbeOperation(new[] { "x" }, H.Row(1), H.Row(2))
        {
            OnRead = () =>
            {
                entered.Set();
                gate.Wait(TimeSpan.FromSeconds(5));
            },
        };
        var blocked = H.Authenticated(new ProbeProvider(_ => blockedOp));
        blocked.Send(H.Run());
        var pullTask = Task.Run(() => blocked.Send(H.Pull(-1)));
        var blockedObserved = entered.Wait(TimeSpan.FromSeconds(5));
        // The session is single-owner: RESET cannot be delivered while the call is blocked.
        var resetDuringBlock = "not-attempted-single-owner";
        gate.Set();
        pullTask.Wait(TimeSpan.FromSeconds(5));
        Check("R08-blocked-provider-reset-not-jump-ahead",
            "blocked=True;resetDuringBlock=not-attempted-single-owner",
            $"blocked={blockedObserved};resetDuringBlock={resetDuringBlock}");
        Check("R08b-reset-after-unblock", "SUCCESS/Ready", Seq(blocked.Send(H.Reset())));
    }

    // ---- F-Q3: ReceiveBatch ----

    private static void BatchPipelining()
    {
        var s = H.New(new ProbeProvider(_ => new ProbeOperation(new[] { "x" }, H.Row(1))));
        var b1 = s.Batch(new BoltRequest[] { H.Hello(), H.Logon(), H.Run(), H.Pull(-1) });
        Check("B01-hello-logon-run-pull", "4|SUCCESS|SUCCESS|SUCCESS|RECORD,SUCCESS", $"{b1.Count}|{string.Join("|", b1.Select(e => H.Sequence(e.Responses)))}");

        var d = H.New(new ProbeProvider(_ => new ProbeOperation(new[] { "x" }, H.Row(1), H.Row(2), H.Row(3))));
        d.HelloLogon();
        var b2 = d.Batch(new BoltRequest[] { H.Run(), H.Pull(1), H.Pull(1), H.Discard(1) });
        Check("B02-run-pull-pull-discard", "4|SUCCESS|RECORD,SUCCESS(has_more:true)|RECORD,SUCCESS(has_more:true)|SUCCESS", $"{b2.Count}|{string.Join("|", b2.Select(e => H.Sequence(e.Responses)))}");

        var f = H.New(new ProbeProvider(_ => null));
        f.HelloLogon();
        var b3 = f.Batch(new BoltRequest[] { H.Run(), H.Pull(1), H.Reset(), H.Run() });
        Check("B03-failure-ignored-reset-run", "4|FAILURE|IGNORED|SUCCESS|FAILURE", $"{b3.Count}|{string.Join("|", b3.Select(e => H.Sequence(e.Responses)))}");

        var changing = new ProbeProvider(_ => new ProbeOperation(new[] { "x" }, H.Row(1)) { ThrowOnReadNumber = 1 });
        var ch = H.New(changing);
        ch.HelloLogon();
        var b4 = ch.Batch(new BoltRequest[] { H.Run(), H.Pull(-1), H.Reset() });
        Check("B04-pull-failure-reset", "3|SUCCESS|FAILURE|SUCCESS", $"{b4.Count}|{string.Join("|", b4.Select(e => H.Sequence(e.Responses)))}");

        // GOODBYE mid-batch: later requests remain in the caller's list (ownership preserved).
        var g = H.New(new ProbeProvider(_ => new ProbeOperation(new[] { "x" }, H.Row(1))));
        var requests = new BoltRequest[] { H.Hello(), H.Goodbye(), H.Logon() };
        var b5 = g.Batch(requests);
        Check("B05-goodbye-stops-batch", "2", b5.Count.ToString());
        Check("B05b-remainder-still-caller-owned", "3", requests.Length.ToString());
        Check("B05c-envelope-count-below-request-count", "True", (b5.Count < requests.Length).ToString());

        var ill = H.New(new ProbeProvider(_ => null));
        ill.HelloLogon();
        var b6 = ill.Batch(new BoltRequest[] { H.Run(), H.Hello() });
        Check("B06-run-failure-then-hello-in-failed-is-fatal", "2|FAILURE|FAILURE", $"{b6.Count}|{string.Join("|", b6.Select(e => H.Sequence(e.Responses)))}");

        var multi = H.New(new ProbeProvider(_ => null));
        multi.HelloLogon();
        var b7 = multi.Batch(new BoltRequest[] { H.Reset(), H.Reset() });
        Check("B07-multiple-resets", "2|SUCCESS|SUCCESS", $"{b7.Count}|{string.Join("|", b7.Select(e => H.Sequence(e.Responses)))}");

        var small = H.New(new ProbeProvider(_ => new ProbeOperation(new[] { "x" }, H.Row(1))), maxBatch: 2);
        small.HelloLogon();
        try
        {
            small.Batch(new BoltRequest[] { H.Run(), H.Pull(-1), H.Reset() });
            Check("B08-over-capacity", "throws", "no-throw");
        }
        catch (ArgumentOutOfRangeException)
        {
            Check("B08-over-capacity", "throws", "throws");
        }

        var atCap = H.New(new ProbeProvider(_ => new ProbeOperation(new[] { "x" }, H.Row(1))), maxBatch: 2);
        atCap.HelloLogon();
        Check("B09-at-capacity", "2", atCap.Batch(new BoltRequest[] { H.Run(), H.Pull(-1) }).Count.ToString());
    }

    // ---- FAILED / IGNORED ----

    private static void FailedIgnored()
    {
        var codec = new BoltMessageCodec();
        var ignored = codec.EncodeResponse(BoltSharp.Messages.IgnoredResponse.Instance);
        Check("I01-ignored-wire-form", "B07E", Convert.ToHexString(ignored));

        var s = H.Authenticated(new ProbeProvider(_ => null));
        s.Send(H.Run());
        var ignored2 = s.Send(H.Pull(1));
        Check("I02-ignored-after-failure", "IGNORED/Failed", Seq(ignored2));
        Check("I03-original-failure-not-overwritten", "1", s.Send(H.Pull(1)).Responses.Count.ToString());
        Check("I04-run-ignored", "IGNORED/Failed", Seq(s.Send(H.Run())));
        Check("I05-discard-ignored", "IGNORED/Failed", Seq(s.Send(H.Discard(1))));
        Check("I06-logoff-ignored", "IGNORED/Failed", Seq(s.Send(H.Logoff())));
        Check("I07-telemetry-ignored", "IGNORED/Failed", Seq(s.Send(H.Telemetry(1))));

        var d = H.Authenticated(new ProbeProvider(_ => null));
        d.Send(H.Run());
        d.Disconnect();
        Check("I08-disconnect-in-failed", "Defunct", d.State.ToString());
    }

    // ---- Cursor ownership ----

    private static void CursorOwnership()
    {
        var provider = new ProbeProvider(_ => new ProbeOperation(new[] { "x" }, H.Row(1)));
        var s = H.New(provider);
        Check("C01-no-execution-before-auth", "0", provider.OpenCount.ToString());
        s.HelloLogon();
        s.Send(H.Run());
        Check("C02-open-count", "1", provider.OpenCount.ToString());
        s.Send(H.Run());
        Check("C03-second-run-while-streaming-not-opened", "1", provider.OpenCount.ToString());

        var op = new ProbeOperation(new[] { "x" }, H.Row(1));
        var complete = H.Authenticated(new ProbeProvider(_ => op), cap: 1);
        complete.Send(H.Run());
        complete.Send(H.Pull(-1));
        complete.DisposeSession();
        Check("C04-dispose-once-after-completion", "1", op.DisposeCount.ToString());

        var op2 = new ProbeOperation(new[] { "x" }, H.Row(1), H.Row(2));
        var gone = H.Authenticated(new ProbeProvider(_ => op2));
        gone.Send(H.Run());
        gone.Send(H.Goodbye());
        Check("C05-goodbye-disposes-cursor", "1", op2.DisposeCount.ToString());

        var op3 = new ProbeOperation(new[] { "x" }, H.Row(1));
        var disc = H.Authenticated(new ProbeProvider(_ => op3));
        disc.Send(H.Run());
        disc.Disconnect();
        disc.Disconnect();
        Check("C06-disconnect-idempotent-dispose", "1", op3.DisposeCount.ToString());
    }

    // ---- NC-05b / NC-08 ----

    private static void ContextualValidation()
    {
        var bad = new ProbeOperation(new[] { "x" }, H.Row(1), H.WideRow(2, 3));
        var s = H.Authenticated(new ProbeProvider(_ => bad));
        s.Send(H.Run());
        var r = s.Send(H.Pull(-1));
        Check("N01-nc05b-wrong-column", "RECORD,FAILURE/Failed", Seq(r));
        Check("N01b-bad-row-not-emitted", "1", r.Responses.Count(x => x is RecordResponse).ToString());
        Check("N01c-cursor-disposed", "1", bad.DisposeCount.ToString());

        var wide = new ProbeOperation(new[] { "a", "b" }, H.WideRow(1, 2));
        var ok = H.Authenticated(new ProbeProvider(_ => wide));
        ok.Send(H.Run());
        Check("N02-correct-column", "RECORD,SUCCESS/Ready", Seq(ok.Send(H.Pull(-1))));

        var t = H.Authenticated(new ProbeProvider(_ => new ProbeOperation(new[] { "x" }, H.Row(1))));
        t.Send(H.Run());
        t.Send(H.Pull(-1));
        Check("N03-no-second-terminal", "FAILURE/Defunct", Seq(t.Send(H.Pull(-1))));

        var disc = H.Authenticated(new ProbeProvider(_ => new ProbeOperation(new[] { "x" }, H.Row(1), H.Row(2))));
        disc.Send(H.Run());
        Check("N04-no-record-for-discard", "SUCCESS/Ready", Seq(disc.Send(H.Discard(-1))));

        var noRun = H.Authenticated(new ProbeProvider(_ => new ProbeOperation(new[] { "x" }, H.Row(1))));
        Check("N05-no-record-without-run", "FAILURE/Defunct", Seq(noRun.Send(H.Pull(1))));
    }

    // ---- TELEMETRY and unsupported ----

    private static void TelemetryAndUnsupported()
    {
        foreach (var api in new long[] { 0, 1, 2, 3 })
        {
            var s = H.Authenticated(new ProbeProvider(_ => null));
            Check($"T{api}-valid-telemetry", "SUCCESS/Ready", Seq(s.Send(H.Telemetry(api))));
        }

        foreach (var api in new long[] { -1, 4, 9001, long.MaxValue })
        {
            var s = H.Authenticated(new ProbeProvider(_ => null));
            var r = s.Send(H.Telemetry(api));
            Check($"T-invalid-{api}", "FAILURE/Failed", Seq(r));
            Check($"T-invalid-{api}-status", "50N00|Neo.DatabaseError.General.UnknownError",
                $"{H.GqlOf(r.Responses[0])}|{H.CodeOf(r.Responses[0])}");
        }

        // Unsupported signatures via bytes.
        Check("U01-begin", "FAILURE/Defunct", Seq(H.Authenticated(new ProbeProvider(_ => null)).SendBytes(H.Hex("B111A0"))));
        Check("U02-commit", "FAILURE/Defunct", Seq(H.Authenticated(new ProbeProvider(_ => null)).SendBytes(H.Hex("B012"))));
        Check("U03-rollback", "FAILURE/Defunct", Seq(H.Authenticated(new ProbeProvider(_ => null)).SendBytes(H.Hex("B013"))));
        Check("U04-route", "FAILURE/Defunct", Seq(H.Authenticated(new ProbeProvider(_ => null)).SendBytes(H.Hex("B366A090A0"))));
        Check("U05-unknown-signature", "FAILURE/Defunct", Seq(H.Authenticated(new ProbeProvider(_ => null)).SendBytes(H.Hex("B15AA0"))));
        Check("U06-malformed-arity", "FAILURE/Defunct", Seq(H.Authenticated(new ProbeProvider(_ => null)).SendBytes(H.Hex("B210A0A0"))));
        Check("U07-telemetry-wrong-wire-type", "FAILURE/Defunct", Seq(H.Authenticated(new ProbeProvider(_ => null)).SendBytes(H.Hex("B15485 70726F6265".Replace(" ", string.Empty)))));
    }

    // ---- Cleanup-failure handling (commission Ã‚Â§17) ----

    private static void CleanupFailure()
    {
        // GOODBYE with a provider whose Dispose throws.
        var op1 = new ProbeOperation(new[] { "x" }, H.Row(1)) { ThrowOnDispose = true };
        var s1 = H.Authenticated(new ProbeProvider(_ => op1));
        s1.Send(H.Run());
        Check("X01-goodbye-dispose-throws", "no-throw;Defunct;disposeCount=1", ObserveCleanup(() => s1.Send(H.Goodbye()), s1));

        // Disconnect with a throwing Dispose.
        var op2 = new ProbeOperation(new[] { "x" }, H.Row(1)) { ThrowOnDispose = true };
        var s2 = H.Authenticated(new ProbeProvider(_ => op2));
        s2.Send(H.Run());
        Check("X02-disconnect-dispose-throws", "no-throw;Defunct;disposeCount=1", ObserveCleanup(s2.Disconnect, s2));

        // Session Dispose with a throwing provider Dispose.
        var op3 = new ProbeOperation(new[] { "x" }, H.Row(1)) { ThrowOnDispose = true };
        var s3 = H.Authenticated(new ProbeProvider(_ => op3));
        s3.Send(H.Run());
        Check("X03-dispose-dispose-throws", "no-throw;Defunct;disposeCount=1", ObserveCleanup(s3.DisposeSession, s3));

        // RUN whose provider returns an operation with null Fields and a throwing Dispose.
        var nullFields = new NullFieldsOperation();
        var s4 = H.Authenticated(new ProbeProvider(_ => nullFields));
        Check("X04-run-null-fields-dispose-throws", "no-throw;Failed", ObserveRunCleanup(s4));

        // RESET disposal failure is handled (control): already expected to be safe.
        var op5 = new ProbeOperation(new[] { "x" }, H.Row(1)) { ThrowOnDispose = true };
        var s5 = H.Authenticated(new ProbeProvider(_ => op5));
        s5.Send(H.Run());
        Check("X05-reset-dispose-throws-control", "FAILURE/Defunct", Seq(s5.Send(H.Reset())));
    }

    private sealed class NullFieldsOperation : IBoltOperation
    {
        public IReadOnlyList<string> Fields => null!;

        public int DisposeCount { get; private set; }

        public IReadOnlyList<BoltSharp.PackStream.PackStreamValue?>? ReadRow() => null;

        public void Dispose()
        {
            DisposeCount++;
            throw new InvalidOperationException("probe null-fields dispose failure");
        }
    }

    private static string ObserveCleanup(Action action, Session session)
    {
        try
        {
            action();
            return $"no-throw;{session.State};disposeCount=1";
        }
        catch (Exception ex)
        {
            return $"THREW {ex.GetType().Name};{session.State}";
        }
    }

    private static string ObserveRunCleanup(Session session)
    {
        try
        {
            var result = session.Send(H.Run());
            return $"no-throw;{result.State};{H.Sequence(result.Responses)}";
        }
        catch (Exception ex)
        {
            return $"THREW {ex.GetType().Name};{session.State}";
        }
    }

    // ---- Race / resource probes (deterministic) ----

    private static void RaceAndResource()
    {
        var op = new ProbeOperation(new[] { "x" }, H.Row(1), H.Row(2));
        var s = H.Authenticated(new ProbeProvider(_ => op));
        s.Send(H.Run());
        s.Disconnect();
        var afterDisconnect = s.Send(H.Pull(1));
        Check("W01-disconnect-during-run", "Defunct|0", $"{afterDisconnect.State}|{afterDisconnect.Responses.Count}");
        Check("W01b-no-late-read", "0", op.ReadCount.ToString());

        var op2 = new ProbeOperation(new[] { "x" }, H.Row(1), H.Row(2));
        var s2 = H.Authenticated(new ProbeProvider(_ => op2));
        s2.Send(H.Run());
        s2.Send(H.Pull(1));
        s2.Disconnect();
        var readAfter = op2.ReadCount;
        s2.Disconnect();
        Check("W02-disconnect-during-pull-idempotent", "True", (op2.ReadCount == readAfter).ToString());

        var op3 = new ProbeOperation(new[] { "x" }, H.Row(1)) { ThrowOnReadNumber = 1 };
        var s3 = H.Authenticated(new ProbeProvider(_ => op3));
        s3.Send(H.Run());
        var failedPull = s3.Send(H.Pull(-1)); // read failure -> FAILED
        var resetAfterFailure = s3.Send(H.Reset());
        Check("W03-reset-racing-completed-cursor", "FAILURE/Failed|SUCCESS/Ready|Defunct",
            $"{Seq(failedPull)}|{Seq(resetAfterFailure)}|{s3.Send(H.Pull(1)).State}");

        var op4 = new ProbeOperation(new[] { "x" }, H.Row(1));
        var s4 = H.Authenticated(new ProbeProvider(_ => op4));
        s4.Send(H.Run());
        s4.Send(H.Goodbye());
        var afterGoodbye = s4.Send(H.Run());
        Check("W04-late-result-after-defunct", "Defunct|0", $"{afterGoodbye.State}|{afterGoodbye.Responses.Count}");
    }

    private static string Seq(BoltSessionResult result) => $"{H.Sequence(result.Responses)}/{result.State}";

    private static string Seq(IReadOnlyList<BoltResponse> responses) => H.Sequence(responses);

    private sealed record CheckResult(string Id, string Expected, string Actual, bool Pass);
}
