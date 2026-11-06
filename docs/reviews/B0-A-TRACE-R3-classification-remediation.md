# B0-A-TRACE-R3 — Context-aware connection-termination classification remediation

**Disposition: B0-A-TRACE-R3 COMPLETE — READY FOR TARGETED INDEPENDENT Q4.** This is a producer
remediation record, not owner acceptance and not a Q4 requalification.

**Executor:** DeepSeek V4.1 Flash in OpenCode.
**Date:** 2026-11-06 (execution timestamps are UTC).
**Repository:** `D:\projects\bolt-sharp`, canonical `main`.
**Owner authorization:** *B0-A-TRACE-R3 REMEDIATION AUTHORIZED* (narrowly scoped correction to
test-only listener behaviour; no production transport, no `src/BoltSharp` change, no push).

---

## 1. Baseline and repository preservation

| Identity | Commit | Tree |
| --- | --- | --- |
| Entry baseline (canonical `main`) | `81f12f9d1f0c274d07431e2543f5ddb58e2e733a` | `f079c054e12fa93204c53e722f684e4a7bc773d9` |
| Implementation commit | `a79a28f29f060703d90adfbbb3d7f558576f7df3` | `dc979880ac0f4e748e4df4ae7530d9d4f09beb43` |

Before any write the checkout was clean and on `main`, at the pinned HEAD/tree, with exactly one
worktree (`D:/projects/bolt-sharp`) and the owner-designated exclusive write role. No Git lock,
BoltSharp testhost or evidence runner was active; no competing writer was observed. No branch,
worktree, reset, rebase, clean, prune or push was performed. No file under `src/BoltSharp`
changed (`git status --porcelain -- src/` is empty). Historical Q1/Q2/Q3 review artifacts, probes
and authentic handshake bytes were not modified. `TRACEQ1-F01` remains closed and was not
reopened.

The evidence commit is the commit containing this report and
`docs/reviews/evidence/B0-A-TRACE-R3/`; because a file cannot contain its own commit hash, that
identity is recorded in the final handoff message.

## 2. Root-cause analysis

The Q3 reviewer left one decisive classification counterexample and one inconclusive disposal
origin.

**TRACEQ3-F01 (decisive).** `ObserveFirstMessageAsync` and `ObserveAdditionalMessagesAsync` each
applied the same six-code "expected closure" set
(`ConnectionReset`, `ConnectionAborted`, `OperationAborted`, `NotConnected`, `Shutdown`,
`Disconnecting`) and assigned `CLEAN_EOF` without checking execution context. The exact code was:

```csharp
catch (Exception ex) when (IsExpectedPeerClosure(ex))
{
    observation.Status = "CLEAN_EOF";   // <-- reset/abort turned into a clean EOF
    observation.FramingValid = false;
    observation.ErrorType = ex.GetType().FullName;
    observation.SanitizedError = Sanitize(ex.Message);
}
```

A thrown socket exception is an exceptional termination; it is not a read returning zero. The
branch therefore conflated exceptional termination with successful stream completion, and the
additional-message path repeated the same assignment. `CLEAN_EOF` must mean only a genuine
zero-byte read at a valid protocol boundary.

**TRACEQ2-F02 (inconclusive).** The `IsLocalDisposal` predicate maps any direct or `IOException`-
wrapped `ObjectDisposedException` to `CANCELLED` at the framed-read boundary from exception type
alone, without consulting connection lifecycle state. It cannot by itself prove that the disposed
resource is listener-owned. Q3 assessed the actual listener-owned shutdown case as consistent with
`CANCELLED` but the arbitrary-origin case as unproven.

## 3. Classification contract (invariants documented and enforced)

| Status | Permitted meaning | Enforced rule |
| --- | --- | --- |
| `CLEAN_EOF` | An actual zero-byte read representing normal stream completion at a valid protocol boundary. | Produced **only** by the `body is null` branch of `BoltMessageReader.ReadMessageAsync`, for both the first and additional messages. No exception path can reach it. An EOF inside an incomplete frame remains `TRUNCATED`. |
| `CLOSED` | Connection closure established at the outer negotiation/acquisition boundary, where the documented listener policy treats closure as an expected reached condition. | Unchanged. Not produced for framed-read exceptions (there is no such status in the post-handshake taxonomy). Network-level faults never map here. |
| `CANCELLED` | Local cancellation, listener shutdown, or locally owned disposal is established. | At the framed-read boundary a reset/abort maps to `CANCELLED` **only** when lifecycle state establishes local teardown. An `ObjectDisposedException` remains the non-clean, non-error local/stream closure signature. Locally initiated cancellation is **not** inferred from the exception type alone. |
| `ERROR` | Unexpected network, IO, lifecycle or resource failure. | The default when benign termination cannot be established. An unconfirmed reset/abort, an unexpected `IOException`, a wrapped network fault, and every unrecognised fault are `ERROR`. |

Enforcement points: stream acquisition, handshake negotiation, first framed read, additional
framed reads, cancellation handling, listener disposal, endpoint diagnostic capture. The first and
additional framed-read boundaries now share one classifier, so they cannot diverge.

## 4. Implementation (minimal correction)

Only three test files changed; `src/BoltSharp` is untouched.

1. **One shared classifier.** `ClassifyFramedReadFailure(Exception, bool localTeardownRequested)`
   is used by both framed-read boundaries. It never returns `CLEAN_EOF`; it returns `CANCELLED`
   for established local teardown, `ERROR` for an unconfirmed closure, and `ERROR` for everything
   else.
2. **Lifecycle context.** `IsLocalTeardownRequested(TcpClient, CancellationToken)` establishes
   local teardown from available connection state: the listener shutdown token, the per-connection
   lifetime/cancellation token, or an already-closed listener-owned client (`TcpClient.Client is
   null`, TRACEQ1-F01). This replaces inferring local cancellation from the exception type.
3. **First framed read.** The three disposal/closure/generic catches were collapsed into one
   classifier call. `CLEAN_EOF` is only the `body is null` branch; genuine `TRUNCATED` and
   `FORMAT_ERROR` behaviour is unchanged; the `TIMED_OUT`/`CANCELLED` cancellation branches are
   unchanged.
4. **Additional framed read.** The disposal/closure catches were replaced by one filtered catch
   using the same classifier; unexpected non-closure faults are left to propagate to the
   first-message handler, preserving the established `ERROR` observation outcome. `Add` now also
   records the additional message's `ErrorType`.
5. **No broad swallowing, no new taxonomy, no timing delays, no new exception hierarchy.** The
   outer negotiation boundary keeps its documented `CLOSED` reached-closure outcome (Q13), with a
   comment recording that only the framed-read boundary is where an exception must never be
   invented as `CLEAN_EOF`.

Source-change inventory (blob identities):

| File | Pre-R3 blob | R3 blob |
| --- | --- | --- |
| `tests/.../HandshakeTrace/LoopbackHandshakeListener.cs` | `24d530498aabaa0ec5d1275ad040a6e984afbe43` | `7017a51ffa2fe693a0e5fd37d5aa3a9492cdf6bf` |
| `tests/.../HandshakeTrace/ListenerFaultClassificationRegressionTests.cs` | `6b35503588887ca800dc6910e2933a13b6748e40` | `9a37e04da40362866ea87af8d4d23ea117a5d174` |
| `tests/.../HandshakeTrace/ListenerClassificationR3RegressionTests.cs` | (new) | `3888b5888407f050fc5e1a5792616c1eb564cb9c` |

## 5. Finding-to-fix-to-test traceability

| Finding | Fix | Regression / probe | Before-fix | After-fix |
| --- | --- | --- | --- | --- |
| TRACEQ3-F01 reset-as-CLEAN_EOF (first read) | shared classifier; exception never takes the `CLEAN_EOF` branch | `R3_04_...` (6 closure codes); retained Q2 `Q23` | `CLEAN_EOF` | `ERROR` |
| TRACEQ3-F01 reset-as-CLEAN_EOF (additional read) | same classifier on the additional boundary | `R3_05_...`; R2 `UnexpectedWrappedSocketFault_DuringAdditionalFramedRead_IsError` | `CLEAN_EOF` | `ERROR` |
| genuine clean EOF | unchanged `body is null` branch | `R3_01_...`, `R3_02_...`; R2 `GenuineCleanEof_...` | `CLEAN_EOF` | `CLEAN_EOF` |
| truncation preserved | unchanged `BoltFramingTruncatedException` branch | `R3_03_...`; R2 `GenuinelyTruncatedFrame_...` | `TRUNCATED` | `TRUNCATED` |
| unexpected IO / wrapped network fault | unchanged generic `ERROR` | `R3_06_...`, `R3_07_...`; Q2 `Q15`, `Q23` | `ERROR` | `ERROR` |
| TRACEQ2-F02 disposal origin | lifecycle state decides local teardown; ODE stays the documented non-clean local closure | `R3_09_...`; Q2 `Q21`; R2 `ObjectDisposed_...` | `CANCELLED` (type only) | `CANCELLED` (lifecycle-aware) |
| local shutdown + socket closure | lifecycle-established `CANCELLED` | `R3_08_...`; Q2 `Q24`; R1 shutdown suite | `CLEAN_EOF` | `CANCELLED` |
| no exception escapes disposal | unchanged disposal guards | `R3_10_...`; R1 suite | pass | pass |

## 6. Before-fix negative control

The byte-faithful pre-R3 listener snapshot
`docs/reviews/evidence/B0-A-TRACE-R3/controls/pre-r3-listener.cs` (Git blob
`24d530498aabaa0ec5d1275ad040a6e984afbe43`, identical to the entry `HEAD` blob) was linked into
the R3 probe host and the R3 regression source was run against it:

| Run | Result | Observed |
| --- | --- | --- |
| pre-R3 listener (`r3-negative`) | **7 passed / 8 failed**, exit 1 | the 6 closure-code first-read cases yield `CLEAN_EOF`; the additional reset yields `CLEAN_EOF`; local-shutdown reset yields `CLEAN_EOF` |
| remediated listener (`r3-live`) | **15 passed / 0 failed**, exit 0 | `ERROR`/`CANCELLED` as specified |

This is a controlled injected-fault negative control against the pre-remediation source. It does
not claim a real network outage.

## 7. After-fix regression results

All counts are actual results from this run. Overlapping suites are not summed.

| Suite | Expected baseline | Actual | Exit | Evidence |
| --- | --- | --- | ---: | --- |
| Full solution | 826 passed | **826 passed / 0 failed** | 0 | `trx/solution.trx` |
| Driver project | 60 passed, 10 gated | **75 passed / 0 failed / 10 gated** | 0 | `trx/driver.trx` |
| B2-A-Q2 | 18 passed | **18 passed** | 0 | `trx/b2a-q2.trx` |
| B2-B-Q1 | 67 passed | **67 passed** | 0 | `trx/b2b-q1.trx` |
| B2-B-Q2 | 46 passed | **46 passed** | 0 | `trx/b2b-q2.trx` |
| B2-C-Q2 | 40 passed | **40 passed** | 0 | `trx/b2c-q2.trx` |
| TRACE-Q1 | 14 passed | **14 passed** | 0 | `trx/q1.trx` |
| Retained TRACE-Q2 decisive probes | 26 passed | **26 passed / 0 failed** | 0 | `trx/q2-retained.trx` |
| GetStream control | 2 passed | **2 passed** | 0 | `trx/getstream-control.trx` |
| R1 shutdown regressions | 10 passed | **10 passed** | 0 | `trx/r1-shutdown.trx` |
| R2 classification regressions | 13 passed | **13 passed** | 0 | `trx/r2-classification.trx` |
| **R3 classification regressions (new)** | — | **15 passed / 0 failed** | 0 | `trx/r3-classification.trx` |
| Authentic handshake probes | 2 passed | **2 passed** | 0 | `trx/authentic.trx` |
| Q3 reviewer probes (retained, unmodified) | 16 passed on the pre-R3 listener | **10 passed / 6 failed** | 1 | `trx/q3-retained-r3.trx` |

The driver total is 60 baseline + 15 new R3 tests. D01–D10 remain gated (`NotExecuted` in
`trx/driver.trx`). The full solution is unchanged because the driver project is deliberately not
part of `BoltSharp.slnx`.

### 7.1 Q3 retained probes — historical-behaviour tests reported separately

The retained `docs/reviews/probes/B0-A-TRACE-Q3/IndependentClassification.cs` was rerun against
the remediated listener without modifying its assertions. Ten tests pass, including the previously
decisive `UnexpectedSocketAndWrappedSocketAreErrors` (5),
`UnexpectedIoAndNestedIoAreErrors`, `ArbitraryDisposedObjectIsCancelledWithoutToken`, and
`RealPeerResetIsReportedAsCleanEof`.

Six tests now fail, and **must** fail on a correct implementation because they intentionally assert
the pre-remediation historical behaviour:

`ClosureCodesBecomeCleanEofEvenWithoutCancellation` ×
{`ConnectionReset`, `ConnectionAborted`, `OperationAborted`, `NotConnected`, `Shutdown`,
`Disconnecting`} — each asserts `CLEAN_EOF`, which is exactly the defect TRACEQ3-F01 requires us
to remove. Their source names them as defect documentation ("BecomeCleanEofEvenWithoutCancellation").
They are reported separately and are **not** modified.

## 8. Historical Q3 preservation

Unchanged and still committed at their original identity: Q1 qualification evidence and probes,
Q2 qualification evidence and probes (`docs/reviews/probes/B0-A-TRACE-Q2`, including
`LifecycleProbes.cs`), Q3 reviewer evidence and probes
(`docs/reviews/probes/B0-A-TRACE-Q3`, `docs/reviews/evidence/B0-A-TRACE-Q3`), and the prior R1/R2
remediation records and accepted B1/B2 code and evidence. The R3 work is additive.

One producer assertion was corrected, not preserved: the R2 regression
`ExpectedPeerReset_DuringFramedRead_RemainsCleanEof` asserted the pre-R3 `CLEAN_EOF` behaviour that
TRACEQ3-F01 overturned. It is renamed `ExpectedPeerReset_DuringFramedRead_IsErrorNotCleanEof` and
now asserts `ERROR`/`NotEqual CLEAN_EOF`, keeping the R2 suite at 13 tests. The prior expectation
is recorded verbatim in §5 and the Q3 report; the historical R2 evidence files are untouched.

## 9. Authentic handshake hash verification

The authentic driver run regenerated the four handshake files under
`docs/reviews/evidence/B0-A-TRACE-R3/authentic/`; all four SHA-256 values are unchanged from the
accepted Q2/Q3 audit (`authentic-handshake-hashes.json`):

| File | SHA-256 | Bytes |
| --- | --- | ---: |
| `driver-manifest-client-handshake.hex` | `0CD1BABC841371031AA2EE228A7CCAAF233D32428C60BB7F16F3D37210A39A5C` | 76 |
| `driver-manifest-server-response.hex` | `52445611830D9D51BA7D8E56E9F9923E5C804A3DAB388248B0C87918E9DEC0AC` | 31 |
| `driver-legacy-client-handshake.hex` | `9BAB968DED8B229BEE87BBEEEC39E26C096EE6ACB39D4A6FE848419E7E17AE13` | 61 |
| `driver-legacy-server-response.hex` | `C35413A73CD1EBB50AF8BF14CC07FA8BE069902E8FB30FCDB32E52483D4E9AB0` | 13 |

No HELLO/LOGON compatibility or session behaviour is claimed.

## 10. Evidence hygiene

`docs/reviews/evidence/B0-A-TRACE-R3/` contains: `manifest.json` (every committed evidence file
except the manifest itself, with SHA-256 and byte counts), `commands.json` (every command with its
working directory, absolute arguments and exit code), `environment.txt`, the pre-R3 and GetStream
hook control sources, `authentic-handshake-hashes.json`, the addendum, per-suite `.txt` logs and
`trx/` results. The two earlier R2 evidence limitations are recorded without rewriting the
historical evidence:

1. The R2 manifest omitted `authentic-handshake-hashes.json`; the R3 manifest includes it.
2. The R2 GetStream build command used a repository-root-relative `ListenerSource` and failed
   with `CS2001` from the repository root; every R3 command uses an absolute or project-relative
   path that reproduces from `D:\projects\bolt-sharp`.

See `docs/reviews/evidence/B0-A-TRACE-R3/evidence-addendum.md`. Manifest hashes were verified
against the committed blobs after the evidence commit.

## 11. Remaining limitations

- Test-only Windows 10.0.26200 / net8.0. Socket faults are injected deterministically; no real OS
  network outage or resource exhaustion was induced.
- TRACEQ2-F02 remains bounded. A framed-read `ObjectDisposedException` with no established local
  teardown is still reported as `CANCELLED` (the accepted Q2 contract requires it not be `ERROR`),
  so an unrelated disposal origin is not provably distinguishable at that boundary. The listener
  now decides local teardown from lifecycle state where it has that context; the ownership
  assumption (the listener owns the only stream/socket on a connection) is documented in source.
- The accepted closure set is finite; unknown or network-level socket codes default to `ERROR` —
  the safe direction for TRACEQ3-F01.
- Race coverage is bounded; `R3_10` and the R1 suite assert bounded, exception-free disposal, not
  every schedule.
- No owner acceptance, no push, no B3/B4/B5 work; Q4 remains the independent judge.

## 12. Commit and tree identities

| Role | Commit | Tree |
| --- | --- | --- |
| Entry baseline | `81f12f9d1f0c274d07431e2543f5ddb58e2e733a` | `f079c054e12fa93204c53e722f684e4a7bc773d9` |
| Implementation | `a79a28f29f060703d90adfbbb3d7f558576f7df3` | `dc979880ac0f4e748e4df4ae7530d9d4f09beb43` |
| Evidence (this report + evidence + R3 probe host) | recorded in the final handoff message | recorded in the final handoff message |

**Final disposition: B0-A-TRACE-R3 COMPLETE — READY FOR TARGETED INDEPENDENT Q4.**
