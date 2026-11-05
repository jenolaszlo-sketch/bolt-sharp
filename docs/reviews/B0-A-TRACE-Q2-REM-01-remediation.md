# B0-A-TRACE-Q2-REM-01 — Socket-failure and framed-read trace classification remediation

**Disposition: Q2 REMEDIATED — READY FOR INDEPENDENT Q3.**

**Executor:** GLM Flash in OpenCode.
**Date:** 2026-11-06 (execution timestamps are UTC on 2026-11-05/06).
**Repository:** `D:\projects\bolt-sharp`, canonical `main`.
**Scope:** B0-A-TRACE Q2 findings only. No push, no branches, no worktrees, no history rewrite.

---

## 1. Baseline and repository preservation

| Identity | Commit | Tree |
| --- | --- | --- |
| Entry baseline (canonical `main`) | `8c4c6b9e8f8aa5bc53090067ffd22b4fe0084201` | `3e688553da26542704ba23c56a24d9a8a4f5d248` |
| Implementation commit | `0766bbdacea4955d445b007c0d83ead91ea54a21` | `62b04ebb4abc6774674ed232e0f0632306236950` |

Before any write the checkout was clean, on `main`, at the pinned HEAD/tree, with exactly one
worktree and no Git lock. No BoltSharp testhost or evidence runner was active. An unrelated
`dotnet test` process belonged to a different repository and did not modify this checkout. No
concurrent BoltSharp writer was observed. No branch, worktree, reset, rebase, clean, prune or push
was performed. Historical Q1/Q2 review artifacts, probes and authentic handshake files were not
modified. `TRACEQ1-F01` remains closed and was not reopened.

The evidence commit is the commit containing this report and
`docs/reviews/evidence/B0-A-TRACE-R2/`; its identity is recorded in the final handoff message
because this file cannot contain its own commit hash.

## 2. Findings addressed

| Finding | Severity | Q2 probes | Disposition here |
| --- | --- | --- | --- |
| `TRACEQ2-F01` | **MAJOR** | Q14 ×3, Q15, Q20 GetStream control, Q23 | Corrected |
| `TRACEQ2-F02` | MINOR | Q21 | Corrected |

The six failing Q2 probes were:

1. `Q14_UnexpectedSocketFault_MustRemainError(code: NetworkDown)`
2. `Q14_UnexpectedSocketFault_MustRemainError(code: NetworkUnreachable)`
3. `Q14_UnexpectedSocketFault_MustRemainError(code: NoBufferSpaceAvailable)`
4. `Q15_UnexpectedWrappedSocketFault_MustRemainError`
5. `Q21_ObjectDisposedDuringFramedRead_IsExpectedClosure`
6. `Q23_UnexpectedWrappedFramedSocketFault_MustRemainError`

The failing GetStream control was `Q20_UnexpectedAcquisitionSocketFault_MustRemainError`.

## 3. Failure-to-source mapping

All failures shared one classification defect: the listener classified by exception **type** at
three different boundaries instead of by the lifecycle cause. The boundaries required separate
corrections because they own different trace fields.

| Probe | Injection point / transport event | Lifecycle phase | Expected | Baseline actual | Losing method (baseline `LoopbackHandshakeListener.cs`) | Trace-contract requirement |
| --- | --- | --- | --- | --- | --- | --- |
| Q14 ×3 | `SocketException` NetworkDown / NetworkUnreachable / NoBufferSpaceAvailable from the handshake read | Negotiation read | `ERROR` | `CLOSED` | `HandleConnectionAsync` outer `catch (SocketException ex)` → unconditional `CLOSED` (baseline lines 414–419) | Unexpected transport faults must remain `ERROR`, not ordinary closure |
| Q15 | `IOException` wrapping `SocketException(NetworkDown)` from the handshake read | Negotiation read | `ERROR` | `CLOSED` | `HandleConnectionAsync` outer `catch (IOException ex)` → unconditional `CLOSED` (baseline lines 420–425) | Same; wrapped inner socket faults must not be erased |
| Q20 | `SocketException(NetworkDown)` from the stream-acquisition call before negotiation | Stream acquisition | `ERROR` | `CLOSED` | Acquisition guard `catch (... SocketException ...)` → unconditional `CLOSED` (baseline lines 344–351) | Acquisition may be `CLOSED` only for verified local/peer closure |
| Q23 | `IOException` wrapping `SocketException(NetworkDown)` from the framed post-handshake read | Framed reading | `ERROR` | `CLEAN_EOF` | `ObserveFirstMessageAsync` `catch (IOException ex)` → unconditional `CLEAN_EOF` (baseline lines 534–540); the additional-message path did the same at baseline lines 610–615 | Unexpected framed-read faults must remain `ERROR`; `CLEAN_EOF` is only clean peer EOF |
| Q21 | `ObjectDisposedException` from the framed post-handshake read | Framed reading | Not `ERROR`; local closure | `ERROR` | `ObserveFirstMessageAsync` had no disposal catch; the generic `catch (Exception ex)` → `ERROR` (baseline lines 541–547) | An expected local disposal is a reached closure outcome, not an unexpected fault |

The accepted shutdown/trace contract uses `CLOSED` for reached connection closure, `CLEAN_EOF`
only for a genuine clean peer end of stream, and `CANCELLED` for local shutdown/disposal during
observation. `ERROR` is reserved for unexpected faults. The Q21 oracle asserts only “not `ERROR`”;
this remediation classifies a framed-read disposal as `CANCELLED` (local closure), explicitly not
`CLEAN_EOF` and not `ERROR`.

## 4. Before-fix failing evidence

Captured against the unmodified baseline `8c4c6b9` before any source change:

| Run | Result | Evidence |
| --- | --- | --- |
| Retained independent Q2 probes | **20 passed / 6 failed**, exit 1 | `docs/reviews/evidence/B0-A-TRACE-R2/baseline/q2-baseline.txt`, `baseline/q2-baseline.trx` |
| GetStream control (historical baseline hook snapshot) | **1 passed / 1 failed**, exit 1; Q20 = `CLOSED` | `baseline/q2-hook-baseline.txt`, `baseline/q2-hook-baseline.trx` |

The Q2 reviewer probe source was not edited. The baseline hook run used the retained
`docs/reviews/evidence/B0-A-TRACE-Q2/negative-control/GetStreamHookListener.cs` byte-faithful
baseline snapshot.

## 5. Production changes

Only `tests/BoltSharp.DriverIntegrationTests/HandshakeTrace/LoopbackHandshakeListener.cs` changed
in production/test-listener behaviour, plus a new producer regression test file. `src/BoltSharp`
and all accepted protocol code are untouched.

1. **Acquisition guard narrowed.** `IsExpectedAcquisitionClosure` maps only `ObjectDisposedException`
   (including an `IOException` wrapping one), `NullReferenceException`, `InvalidOperationException`,
   and expected peer/local socket closure codes to `CLOSED`. A `SocketException(NetworkDown)` or
   `IOException` wrapping one now falls through to the generic `ERROR` handler (Q20).
2. **Outer negotiation/observation catches narrowed.** The unconditional `SocketException` and
   `IOException` → `CLOSED` catches were replaced with one expected-closure filter:
   - local disposal (`ObjectDisposedException`, including wrapped), or
   - `ConnectionReset`, `ConnectionAborted`, `OperationAborted`, `NotConnected`, `Shutdown`,
     `Disconnecting`.

   Network-level faults remain unexpected and reach the generic `ERROR` handler (Q14, Q15).
3. **Framed-read observation corrected.** `ObserveFirstMessageAsync` now distinguishes:
   - local disposal → `CANCELLED` (Q21; not `ERROR`, not `CLEAN_EOF`),
   - expected peer closure → `CLEAN_EOF`,
   - unexpected socket/IO faults → `ERROR` (Q23).
4. **Additional framed messages use the same policy**, so wrapped socket faults are no longer
   reduced to `CLEAN_EOF` there either.
5. **No broad suppression.** No new trace taxonomy value was introduced. No exception is
   classified by type alone: the classifier inspects the cause chain and only accepts an explicit
   closure set. Clean EOF (read returning zero) and genuine truncated frames (`TRUNCATED`) are
   unchanged.
6. **Cleanup does not overwrite the terminal outcome.** The `finally` block still closes the
   client and records the trace after the classification decision, so a later close does not
   replace an earlier `ERROR`.

## 6. After-fix probe results

The retained Q2 probes were rerun against the remediated source (default project links the live
listener):

| Probe | Injection | Expected | After-fix actual | Result |
| --- | --- | --- | --- | --- |
| Q14 NetworkDown | direct `SocketException` during negotiation | `ERROR` | `ERROR` | pass |
| Q14 NetworkUnreachable | direct `SocketException` during negotiation | `ERROR` | `ERROR` | pass |
| Q14 NoBufferSpaceAvailable | direct `SocketException` during negotiation | `ERROR` | `ERROR` | pass |
| Q15 | `IOException` + inner `NetworkDown` during negotiation | `ERROR` | `ERROR` | pass |
| Q21 | `ObjectDisposedException` during framed read | not `ERROR` | `CANCELLED` (not `CLEAN_EOF`) | pass |
| Q23 | `IOException` + inner `NetworkDown` during framed read | `ERROR` | `ERROR` | pass |
| Q19 | close after `NoDelay`, before real `GetStream` | `CLOSED` with ODE | `CLOSED` with ODE | pass |

Decisive run: **26 passed / 0 failed**, exit 0
(`docs/reviews/evidence/B0-A-TRACE-R2/after/q2-after.txt`,
`after/q2-after.trx`).

## 7. GetStream control

The historical baseline hook snapshot still fails Q20 as expected. For the after-fix control, a
new remediated-source hook snapshot was generated with exactly one expression changed:
`client.GetStream()` → `ReviewerHooks.GetStream(client)`.
See `docs/reviews/evidence/B0-A-TRACE-R2/controls/GetStreamHookListener.cs`.

| Run | Q19 | Q20 | Result |
| --- | --- | --- | --- |
| Baseline hook | pass (`CLOSED`, ODE) | fail (`CLOSED`, expected `ERROR`) | 1 passed / 1 failed |
| Remediated hook | pass (`CLOSED`, ODE) | pass (`ERROR`) | **2 passed / 0 failed** |

Evidence: `baseline/q2-hook-baseline.txt`, `after/q2-hook-after.txt`,
`baseline/q2-hook-baseline.trx`, `after/q2-hook-after.trx`.

## 8. Regression totals

| Suite | Baseline | After | Notes |
| --- | ---: | ---: | --- |
| Full solution | 826 / 0 / 0 | **826 / 0 / 0** | unchanged |
| Driver project | 47 passed / 10 gated | **60 passed / 0 failed / 10 gated** | +13 new R2 regression tests |
| B2-A-Q2 | 18 | **18** | unchanged |
| B2-B-Q1 | 67 | **67** | unchanged |
| B2-B-Q2 | 46 | **46** | unchanged |
| B2-C-Q2 | 40 | **40** | unchanged |
| B0-A-TRACE-Q1 probes | 14 | **14** | unchanged, includes T10 |
| T10 separately | 1 | **1** | unchanged |
| Authentic handshake | 2 | **2** | unchanged |
| R1 shutdown regression | 10 | **10** | unchanged |
| R2 focused fault-classification tests | — | **13 passed** | new |
| Q2 decisive probes | 20 passed / 6 failed | **26 passed / 0 failed** | defects corrected |
| Q2 GetStream control | 1 passed / 1 failed | **2 passed / 0 failed** | Q20 corrected |

The only count change is the expected addition of the 13 producer regression tests. The
full-solution and driver builds have zero warnings/errors. The retained Q1 probe build still emits
its pre-existing `CS0219` warning in untouched historical source; it is not introduced here.

New regression coverage in
`tests/BoltSharp.DriverIntegrationTests/HandshakeTrace/ListenerFaultClassificationRegressionTests.cs`:

- unexpected socket fault during negotiation, with cleanup retaining `ERROR`;
- wrapped unexpected socket fault during negotiation;
- wrapped unexpected socket fault during the first framed read;
- direct unexpected socket fault during the first framed read;
- wrapped unexpected socket fault during an additional framed read;
- `ObjectDisposedException` during framed read → local closure, not `ERROR`, not `CLEAN_EOF`;
- genuine clean EOF remains `CLEAN_EOF`;
- expected peer reset remains `CLEAN_EOF`;
- genuinely truncated frame remains `TRUNCATED`, not `CLEAN_EOF`;
- explicit local close during framed read is not `ERROR`;
- expected disposal during shutdown is not `ERROR`;
- shutdown competing with an injected transport failure retains `ERROR`;
- acquisition classification distinguishes expected closure from unexpected faults.

## 9. Authentic handshake hashes

The four authentic handshake files are unchanged and byte-identical to the Q2 independent audit:

| File | SHA-256 | Bytes |
| --- | --- | ---: |
| `driver-manifest-client-handshake.hex` | `0CD1BABC841371031AA2EE228A7CCAAF233D32428C60BB7F16F3D37210A39A5C` | 76 |
| `driver-manifest-server-response.hex` | `52445611830D9D51BA7D8E56E9F9923E5C804A3DAB388248B0C87918E9DEC0AC` | 31 |
| `driver-legacy-client-handshake.hex` | `9BAB968DED8B229BEE87BBEEEC39E26C096EE6ACB39D4A6FE848419E7E17AE13` | 61 |
| `driver-legacy-server-response.hex` | `C35413A73CD1EBB50AF8BF14CC07FA8BE069902E8FB30FCDB32E52483D4E9AB0` | 13 |

Machine-readable results: `docs/reviews/evidence/B0-A-TRACE-R2/authentic-handshake-hashes.json`.
No HELLO/LOGON compatibility is claimed.

## 10. Remaining limitations

- The listener is test-only; this is Windows 10.0.26200, net8.0, .NET 8.0.31 evidence. Socket
  faults are injected deterministically; no real OS network outage or resource exhaustion was
  induced.
- The accepted closure set is finite and conservative. Unknown or network-level socket codes
  default to `ERROR`; if a future platform reports an ordinary close with a code outside the set,
  it would be reported as `ERROR` rather than `CLOSED`. That is the safe direction for this
  finding.
- A framed-read `ObjectDisposedException` is treated as local disposal → `CANCELLED`. The Q2
  oracle only requires “not `ERROR`”; the contract-consistent choice preserves `CLEAN_EOF` for
  genuine peer EOF. An ODE caused by a non-listener disposal is not distinguishable at this
  boundary and would also be classified as local closure. Q3 should assess this choice explicitly.
- Bounded race tests are coverage, not proof over every possible schedule. T10’s historical
  interleaving/refusal limitations are unchanged.
- No owner acceptance, no push, no B3/B4/B5 work.

## 11. Independent Q3 handoff

Q3 should:

1. Start from the evidence commit (final handoff) and confirm the implementation commit
   `0766bbdacea4955d445b007c0d83ead91ea54a21` is its ancestor.
2. Rerun the retained Q2 probes exactly as in `docs/reviews/evidence/B0-A-TRACE-R2/commands.json`;
   expect Q14 ×3, Q15, Q21, Q23 to pass and Q19/Q20 to pass.
3. Confirm the after-fix GetStream control uses
   `docs/reviews/evidence/B0-A-TRACE-R2/controls/GetStreamHookListener.cs`, not the historical
   baseline hook snapshot.
4. Run `ListenerFaultClassificationRegressionTests` and the driver/shutdown suites; confirm
   60 passed / 10 gated and 10 R1 passes.
5. Recheck the four authentic handshake hashes in
   `authentic-handshake-hashes.json`; confirm they still match the Q2 audit.
6. Assess specifically whether `CANCELLED` is the right post-handshake status for the Q21 local
   disposal, and whether the expected-closure socket-code set is adequately narrow.
7. Do not reopen `TRACEQ1-F01`; no regression in its accepted correction was demonstrated.

**Final disposition: Q2 REMEDIATED — READY FOR INDEPENDENT Q3.**
