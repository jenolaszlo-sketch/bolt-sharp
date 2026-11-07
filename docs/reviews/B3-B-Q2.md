# B3-B-Q2 — Independent remediation requalification

Review date: 2026-11-07 (commission/client date). Reviewer: Codex, independently of the recorded
GLM-5.3-Flash producer and Q1 executor. This is a qualification report, not owner acceptance.

## 1. Formal disposition

**REMEDIATION REQUIRED**

R1 corrects the demonstrated direct GOODBYE, disconnect, session-dispose and null-`Fields`
disposal-exception escapes. Invalid integer TELEMETRY status mapping is corrected. X04 is an
**EXPECTED CONTRACT CORRECTION**. However, independent probes demonstrate two blocking lifecycle
defects in the reviewed implementation:

1. **Q2-F01:** a failed cleanup is forgotten by recovery control. RESET reports SUCCESS/Ready and
   RUN opens another operation although the previous operation's cleanup did not complete.
2. **Q2-F02:** RUN metadata access/materialization is outside the provider exception boundary.
   The first `Fields` getter can throw after acquisition but before installation, leaking the
   operation with zero disposal attempts, even after disconnect and session disposal. Later
   metadata failure can leave Streaming without any RUN SUCCESS having been returned.

The first finding does **not** require changing the commissioned immediate PULL-completion
FAILURE/Failed result. It requires refusing false recovery afterward. Both findings are reachable
with ordinary, sequential calls to the public provider interface. Neither depends on concurrency,
reflection-based state mutation, process-fatal exceptions, or a hostile client bypassing B3-A.

Recommend a bounded **B3-B-R2-IMPL-01**, then independent **B3-B-Q3**. Do not commission owner
acceptance yet. No remediation, B3-C/B5 work, owner decision, or push was performed.

## 2. Verified baseline, history and scope

| Item | Verified value |
| --- | --- |
| Canonical checkout | `D:/projects/bolt-sharp` |
| Starting branch | `main` |
| Starting HEAD | `801badcea8523372d4ed83f8f2942d80bc0373e1` |
| Starting tree | `243998eb21d01aa918f4b83143cb8a0c6bb27dd1` |
| Reviewed R1 implementation | `454ae005245d6511b672f8962092829839fb7153` |
| R1 implementation tree | `f93388ca7a23bcf727b6c76b8435d0b1f609f5ad` |
| R1 parent / original Q1 evidence | `3caab5bafad0af651e5e066b29278606555388d6` |
| Original B3-B implementation | `9f224cf41656e4baf155532e682b862ed8e910b0` |
| Worktrees | Exactly one, canonical checkout |
| Entry tracked/index/untracked state | All clean; no untracked files to disturb |
| Remote-tracking `origin/main` | `9abf5e3724a31aff085b3810ebb9bd3a7783ea2a`; not fetched or pushed |

Linear ancestry and R1 change isolation were checked. R1 changes exactly one production file,
one existing test file, and adds one test file. The implementation is an ancestor of HEAD;
the follow-up commit contains documentation/evidence. Q1 report, probes and evidence, B3-A
message implementation, PackStream, framing, handshake, solution and shared build properties
are unchanged since the R1 entry revision. Q1 manifest **12/12** and R1 manifest **14/14**
entries match SHA-256. No applicable repository `AGENTS.md` was found.

See [baseline](evidence/B3-B-Q2/baseline.json), [history audit](evidence/B3-B-Q2/history-audit.txt),
[integrity verification](evidence/B3-B-Q2/historical-integrity.json),
[reviewed inputs](evidence/B3-B-Q2/reviewed-inputs.json), and
[actual R1 delta](evidence/B3-B-Q2/implementation-delta.patch).

Only this report and `docs/reviews/evidence/B3-B-Q2/` are deliverables. Builds, restore outputs,
and binaries were placed in external scratch under
`C:/Users/Laszlos/source/repos/Solo/reviews/boltsharp-b3b-q2-20261107/`.
The final documentation commit HEAD/tree are reported in the handoff; they can also be obtained
from the commit adding this report. They are not recursively embedded in their own tree.

## 3. Binding contracts and independent method

The review began from [the governing specification](../boltsharp-spec.md),
[B3-B acceptance requirements](../implementation-tasks.md),
[the preserved server-state specification](evidence/B3-B-IMPL-01/spec/server-state.adoc),
[version-5 transitions](evidence/B3-B-IMPL-01/spec/version-5.adoc), and
[message contracts](evidence/B3-B-IMPL-01/spec/message.adoc). The B3-A implementation record and
[owner acceptance](../decisions/B3-A-OWNER-01-owner-acceptance.md) establish the typed-message
boundary. The [commission](evidence/B3-B-Q2/commission.md) explicitly requires at-most-once
**attempted** disposal, explicit terminal accounting, and no apparently reusable session with
incomplete cleanup. Producer reports were compared against code and execution, not used as
proof of their own conclusions.

Relevant accepted requirements:

- Governing specification §5 (ownership model) assigns cursor ownership to the session and disposal
  to connection teardown; §6.3 permits RESET SUCCESS when legally recoverable. B3-B task acceptance
  includes defined failure/closure and no cursor leak.
- B3-B-IMPL-01 §7.2 requires RESET cleanup failure to become FAILURE/Defunct, with no recovery
  claim while work remains active. Q1 §8 supports that bounded fail-closed RESET contract.
- `IBoltOperation` in `BoltSessionOptions.cs` is a prepared handle owned by the session;
  disposal is attempted at most once on completion/failure/reset/closure. Its `Fields` getter is
  provider code. There is no exemption that makes throwing metadata access caller-owned.
- Bolt `FAILED` is temporarily unusable/recoverable; `DEFUNCT` is permanently unusable and terminal.
  The wire specification does not define a .NET `Dispose` exception. Applying the repository's
  cleanup contract to that exception is an explicit contract interpretation, not a quotation of
  a protocol rule about .NET.

New probes reference only the freshly built production assembly. They do not reuse producer or
Q1 harness classes. A tracked resource remains live until a successful disposal; injected
disposal throws **before release**. Each action records starting state, installed ownership,
response metadata and encoded bytes, final state, close flag, acquisition count, disposal
attempts, successful cleanup, resource liveness, exception escape and diagnostic identity.
The provider observes the state and detached ownership at disposal time without re-entering
the session. `ResourceLive` is a deterministic simulated provider-resource flag, not a claim
to measure OS handles.

## 4. Cleanup ownership reconstruction

| Stage/path | Responsible owner and operation lifetime | Observed consequence |
| --- | --- | --- |
| Before `Prepare` returns | Provider owns allocations not returned to caller | Throw/null transfers no operation; session emits recoverable FAILURE |
| Non-null `Prepare` return | Session's RUN call owns the local handle, even before `_operation` installation | This obligation is lost if the first `Fields` access throws (Q2-F02) |
| Null `Fields` | Local handle, never installed; direct `DisposeGuarded` call | One attempt; clean -> Failed, throwing -> Defunct; no cursor/look-ahead installed |
| Valid metadata | `_operation` installed; `_pendingRow` cleared; state set Streaming | A second metadata getter/enumeration still runs afterward and can escape before RUN response (Q2-F02) |
| PULL / DISCARD | Session owns active operation and one look-ahead row | Partial result retains both; completion/failure detaches both |
| Exhaustion | `CompleteResult` detaches and attempts disposal | Clean -> Ready/SUCCESS; failure -> Failed/FAILURE, with incomplete resource no longer tracked for recovery |
| Read or row-shape failure | `FailRecoverable` detaches and attempts disposal | Primary failure preserved; cleanup diagnostic recorded; Failed, including failed cleanup |
| RESET | State Interrupted; detach then dispose | Failure on this call -> Defunct/FAILURE; no operation -> Ready/SUCCESS, even after an earlier failed disposal (Q2-F01) |
| GOODBYE / Disconnect | Detach and dispose; Defunct follows guarded call | No protocol response; contained failure; one attempt |
| Session Dispose | Disconnect; `_disposed=true` in finally | Repeated calls do not retry; Receive afterward throws ObjectDisposedException |
| Terminal protocol failure | `FailTerminal` detaches and disposes, sets Defunct, constructs result | Primary FAILURE preserved; cleanup error does not escape |
| Later terminal cleanup | `_operation` already null | No repeated disposal; Defunct never reopened |

`DetachAndDisposeOperation` first copies the reference locally, clears `_operation` and
`_pendingRow`, and then actually calls the guard. This is a meaningful disposal attempt, not
detachment alone. The local reference survives through the call. On an ordinary caught exception
the attempt has failed; successful disposal cannot be inferred. The diagnostic retains the
exception, not a resource owner or a successful-release receipt.

Installed operations receive at most one attempt across all executed terminal combinations,
Failed -> Disconnect, Failed -> RESET, and completion -> session disposal. The pre-installation
metadata path demonstrates the opposite risk: **zero** attempts. An at-most-once invariant alone
does not establish complete ownership coverage or safe recovery.

## 5. Exception containment and terminal-state results

In this table “throwing cleanup” always means the old resource remains live. It never means
release was completed. Full per-action records are in
[q2-probe-results.json](evidence/B3-B-Q2/q2-probe-results.json).

| Scenario | Immediate response/state | Original operation attempts / diagnostic / escape | Follow-up |
| --- | --- | --- | --- |
| GOODBYE, throwing cleanup | No response; Defunct; close | 1 / exact cleanup exception / none | Repeated GOODBYE, Disconnect, Dispose do not retry |
| Disconnect, throwing cleanup | No response; Defunct | 1 / exact exception / none | Dispose and repeated Dispose do not retry |
| Session Dispose, throwing cleanup | No response; Defunct | 1 / exact exception / none | `_disposed` confirmed through later ObjectDisposedException; no reopen |
| GOODBYE -> Dispose | Defunct throughout | 1 / retained / none | Idempotent |
| Late RUN after terminal cleanup | No response; Defunct, or ObjectDisposedException if disposed | Still 1 / retained | Provider open count stays 1 |
| RUN null `Fields`, clean cleanup | FAILURE; Failed; open | 1 / null / none | RESET succeeds; later operation permitted |
| RUN null `Fields`, throwing cleanup | FAILURE; Defunct; close | 1 / exact exception / none | RESET/RUN cannot reopen |
| PULL exhaustion, throwing cleanup | RECORD, FAILURE; Failed; open | 1 / exact exception / none | **RESET falsely succeeds, new operation opens: Q2-F01** |
| DISCARD exhaustion, throwing cleanup | FAILURE; Failed; open | 1 / exact exception / none | **Same recovery defect** |
| RESET on active throwing operation | FAILURE; Defunct; close | 1 / exact exception / none | Repeated RESET/Dispose do not retry |
| PULL read failure then cleanup failure | Prior RECORD, primary FAILURE; Failed | 1 / cleanup exception / none | Primary preserved; **false recovery** |
| DISCARD read failure then cleanup failure | Primary FAILURE; Failed | 1 / cleanup exception / none | **False recovery** |
| Row-shape failure then cleanup failure | Row-shape FAILURE; Failed | 1 / cleanup exception / none | **False recovery** |
| Illegal RUN while Streaming, cleanup failure | Protocol FAILURE; Defunct; close | 1 / cleanup exception / none | Provider never reopened |
| First RUN metadata getter throws | No returned protocol result; Ready | **0 / null / InvalidOperationException** | New RUN opens; teardown never retires first handle: Q2-F02 |
| Second metadata getter / null field element | No returned protocol result; Streaming | **0 initially / null / exception** | Teardown later attempts once; RUN never returned SUCCESS: Q2-F02 |

The session performs no transport emission and has no injectable sender. A caller-side sender
failure was injected after a returned terminal FAILURE; caller Disconnect leaves Defunct and
does not repeat disposal. This verifies that limited integration pattern, not a B5 transport.
State is set before terminal result construction. By contrast RUN field materialization is a
reachable response-construction failure and is explicitly covered by Q2-F02. No guarantee is
made for process termination, stack overflow, or arbitrary allocation failure.

## 6. Independent state-transition matrix

`F/D` = FAILURE/Defunct/close; `S/R` = SUCCESS/Ready; `I/F` = IGNORED/Failed;
`-/D` = no response/Defunct. Every matrix entry for the six externally attainable states and
eight listed actions was executed independently (**48 checks**), using clean finite operations.

| Starting state | RUN | PULL | DISCARD | RESET | GOODBYE | TELEMETRY | Disconnect / Dispose |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Negotiation | F/D | F/D | F/D | F/D | -/D | F/D | -/D |
| Authentication | F/D | F/D | F/D | F/D | -/D | F/D | -/D |
| Ready | SUCCESS/Streaming | F/D | F/D | S/R | -/D | Valid: S/R; invalid integer: FAILURE/Failed | -/D |
| Streaming | F/D | RECORD*/SUCCESS, Streaming or Ready; failure -> Failed | SUCCESS, Streaming or Ready; failure -> Failed | Clean S/R; cleanup failure F/D | -/D | F/D | -/D |
| Failed | I/F | I/F | I/F | Clean S/R; **prior cleanup failure incorrectly S/R** | -/D | I/F | -/D |
| Interrupted | IGNORED/Interrupted | IGNORED/Interrupted | IGNORED/Interrupted | Ready or Defunct | -/D | IGNORED/Interrupted | -/D |
| Defunct | -/D | -/D | -/D | -/D | -/D | -/D | -/D |

Interrupted is transient and was observed inside RESET disposal. Its externally unreachable
message branches were inspected, not fabricated with reflection or concurrent calls. Dispose is
also an orthogonal flag: all Receive APIs reject after disposal with ObjectDisposedException.
GOODBYE is accepted in Authentication by the implementation; the producer's “anything else”
Authentication table row is overbroad. The concrete matrix above records actual behavior.

HELLO moves Negotiation -> Authentication; verified LOGON moves Authentication -> Ready;
missing/throwing verifier fails closed. LOGOFF in Ready requires reauthentication; Failed work
is ignored. HELLO/LOGON in Failed are terminal, as already qualified by Q1, despite the broader
wording of the original producer table. Explicit transaction states remain absent, and
unsupported transaction/routing byte messages remain terminal refusals. No B3-C behavior was added.

## 7. X04 adjudication

**Classification: EXPECTED CONTRACT CORRECTION.**

The general RUN-metadata-failure path is recoverable only when the returned handle is retired
safely. For X04 the provider has successfully returned a handle but no cursor was installed;
its direct cleanup attempt throws. A Failed session followed by RESET would have no installed
handle through which to finish or verify cleanup. The accepted fail-closed RESET/cleanup
contract, and this commission's explicit prohibition on apparent reuse with incomplete cleanup,
therefore require terminal accounting. FAILURE/Defunct with `ShouldClose=true` correctly
preserves the metadata/provider failure and prevents unsafe reuse. Clean null-`Fields` cleanup
continues to produce FAILURE/Failed. This interpretation does not redefine ordinary RUN failure
or the required immediate PULL completion failure state.

The unchanged Q1 probe remains **failed**, exit 4: X04 expects `no-throw;Failed` and observes
`no-throw;Defunct;FAILURE`. Its observation function also appends a response suffix absent from
the old expectation, so the stale string has a formatting omission as well as the state mismatch.
Do not edit the historical probe. A future regression should expect
`no-throw;Defunct;FAILURE`, plus one actual disposal attempt, `HasActiveOperation=false`, close
true, provider metadata `50N00/Neo.DatabaseError.General.UnknownError`, a cleanup diagnostic,
and no subsequent RESET/RUN reopening.

The same fail-closed reasoning exposes Q2-F01 elsewhere; accepting X04 does not establish that
all failed-cleanup recovery paths are safe.

## 8. Primary-failure precedence

| Trigger | Protocol primary | Cleanup diagnostic | Recoverability |
| --- | --- | --- | --- |
| Prepare throws before return | `50N00/UnknownError`, provider description | null; no transferred handle | Failed; clean RESET permitted |
| Provider refuses with null | `50N42/Request.Invalid`, unsupported query | null | Failed; RESET permitted |
| RUN null fields, disposal throws | `50N00/UnknownError`, provider description | Same disposal exception | Defunct |
| PULL/DISCARD read throws + disposal throws | `50N00/UnknownError`, provider description | Disposal exception, not read error | Failed immediately; later RESET unsafe (Q2-F01) |
| Row width mismatch + disposal throws | `50N42/UnknownError`, exact row-shape description | Disposal exception | Primary preserved; later RESET unsafe |
| Exhaustion cleanup fails | `50N00/UnknownError`, provider description; cleanup itself is failure | Disposal exception | Required immediate Failed; later RESET unsafe |
| RESET cleanup fails | `50N00/UnknownError`, provider description | Disposal exception | Defunct |
| Protocol violation + disposal throws | `08N06/Request.Invalid`, protocol description | Disposal exception | Defunct |
| RUN metadata accessor throws | **No protocol FAILURE returned** | No attempt/diagnostic at failure point | Escaped primary; ownership/state defect (Q2-F02) |

The original provider/read exception is deliberately sanitized into protocol metadata; the session
does not retain it as a primary exception property. Precedence is established by exact status,
code and description, including the row-shape distinction, not by exception-object identity.
PULL's valid record prefix is preserved. No secondary cleanup exception replaced these returned
primary responses in the successful containment cases.

Under sequential normal operation, Failed has no installed operation, so there is no reachable
“new cleanup of an active operation while already Failed” path. Failed -> Disconnect and Failed
-> RESET make zero additional disposal attempts. The defect is that the latter treats absence
of an installed reference as successful recovery despite an earlier failed cleanup. New later
cleanup diagnostics can arise only after the current implementation permits recovery and another
acquisition; that sequence is recorded, not endorsed.

## 9. F-02 TELEMETRY requalification

The preserved message specification defines `api::Integer`, enumerates **0, 1, 2, 3**, and
defines invalid-value FAILURE. Example 3 is expressly **v5.7+**, so its `9001` mapping applies
to pinned Bolt 5.8. Example 2 assigns `22G03` to a wrong **type**, not an unknown integer.
No separate invalid-integer subclasses appear in that accepted contract. Applying the same
unknown-identifier classification to -1, 4 and `long.MaxValue` is justified by the shared domain
violation and Q1's accepted F-02 finding; the example alone is not treated as a universal rule
for all TELEMETRY errors or protocol versions.

Independent results:

| Input | Metadata / sequencing |
| --- | --- |
| 0, 1, 2, 3 | Exactly empty SUCCESS metadata; Ready |
| -1, 4, 9001, `long.MaxValue` | Exactly `gql_status="50N00"`, `neo4j_code="Neo.DatabaseError.General.UnknownError"`, `description="The telemetry API identifier is not valid for this server."`, `diagnostic_record={}`; Failed; close false |
| Work after invalid integer | IGNORED; no provider acquisition |
| RESET after invalid integer | SUCCESS/Ready; null cleanup diagnostic |
| String field on wire | B3-A `InvalidFieldType`; session byte path FAILURE `08N06`/Defunct |

All four integer FAILURE maps were encoded/decoded through B3-A and the exact wire hex retained.
No obsolete `code`, `message`, or unintended `cause` field appeared. The repository uses bounded
descriptions rather than reproducing Neo4j's Java exception chain; F-02 did not commission that
chain. Wrong-typed TELEMETRY's existing seam gap remains a qualification, not a silently resolved
conformance claim. **The original F-02 correction is independently verified.**

## 10. `LastCleanupFailure` compatibility and diagnostic semantics

Source delta and reflection agree: one public `Exception?` getter with a private setter on an
already sealed public class. No existing member signature, interface, dependency, friend assembly
grant, assembly name or target framework changed. This is an additive source/binary-compatible
member for ordinary existing consumers; no universal downstream consumer compatibility test or
package compatibility claim was performed.

The property holds the **same exception instance** thrown by provider disposal. It is not an
immutable snapshot: mutation of its `Data` dictionary is observable through subsequent reads
and by the provider. It does not expose or allow mutation of session control fields. The code
never consults this property for transitions. Treat raw provider exceptions as potentially
mutable and containing provider detail; do not describe this surface as immutable/redacted.
This is a non-blocking documentation obligation, not a demand for API redesign.

It means “most recent failed cleanup over this session's lifetime,” not current error or current
resource ownership. It starts null, is replaced only on a later failed cleanup, and is retained
across clean operations, RESET, disconnect and Dispose. The probe observes retention after the
currently permitted successful RESET and replacement on a later failure. Retention follows the
property's wording; the false RESET outcome is Q2-F01, not evidence that diagnostics should be
cleared to conceal it.

Recording consists of a reference assignment. A custom exception whose `Message` getter throws
is contained and recorded without invoking that getter. Reading/logging an arbitrary provider
exception can of course invoke provider behavior; recording itself did not do so. A non-null
value alone cannot be used as an `IsDefunct` predicate. No diagnostic reset or API redesign was
made by this review.

## 11. Executed tests, builds and exclusions

Environment: Windows 10.0.26200 / x64, .NET SDK 10.0.401, MSBuild 18.9.11; projects target net8.0.
Exact commands and exits are in the logs and reproduction scripts.

| Execution | Passed | Failed | Skipped | Notes |
| --- | ---: | ---: | ---: | --- |
| Core test project | 1,025 | 0 | 0 | Entire core suite; production and tests rebuilt externally |
| Session filter | 101 | 0 | 0 | Subset of core, not additive |
| Remediation class | 21 | 0 | 0 | Subset of sessions; R1 report says 20, actual discovered count is 21 |
| Message filter | 98 | 0 | 0 | Subset of core |
| B0-B integration | 19 | 0 | 0 | Separate project, pinned driver dependency |
| Unchanged original Q1 probe | 117 | 1 | 0 | 118 checks; sole mismatch X04; process exit 4 |
| Original cleanup probes X01-X05 | 4 | 1 | 0 | Subset of the unchanged Q1 run, not another independent total |
| New Q2 probe | 163 | 18 | 0 | 181 assertions, 217 trace steps; exit 4; failures map to two defects |

Core full rebuild: **2 warnings, 0 errors**, both pre-existing `xUnit2012` at
`BoltRequestDecodeTests.cs:71` and `:72`. Integration and original Q1 probe builds:
**0 warnings, 0 errors**. Final independent Q2 probe build: **0 warnings, 0 errors**.
The first Q2 authoring build had two `CS8602` advisories in reviewer diagnostic output only;
nullability annotations were corrected and the probe rerun with identical 181/18 results.
Both initial logs are retained. No production compiler error or test failure was hidden.

Original Q1 evidence limitations: its JSON embeds the old revision identifiers as constants,
even when run against R1. Actual input DLL provenance is established by this review's build logs,
hash manifest and current baseline. Its X01-X03 observation helper literally formats
`disposeCount=1`; those strings do **not** measure disposal counters. The new Q2 probes measure
the actual operation counters independently. Historical files were not corrected.

No existing core/session/message/B0-B test was excluded. The separate
`BoltSharp.DriverIntegrationTests` project, real B3-B socket host, Browser interoperability,
B3-C backend integration, B5, concurrent/reentrant usage, and all-platform/package compatibility
were not commissioned or claimed. B0-B's 19 passing tests exercise its accepted fake-server
harness, not a live B3-B host. Bounded `PULL(-1)`, original batch ownership/remainder checks,
authentication, non-jump-ahead RESET, and cursor look-ahead regressions pass in Q1 and session suites.

## 12. Material findings

### B3-B-Q2-F01 — Failed cleanup can be followed by false RESET success

- **Severity:** high; **blocks B3-B qualification**.
- **Contract:** truthful recovery, single active operation ownership, at-most-once attempted
  disposal with explicit terminal failure accounting. Governing spec §5/§6.3;
  B3-B-IMPL-01 §7.2; commission §§6-7.
- **Source:** `BoltConnectionSession.cs:515-522`, `:546-551`, `:592-600`, and `:449-458`.
- **Reproduction:** return an operation whose Dispose throws before release. Complete PULL or
  DISCARD, or inject a ReadRow/row-shape failure. After the returned FAILURE/Failed, send RESET,
  then RUN. Cases `pull-complete`, `discard-complete`, `pull-read`, `discard-read`, `row-shape`.
- **Expected:** preserve the original FAILURE and required immediate Failed state where specified;
  do not retry disposal; fail closed on attempted recovery while cleanup is unresolved. RESET
  must not claim successful recovery and another operation must not open.
- **Observed:** one attempted but unsuccessful disposal; reference detached; diagnostic present;
  RESET SUCCESS/Ready; provider opens count 2, while first `ResourceLive=true`. No exception
  escapes. Ten failed Q2 assertions establish the recovery outcome and subsequent reopening.
- **Impact:** primary failure metadata is preserved but session lifecycle recovery is unsound.
  The connection can accumulate abandoned provider resources and falsely claim readiness.
- **Provenance:** the same detachment/no-history structure existed before R1; completion cleanup
  exceptions already fell through to Failed. R1 preserves that unsafe recovery and now contains
  more failure paths into Failed. This is not claimed to be wholly newly introduced by R1.
- **Recommended bounded remediation:** preserve an explicit unrecoverable cleanup condition
  independently of the detached operation and prevent false recovery, while retaining primary
  protocol precedence and the at-most-once attempt invariant. Determine the exact internal
  implementation in the remediation commission; do not retry or silently clear evidence of failure.

### B3-B-Q2-F02 — RUN metadata exceptions bypass ownership and response containment

- **Severity:** high; **blocks B3-B qualification**.
- **Contract:** session owns every returned prepared result; deterministic provider exception
  handling; no cursor leak; no Streaming without the RUN metadata response.
- **Source:** `BoltConnectionSession.cs:310-345`, especially `:325` and `:336-342`;
  `IBoltOperation` ownership contract in `BoltSessionOptions.cs`.
- **Reproduction:** `Prepare` returns a tracked operation whose first `Fields` getter throws;
  call RUN, catch the escape in the test harness, then RUN again, Disconnect and Dispose.
  Also test a getter throwing on its second read, and a null field element failing response
  materialization. All are public interface implementations, with no concurrent calls.
- **Expected:** contain metadata failure, give the acquired operation exactly one guarded cleanup
  attempt, return the primary provider FAILURE, and make the session terminal if that cleanup
  fails. No abandoned local handle or premature Streaming outcome.
- **Observed:** first getter throws `InvalidOperationException`, leaves Ready/no installed
  operation/no response, zero cleanup attempts and null diagnostic. Another operation opens;
  even final teardown leaves the original at zero attempts. Second-getter/null-element cases
  escape as `InvalidOperationException`/`ArgumentNullException`, leave Streaming/installed, and
  return no RUN SUCCESS. Their cleanup only happens if the external caller later tears down.
  Eight failed Q2 assertions cover these variants.
- **Impact:** actual ownership gap and escaped failure, with possible leaked resources or invalid
  response sequencing. A provider throwing in its getter need not be malicious; preparation
  metadata can be computed lazily. The interface supplies no no-throw exemption.
- **Provenance:** metadata accesses and premature installation/state assignment are unchanged from
  the original B3-B source. This is an independently demonstrated pre-existing gap in the complete
  ownership claim, not an assertion that R1 added those lines.
- **Recommended bounded remediation:** include acquisition-through-metadata construction in the
  provider failure/ownership boundary, retire every successfully returned handle exactly once,
  and commit Streaming only after a valid RUN response can be produced. Preserve clean-disposal
  recoverability and terminal handling when cleanup fails. No production change was made here.

## 13. Retained qualifications and documentation obligations

All ten original Q1 §19 qualifications remain visible: bounded PULL(-1) batches; non-jump-ahead
RESET; implicit batch remainder ownership; wrong-typed TELEMETRY boundary gap; single-owner /
non-thread-safe API not explicitly documented on the type; provisional session/provider API;
bounded placeholder error-code fidelity; the two xUnit advisories; no cancellation token seam;
and the historical author-independence/attestation caveat. Current reviewer separation does not
retroactively provide an organizational attestation for historical evidence.

No qualification was silently promoted into a full compatibility claim. In particular, Q1's
broad “no resource leaks” statement cannot be retained for the newly demonstrated paths.
Diagnostic aliasing/retention documentation and the inaccurate remediation-class count are
non-blocking obligations. The unchanged Q1 X04 expectation and literal counter formatting are
evidence limitations, not additional production defects. There is no unresolved specification
question preventing the two demonstrated lifecycle defects from being adjudicated.

## 14. Evidence and owner handoff

The [evidence README](evidence/B3-B-Q2/README.md) explains provenance, exact commands, known
non-zero exits and reproduction. The manifest hashes all retained evidence and this report;
disposable binaries are excluded. The retained source files are reviewer probes and text
snapshots for audit only, not replacements for production or existing tests.

The next owner decision is whether to commission **B3-B-R2-IMPL-01** for Q2-F01 and Q2-F02,
followed by **B3-B-Q3**. Preserve the verified original F-02 correction and direct terminal
cleanup fixes. Owner acceptance should wait for that requalification. Work stops after the
qualification-only commit, without pushing or beginning B3-C/B5.
