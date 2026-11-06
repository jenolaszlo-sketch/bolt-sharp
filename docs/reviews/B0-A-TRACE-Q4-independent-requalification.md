# B0-A-TRACE-Q4 — Independent requalification of R3 trace classification remediation

**Disposition: B0-A-TRACE-Q4 SUPPORTED WITH QUALIFICATIONS.** All in-scope R3 corrections are
independently supported against the committed R3 source. The previously open termination
classification defects are closed. No new blocking or major TRACE defect remains. Bounded
platform/race qualifications remain explicit. Bounded owner acceptance is recommended. This is a
reviewer assessment, not owner acceptance, and it records no owner signoff.

**Executor:** reviewer session running **DeepSeek V4.1 Flash** (provider `nvidia`) in OpenCode. The
assignment header names "OpenAI Codex"; that reported identity could not be confirmed from this
session's runtime, and the discrepancy is recorded rather than resolved. **Independence remains
UNVERIFIED**; Git author labels do not establish independent human, organizational or model
provenance. Technical conclusions rest on executable controls and source assessment, not on the
producer's assurances.

**Date:** 2026-11-06 (execution timestamps UTC).

**Repository:** `D:\projects\bolt-sharp`, canonical `main`.

**Independence:** This session authored only the Q4 probes, evidence and this report. It did not
author R3 or any prior remediation. No production source was changed; no branch, linked worktree,
reset, clean, prune, rebase or push was performed. Only additive Q4 reviewer artifacts were created.

---

## 1. Preflight status (mandatory gate)

**Initial status: NOT STARTED.**

| Check | Value |
| --- | --- |
| Expected R3 HEAD | `9ed5150096d18ea56b92a6535f065786ea331458` |
| Actual HEAD | `9ed5150096d18ea56b92a6535f065786ea331458` (**match**) |
| Branch | `main` (canonical) |
| HEAD tree | `6af3130696e5261bd4eadc764ac75b4a4ea2c1ca` |
| Working tree | clean; no staged/unstaged tracked changes |
| Worktrees | exactly one (`D:/projects/bolt-sharp`) |
| Origin | `https://github.com/jenolaszlo-sketch/bolt-sharp.git` |
| Commits matching `TRACE-Q4`/`TRACE-R3`/`B0-A-TRACE` | R3 remediation + R3 evidence only; **no Q4 commit** |
| Existing Q4 report | none |
| Existing Q4 evidence package | none (`docs/reviews/evidence/` has no `B0-A-TRACE-Q4`) |
| Existing Q4 probes | none |

No Q4 reviewer package, report, probe or commit existed before this session, and no concurrent Q4
writer was observed. Q4 was therefore executed in full as a bounded independent review. No earlier
handoff was mistaken for a completed Q4.

The reviewed target is unambiguous: HEAD equals the R3 evidence commit, whose only production-relevant
change since the `81f12f9` baseline is the test-listener classification remediation `a79a28f`.

---

## 2. Reviewed identity and source verification

| Identity | Commit | Tree |
| --- | --- | --- |
| R3 entry baseline | `81f12f9d1f0c274d07431e2543f5ddb58e2e733a` | `f079c054e12fa93204c53e722f684e4a7bc773d9` |
| R3 implementation | `a79a28f29f060703d90adfbbb3d7f558576f7df3` | `dc979880ac0f4e748e4df4ae7530d9d4f09beb43` |
| Reviewed HEAD / R3 evidence | `9ed5150096d18ea56b92a6535f065786ea331458` | `6af3130696e5261bd4eadc764ac75b4a4ea2c1ca` |

`git diff 81f12f9 a79a28f --stat` touches exactly three test files and no path under `src/BoltSharp`:

```
tests/.../HandshakeTrace/ListenerClassificationR3RegressionTests.cs  (new, +506)
tests/.../HandshakeTrace/ListenerFaultClassificationRegressionTests.cs (8 lines changed)
tests/.../HandshakeTrace/LoopbackHandshakeListener.cs                 (122 lines changed)
```

Source-control byte identities were independently recomputed with `git hash-object`:

| Artifact | Expected blob | Observed blob | Result |
| --- | --- | --- | --- |
| live listener (`.../LoopbackHandshakeListener.cs`) | `7017a51ffa2fe693a0e5fd37d5aa3a9492cdf6bf` | `7017a51ffa2fe693a0e5fd37d5aa3a9492cdf6bf` | match |
| pre-R3 control snapshot | `24d530498aabaa0ec5d1275ad040a6e984afbe43` | `24d530498aabaa0ec5d1275ad040a6e984afbe43` | match |

The pre-R3 snapshot is byte-identical to the `81f12f9` listener (`git diff` exit 0 against
`81f12f9:.../LoopbackHandshakeListener.cs`). The R3 (`GetStreamHookListener.cs`) control differs from
the live listener by exactly one line — the `client.GetStream()` expression replaced by
`ReviewerHooks.GetStream(client)` — with every handler and classifier identical. Both claims in the
R3 report are therefore verified.

**Production code is untouched.** The remediation is confined to a test-only loopback listener; the
governing framing/handshake code under `src/BoltSharp` (`BoltMessageReader`, `BoltHandshakeNegotiator`)
is not modified by R3.

---

## 3. Evidence integrity (R3 manifest)

The R3 manifest was independently verified from working-tree bytes (`.gitattributes` sets `* -text`
in the evidence directory, so working bytes equal committed blob bytes):

* Listed files: **49**; report entry: 1; total checked: **50**.
* Result: **50 / 50 SHA-256 hashes and byte counts match; 0 mismatches.**

This reproduces the producer-reported "50/50 evidence hashes verified". The Q4 review reused the R3
evidence addendum's two recorded historical limitations (the R2 manifest omission of
`authentic-handshake-hashes.json`, and the R2 relative `ListenerSource` build command) without
rewriting any historical R2/Q3 evidence.

---

## 4. Classification contract and independent path audit

The governing contract, documented in source and in the R3 report, is:

| Status | Permitted meaning | Enforced rule |
| --- | --- | --- |
| `CLEAN_EOF` | Genuine zero-byte read, valid protocol boundary | Produced **only** by the `body is null` branch of `BoltMessageReader.ReadMessageAsync` (first and additional). No exception path can reach it. |
| `CLOSED` | Reached connection closure at the **outer** negotiation/acquisition boundary only | Never produced for a framed-read exception. |
| `CANCELLED` | Established local teardown/cancellation, or the listener-owned stream/socket disposal signature | At the framed-read boundary a reset/abort maps here **only** when lifecycle state establishes local teardown. |
| `ERROR` | Unexpected network/IO/lifecycle/resource failure | The default when benign termination cannot be established. |

**Source-backed proof (static):** `CLEAN_EOF` is assigned at exactly two source locations, both the
`body is null` branches — `LoopbackHandshakeListener.cs:487` (first read) and `:573` (additional
read). Every other terminal status on a framed read flows through the shared
`ClassifyFramedReadFailure(Exception, bool)`, which by inspection returns only `CANCELLED` or
`ERROR`, never `CLEAN_EOF`. The first-read generic catch (`:533`) and the additional-read filtered
catch (`:611`) both call it, so the two boundaries cannot diverge.

Ten terminal paths were traced; for each, the causal event, lifecycle state, exception/result,
classification decision, terminal outcome, overwrite risk and contract requirement are:

| # | Path | Causal event | Lifecycle state | Classification | Terminal | Overwrite risk | Governing requirement |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Normal clean EOF | `ReadMessageAsync` returns null | either | `body is null` branch | `CLEAN_EOF` | none (assigned once) | CLEAN_EOF ≡ zero-byte read |
| 2 | Explicit local shutdown | `listener.Cancel()` / lifetime token cancelled | teardown requested | negotiation `OCE`→`CANCELLED`; framed read → `IsLocalTeardownRequested` true | `CANCELLED` | none | shutdown is local cancellation |
| 3 | Expected disposal during shutdown | `DisposeAsync` closes owned client | teardown requested | disposal/closure → `CANCELLED` | `CANCELLED` | none | expected disposal ≠ unexpected error |
| 4 | Unexpected socket failure | `NetworkDown`/`Unreachable`/`NoBufferSpace`/`AccessDenied`/`TimedOut` (± IO-wrapped) | open, no teardown | not in closure set → default | `ERROR` | none | unexpected fault stays ERROR |
| 5 | Stream acquisition / `GetStream` failure | close before `GetStream`; unexpected acquisition fault | pre-handshake | `IsExpectedAcquisitionClosure` → `CLOSED`; else → `ERROR` | `CLOSED` / `ERROR` | none | closure vs fault distinguished |
| 6 | Framed-read disposal | direct/wrapped `ObjectDisposedException` | listener-owned | `IsLocalDisposal` → `CANCELLED` | `CANCELLED` | none | non-clean, non-error closure |
| 7 | Interrupted / incomplete frame | `BoltFramingTruncatedException` | either | dedicated branch | `TRUNCATED` | none | framing error ≠ clean EOF |
| 8 | Failure then cleanup | classified failure; later `DisposeAsync` | teardown after event | status set before `finally`; `finally` only reads | prior status retained | **none** (verified) | later close cannot conceal earlier fault |
| 9 | Concurrent shutdown + transport failure | teardown requested, then closure/fault released | teardown requested | closure code → `CANCELLED`; non-closure fault → `ERROR` | `CANCELLED` / `ERROR` | none | correct distinction under supported ordering |
| 10 | Cancellation / owner-initiated closure | token cancel; owner `Close` during read | owner/local | `OCE`→`TIMED_OUT`/`CANCELLED`; owner close → `CANCELLED`/`CLEAN_EOF` | not `ERROR` | none | local closure is not an error |

Key confirmations:

* **Unexpected faults are not silently converted to clean termination.** No exception path assigns
  `CLEAN_EOF`; the default for an unconfirmed closure is `ERROR`.
* **Expected shutdown disposal is not reported as an unexpected error.** `IsLocalTeardownRequested`
  derives local teardown from the shutdown token, the per-connection lifetime token, or an
  already-closed owned client (`TcpClient.Client is null`, TRACEQ1-F01).
* **No blanket type-based rule.** `ObjectDisposedException` is never classified as clean closure, and
  a socket exception is neither universally `ERROR` nor universally clean: a closure code is
  `CANCELLED` only when lifecycle state establishes local teardown, otherwise `ERROR`.
* **Terminal status is stable.** Each boundary assigns its status once; the handler `finally` only
  reads managed capture state, so a later cleanup cannot overwrite an earlier unexpected failure
  (independently exercised by Q4 `P08`).

---

## 5. Historical finding-closure matrix

| Finding ID | Original failure | R3 correction (source) | Current probe | Result |
| --- | --- | --- | --- | --- |
| **TRACEQ1-F01** (MAJOR) | Escaping endpoint/`GetStream` NRE/ODE in the accept→handler prologue | (R1, pre-R3) endpoint captured in accept loop; socket access inside the exception boundary; `IsOwnedClientClosed` | Q2 retained 26/26; R1 shutdown 10/10; GetStream Q19 = `CLOSED` | **CLOSED** (carried; original mechanism not reopened) |
| **TRACEQ2-F01** (MAJOR) | Unexpected socket failures classified `CLOSED`/`CLEAN_EOF` at acquisition/negotiation/observation | (R2/Q2-REM-01) narrow six-code closure set; network-level faults fall through to `ERROR`; shared classifier excludes them | Q2 retained 26/26 (Q14×3, Q15, Q20, Q23); R2 `UnexpectedSocketFault_*`; Q4 `P01` (5 codes × direct/wrapped), `P05` | **CLOSED** |
| **TRACEQ3-F01** (MINOR) | Framed-read reset/abort (all six closure codes) reported `CLEAN_EOF` without context, first and additional | Shared `ClassifyFramedReadFailure`; exception never reaches `CLEAN_EOF`; reset/abort → `ERROR` unless lifecycle establishes local teardown | R3_04/R3_05; Q4 `P02`/`P03`/`P04`; pre-R3 negative control (8 failures); source audit | **CLOSED** |
| **TRACEQ2-F02** (MINOR) | Post-handshake framed-read `ObjectDisposedException` became `ERROR`; arbitrary disposal origin unprovable | `IsLocalDisposal` → `CANCELLED` at the framed-read boundary; lifecycle-aware teardown; listener-ownership assumption documented in source | R2 `ObjectDisposed_...`; Q2 Q21; Q3 `ArbitraryDisposedObjectIsCancelledWithoutToken` (passes); Q4 `P06` | **CLOSED** for the accepted Q2 contract, with a bounded OBSERVATION (unrelated disposal origin not distinguishable from the exception alone — documented) |

**The six historical Q3 failures are not current Q4 failures.** They are the retained reviewer
assertions `ClosureCodesBecomeCleanEofEvenWithoutCancellation` × {ConnectionReset, ConnectionAborted,
OperationAborted, NotConnected, Shutdown, Disconnecting}, which deliberately encode the **pre-R3
defect**. On a correct implementation they **must** fail. Independently reproduced: the Q3 retained
run is **10 passed / 6 failed**, and the six failures were individually confirmed to be exactly those
named cases. No old reviewer oracle was rewritten.

The producer's R2 assertion `ExpectedPeerReset_DuringFramedRead_RemainsCleanEof` was renamed and
inverted to `..._IsErrorNotCleanEof`; the pre-R3 expectation is recorded in the R3 report and the
historical R2 evidence is untouched. This is a documented contract correction, not an oracle-
weakening: it encodes the same requirement as TRACEQ3-F01.

---

## 6. Independent executable verification

Build provenance: committed R3 source at `9ed5150` (HEAD = R3 evidence commit), SDK 10.0.401,
MSBuild 18.9.11, target `net8.0`, runtime 8.0.31, Windows 10.0.26200 x64. The reviewer probes build
with **0 warnings / 0 errors** against the live listener. The pre-R3 negative control is produced by
linking the byte-faithful snapshot (`24d5304...`) via `-p:ListenerSource`.

| Set | Q4 actual | R3 reference | Match |
| --- | ---: | ---: | --- |
| Full solution | 826 passed / 0 failed | 826 | yes |
| Driver project | 75 passed / 0 failed / **10 gated** | 75 / 10 gated | yes |
| R1 shutdown | 10 passed | 10 | yes |
| R2 classification | 13 passed | 13 | yes |
| R3 classification | 15 passed | 15 | yes |
| R3 probe host (live, `R3.csproj`) | 15 passed | 15 | yes |
| R3 probe host (pre-R3 negative) | **7 passed / 8 failed** (exit 1) | 7 / 8 | yes |
| Retained Q2 decisive probes | 26 passed | 26 | yes |
| Retained Q3 reviewer probes | **10 passed / 6 failed** (exit 1) | 10 / 6 | yes |
| GetStream hook control (Q19/Q20) | 2 passed | 2 | yes |
| Authentic handshake | 2 passed (within driver set) | 2 | yes |
| **Q4 independent reviewer probes** | **27 passed / 0 failed** (exit 0) | — | new |

The overlapping driver/solution sets are **not** summed. The 10 gated entries are the D01–D10
`NotExecuted` driver interop tests (TRX outcome audit), unchanged.

### 6.1 Reviewer-authored probes (`docs/reviews/probes/B0-A-TRACE-Q4/`)

Independent of the producer suite, 27 deterministic fault-injection cases passed:

* `P01` — 5 unexpected socket codes (NetworkDown, NetworkUnreachable, NoBufferSpaceAvailable,
  AccessDenied, TimedOut) × {direct, IO-wrapped} at the framed read ⇒ `ERROR`, never
  `CLEAN_EOF`/`CLOSED`/`CANCELLED`. (10 cases)
* `P02` — the six closure codes at the framed read with **no** local teardown ⇒ `ERROR`, never
  `CLEAN_EOF`. (6 cases)
* `P03` — the same six closure codes with local teardown established (listener shutdown requested)
  ⇒ `CANCELLED`, not `ERROR`, not `CLEAN_EOF`. (6 cases)
* `P04` — closure code during the **additional** framed read with teardown ⇒ additional message
  `CANCELLED`; observation stays `MESSAGE_READ`.
* `P05` — unexpected wrapped network fault during the additional read ⇒ `ERROR`.
* `P06` — direct and IO-wrapped `ObjectDisposedException` at the framed read ⇒ `CANCELLED`, never
  `CLEAN_EOF` or `ERROR`.
* `P07` — genuine zero-byte EOF ⇒ `CLEAN_EOF` with no `ErrorType`; EOF inside an incomplete frame
  ⇒ `TRUNCATED`; the two remain distinct.
* `P08` — **real handler path** (acquisition → negotiation → observation) with an unexpected
  `NetworkDown` at the framed read ⇒ trace `ACCEPTED`, post `ERROR`; a subsequent
  `DisposeAsync()` leaves the same trace and the same `ERROR` (cleanup does not conceal the fault).

Faults are injected deterministically into the framed read and released via
`TaskCompletionSource` barriers; no sleep is used to establish a phase. They prove classification at
the actual catch boundaries and are **not** a reproduced real network outage.

### 6.2 Negative control (decisive)

Running the producer R3 regression source against the byte-faithful **pre-R3** listener yields
**7 passed / 8 failed**; the eight failures are the six closure-code first reads, the additional
reset, and the local-shutdown reset — each of which yielded `CLEAN_EOF` before the fix and the
corrected outcome after it. This is a controlled injected-fault before/after control, not a real
outage.

---

## 7. Authentic handshake and regression

The reviewer ran the driver project with `BOLTSHARP_TRACE01_EVIDENCE_DIR` redirected to the **Q4**
evidence directory (never the historical locations), regenerating the four handshake files. All four
are **byte-identical** to the accepted Q2/Q3/R3 baselines:

| File | SHA-256 | Bytes | Result |
| --- | --- | ---: | --- |
| `driver-manifest-client-handshake.hex` | `0CD1BABC…39A5C` | 76 | match |
| `driver-manifest-server-response.hex` | `52445611…EC0AC` | 31 | match |
| `driver-legacy-client-handshake.hex` | `9BAB968D…7AE13` | 61 | match |
| `driver-legacy-server-response.hex` | `C35413A7…9EAB0` | 13 | match |

No committed authentic artifact was modified (`git status` shows no tracked change). **No
HELLO/LOGON compatibility or general Neo4j-driver interoperability is claimed** from these TRACE
results; only the handshake framing/selection bytes and the expected post-handshake driver exception
are observed. Bolt 5.8 / PackStream 1.0 selection remains provisional.

---

## 8. Findings

**BLOCKER:** none.

**MAJOR:** none.

**MINOR:** none. No remaining actionable classification defect was demonstrated against the R3
implementation.

**OBSERVATIONS (explicit limitations, no remediation required for this milestone):**

* **Q4-O01 — Outer negotiation-boundary closure codes remain `CLOSED`.** A `ConnectionReset` (and
  the other five closure codes) thrown during negotiation, without local teardown, is recorded
  `CLOSED` at the outer boundary (source `:413`). This is the pre-existing accepted policy (Q13,
  preserved by R3), distinct from the framed-read boundary, which is the one TRACEQ3-F01 governs.
  Not reopened; recorded so the boundary distinction is explicit.
* **Q4-O02 — Unrelated disposal origin is not provable from the exception (TRACEQ2-F02 residual).**
  An `ObjectDisposedException` at the framed-read boundary is `CANCELLED` regardless of origin,
  because the Q2 contract requires it not be `ERROR` and the exception carries no object identity.
  The listener-ownership assumption is documented in source. The arbitrary-origin case was never
  demonstrated reachable in the listener-owned stream path; bounded, documented.
* **Q4-O03 — Closure code racing local teardown.** If local teardown is requested concurrently with a
  closure-code fault, the event is recorded `CANCELLED` (teardown-established). A truly unexpected
  non-closure fault racing teardown stays `ERROR` (R2 `ShutdownCompetingWithInjectedTransportFailure_RetainsError`,
  verified). Within the finite-information contract this is the supported ordering; not all
  interleavings were proven.
* **Q4-O04 — Documentation nuance.** R3 report §4.4 says the additional-message handler now records
  the additional message's `ErrorType`; `FramedMessageObservation` has no `ErrorType` field — the
  type is recorded on the parent observation (`observation.ErrorType ??=`). Behaviour is correct;
  the wording is imprecise. Documentation-only.
* **Q4-O05 — Test-only scope.** Windows 10.0.26200 / net8.0; synthetic deterministic fault
  injection, no real OS outage or resource exhaustion; race coverage bounded; no producer counts were
  treated as independent results.

---

## 9. Final disposition

**B0-A-TRACE-Q4 SUPPORTED WITH QUALIFICATIONS.**

All in-scope R3 corrections are independently supported against committed R3 source. Previously open
termination-classification defects (TRACEQ2-F01, TRACEQ2-F02, TRACEQ3-F01) are closed for the
accepted contract; TRACEQ1-F01 remains closed. No new blocking or major TRACE defect remains. The
bounded platform/race qualifications and the outside-scope negotiation-boundary `CLOSED` policy are
explicit. Bounded owner acceptance is recommended.

### Owner handoff

1. **Initial status:** NOT STARTED (no prior Q4 work).
2. **Existing Q4 work reused:** none; Q4 was executed in full. R3 evidence was *reused and verified* (50/50).
3. **Reviewed HEAD/tree:** `9ed5150…` / `6af3130…`; implementation `a79a28f…` / `dc97988…`.
4. **R3 source/evidence verification:** listener blob `7017a51…`; pre-R3 control `24d5304…` (byte-identical to baseline); hook snapshot differs by one line; manifest 50/50.
5. **Finding closure:** TRACEQ1-F01 CLOSED; TRACEQ2-F01 CLOSED; TRACEQ2-F02 CLOSED (bounded); TRACEQ3-F01 CLOSED.
6. **Unexpected socket-failure classification:** `ERROR` (all 5 codes × direct/wrapped; framed first and additional).
7. **Framed-read disposal classification:** `CANCELLED` (direct and IO-wrapped), never `CLEAN_EOF`/`ERROR`.
8. **GetStream control:** 2/2 — pre-`GetStream` close → `CLOSED` (ODE); unexpected acquisition fault → `ERROR`.
9. **Shutdown/race precedence:** teardown-established closure → `CANCELLED`; non-closure fault racing teardown → `ERROR`; later disposal does not overwrite an earlier `ERROR`.
10. **Independent test counts / provenance:** solution 826; driver 75 (+10 gated); R1 10; R2 13; R3 15; R3 live 15; R3 negative 7/8; Q2 retained 26; Q3 retained 10/6; GetStream 2; authentic 2; **Q4 probes 27/0**. SDK 10.0.401 / net8.0 / runtime 8.0.31.
11. **Authentic hash verification:** all four files byte-identical to accepted baselines; no historical artifact modified.
12. **Remaining qualifications:** Q4-O01…O05.
13. **Final disposition:** **B0-A-TRACE-Q4 SUPPORTED WITH QUALIFICATIONS.**
14. **Recommendation:** bounded owner acceptance of the B0-A-TRACE classification milestone. No next
    remediation is required for this scope. If desired, the only optional cleanup is the Q4-O04
    documentation wording; it has no behavioural effect.

**Stop conditions honored:** no production source change; no R3 remediation during Q4; no unrelated
listener, B3/B4/B5 or HELLO/LOGON work; no new branch or linked worktree; no reset/clean/prune/rebase/
push; no modification of historical evidence; no owner acceptance recorded. New Q4 artifacts are
additive and untracked; no repository commit was made.

---

## Appendix A — Q4 artifact inventory

Reviewer evidence: `docs/reviews/evidence/B0-A-TRACE-Q4/` (report separately at
`docs/reviews/B0-A-TRACE-Q4-independent-requalification.md`). Reviewer probes:
`docs/reviews/probes/B0-A-TRACE-Q4/` (`Q4.csproj`, `IndependentQ4Classification.cs`).

Reviewer probe source hashes:

| File | SHA-256 |
| --- | --- |
| `docs/reviews/probes/B0-A-TRACE-Q4/Q4.csproj` | `A09D1715274AB6A2E34F75C67607327B01ECD90F14C7CCA024628B2630ACA6BC` |
| `docs/reviews/probes/B0-A-TRACE-Q4/IndependentQ4Classification.cs` | `71BBB616E8328AC0593642A5BAB879E7008C877D46DF71A4ACCEAA24576431A7` |

The evidence directory contains `manifest.json` (SHA-256 of every Q4 evidence file except the
manifest itself), `commands.json`, `environment.txt`, `q4-results-summary.json`,
`authentic-handshake-hashes.json`, the regenerated `authentic/` hex and trace files, and the `trx/`
results for every executed set. Raw logs and reviewer probes are preserved.
