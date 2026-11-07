# BOLTSHARP-B3-B-Q2 — Independent Remediation Requalification

## Role

Act as BoltSharp's independent protocol, lifecycle, and implementation qualification reviewer.

Independently requalify the completed remediation:

**B3-B-R1-IMPL-01**

Your responsibility is to determine whether the two demonstrated B3-B-Q1 findings have been correctly remediated without introducing new protocol, session-state, cleanup, compatibility, or recovery defects.

This is an independent qualification commission.

Do not implement remediation, perform owner acceptance, or begin B3-C/B5.

Approach the implementation adversarially. Reconstruct the state machine and relevant contracts independently before evaluating the producer's conclusions.

## 1. Repository baseline

Repository: BoltSharp, canonical checkout.

Expected starting state:

- Branch: `main`
- HEAD: `801badcea8523372d4ed83f8f2942d80bc0373e1`
- Tree: `243998eb21d01aa918f4b83143cb8a0c6bb27dd1`
- Implementation commit: `454ae005245d6511b672f8962092829839fb7153`
- Exactly one canonical worktree.
- Clean tracked files and index.
- No push.

Verify the baseline, current branch, worktree count, tracked/index state, and repository history before proceeding.

Inspect untracked files and preserve all existing artifacts.

If the starting HEAD or tree differs, stop and report the discrepancy. Do not reset, clean, rebase, prune, overwrite concurrent work, or create additional worktrees.

## 2. Binding evidence

Read the repository's authoritative B3-B specifications, implementation records, qualification reports, and remediation evidence.

Prioritize:

- B3-B protocol and session-state specification.
- B3-B-IMPL-01 implementation report.
- B3-B-Q1 independent qualification report and probes.
- B3-B-R1-IMPL-01 remediation report.
- `docs/reviews/evidence/B3-B-R1-IMPL-01/`
- Relevant B3-A message contracts.
- Applicable Bolt protocol specification and error/status contracts.

Inspect the actual implementation:

`src/BoltSharp/Sessions/BoltConnectionSession.cs`

Inspect the related tests:

- `tests/BoltSharp.Tests/Sessions/AuthenticationAndTransitionTests.cs`
- `tests/BoltSharp.Tests/Sessions/CleanupAndTelemetryRemediationTests.cs`

Inspect any other affected caller or protocol contract necessary to establish correctness.

Treat producer reports as claims requiring independent verification.

Do not change accepted architecture merely to make the remediation easier to qualify.

## 3. Qualification objectives

Independently determine:

1. Whether F-01 throwing-disposal behavior is corrected.
2. Whether terminal states remain valid under cleanup failures.
3. Whether cleanup is attempted at most once per owned operation.
4. Whether primary protocol and provider failures are preserved.
5. Whether F-02 TELEMETRY status mapping matches accepted contracts.
6. Whether X04's remaining Q1 probe mismatch is an outdated expectation or a real defect.
7. Whether `LastCleanupFailure` is a safe and compatible diagnostic addition.
8. Whether existing B3-B state transitions and recovery semantics remain intact.

Do not assume that passing producer tests establishes these claims.

## 4. F-01: Independent cleanup ownership reconstruction

Reconstruct the complete lifecycle of an active session operation.

Identify:

- Operation creation and installation.
- Ownership transfer to the session.
- Cursor ownership.
- Completion and cleanup.
- RESET-related cleanup.
- GOODBYE cleanup.
- Disconnect cleanup.
- Session disposal.
- Terminal failure cleanup.
- RUN-null-`Fields` rejection.
- Any path involving an operation that was created but not installed.

Establish which component owns each resource at every transition.

Determine whether every successful acquisition has exactly one responsible cleanup path.

Distinguish:

- Cleanup attempted.
- Cleanup completed successfully.
- Cleanup failed.
- Ownership detached.
- Session became terminal.

Do not treat detachment as proof that underlying resource disposal succeeded.

## 5. F-01: Exception containment

The producer reports introducing:

- `DetachAndDisposeOperation()`
- `DisposeGuarded()`
- `LastCleanupFailure`

Review the implementation against the accepted B3-B state machine.

Independently inspect every path where provider `Dispose()` may throw.

Test at least:

| Scenario | Required property |
|---|---|
| GOODBYE with throwing Dispose | No exception escape; terminal Defunct |
| Disconnect with throwing Dispose | No exception escape; terminal Defunct |
| Session Dispose with throwing Dispose | No exception escape; `_disposed` established |
| GOODBYE followed by Disconnect | No second disposal attempt |
| Disconnect followed by Dispose | No second disposal attempt |
| Repeated Dispose | Idempotent, no second disposal |
| Repeated GOODBYE | No second disposal |
| Late operation after terminal cleanup | Rejected without provider reopening |
| RUN-null-Fields, clean disposal | Failed; no installed cursor |
| RUN-null-Fields, throwing disposal | Defunct; no installed cursor |
| PULL completion, throwing cleanup | Required failure response and Failed state |
| RESET cleanup failure | FAILURE and Defunct |
| Provider primary failure plus Dispose failure | Primary failure preserved |
| Row-shape failure plus Dispose failure | Primary failure preserved |

For each probe, record:

- Starting state.
- Operation ownership.
- Injected failure.
- Protocol responses.
- Final session state.
- Disposal-attempt count.
- `LastCleanupFailure`.
- Whether any exception escaped.
- Whether the provider was reopened.

Design original probes rather than relying only on the remediation tests.

## 6. At-most-once cleanup invariant

Verify the producer's claim that disposal is attempted at most once for each operation.

Pay special attention to state changes across:

- GOODBYE → Disconnect.
- GOODBYE → Dispose.
- Disconnect → Dispose.
- Failed → RESET.
- Failed → Disconnect.
- Defunct → Dispose.
- Repeated terminal operations.
- RUN failure before cursor installation.
- PULL completion followed by session disposal.

Confirm that a failed disposal cannot be retried indirectly through another cleanup path.

Also assess the opposite risk: premature detachment causing a resource to become permanently unowned before a meaningful cleanup attempt.

The expected invariant is **at-most-once attempted disposal with explicit terminal failure accounting**, not a claim that disposal always succeeds.

## 7. Terminal-state and response correctness

Reconstruct the state transitions for:

- `Ready`
- `Streaming`
- `Failed`
- `Defunct`
- Any additional states defined by the accepted B3-B specification.

Determine which states permit:

- RUN.
- PULL.
- DISCARD.
- RESET.
- GOODBYE.
- TELEMETRY.
- Disconnect.
- Dispose.

For throwing-disposal paths, verify that no state transition can leave the session apparently reusable when cleanup is incomplete.

Check whether terminal state is established even when:

- Response emission fails.
- The provider throws during cleanup.
- A primary exception already exists.
- Session disposal is invoked repeatedly.

Do not infer correctness from a single final-state assertion. Trace state, resource ownership, and protocol output together.

## 8. X04 adjudication

The original B3-B-Q1 probe contained X04:

RUN returns null `Fields`, and provider disposal throws.

The producer reports that the original probe still has one failing expectation:

- Original expected behavior: no throw, `Failed`.
- Remediated behavior: no throw, `Defunct`, with `FAILURE`.

**Independently decide which behavior is correct.**

Inspect:

1. The accepted B3-B contract for RUN metadata failure.
2. The terminal-state contract when cleanup fails.
3. Whether the operation was ever installed.
4. Whether incomplete disposal permits subsequent RESET.
5. Whether the response sequence is protocol-correct.
6. Whether the session can safely be reused.

Consider whether a session with an incompletely disposed operation can legitimately remain merely `Failed`.

Classify X04 as one of:

- **EXPECTED CONTRACT CORRECTION:** Q1 expected `Failed`, but `Defunct` is required by the accepted cleanup/fail-closed contract.
- **REMEDIATION DEFECT:** The new behavior violates the accepted contract.
- **SPECIFICATION AMBIGUITY:** The authoritative contract does not resolve the difference.

Do not silently update the Q1 probe or redefine the specification.

If the correction is supported, provide an explicit contract-based explanation and recommend an updated future regression expectation.

If ambiguous, report the exact missing decision.

## 9. Primary-failure precedence

Verify that introducing guarded disposal does not replace or obscure the original failure.

Include cases where:

- RUN fails, then Dispose throws.
- PULL or DISCARD fails, then Dispose throws.
- Row validation fails, then Dispose throws.
- RESET cleanup fails.
- Provider operation creation fails before ownership transfer.
- An existing Failed state encounters an additional cleanup failure.

Determine:

- Which error is primary.
- Which error is recorded as cleanup diagnostic.
- Which protocol FAILURE is emitted.
- Whether the session remains recoverable.
- Whether a secondary cleanup error is incorrectly surfaced as the primary error.

Do not assume that preserving an exception object automatically preserves protocol-level error precedence.

## 10. F-02: TELEMETRY status requalification

The producer changed invalid integer TELEMETRY identifiers from:

- `22G03` / `Request.Invalid`

to:

- `50N00`
- `Neo.DatabaseError.General.UnknownError`

The producer cites the accepted specification's `TELEMETRY 9001` example.

Independently inspect that contract and the current message parsing rules.

Verify:

| Input | Expected behavior |
|---|---|
| 0 | SUCCESS → Ready |
| 1 | SUCCESS → Ready |
| 2 | SUCCESS → Ready |
| 3 | SUCCESS → Ready |
| -1 | FAILURE with accepted server-side status |
| 4 | FAILURE with accepted server-side status |
| 9001 | FAILURE with accepted server-side status |
| `long.MaxValue` | FAILURE with accepted server-side status |
| Non-integer field | Rejected by existing B3-A message boundary |
| Invalid TELEMETRY followed by work | IGNORED according to Failed-state rules |
| Invalid TELEMETRY followed by RESET | Existing recovery behavior preserved |

Confirm the actual metadata fields and values, not only the resulting session state.

Check whether the chosen status and Neo4j error code match the applicable accepted protocol version and repository contract.

Do not assume the specification example establishes a general rule without verifying its scope.

Do not broaden this commission into a redesign of TELEMETRY error classification.

## 11. `LastCleanupFailure` qualification

The remediation introduces an additive diagnostic property.

Determine:

- Its accessibility and public API impact.
- Whether the returned object exposes mutable internal state.
- Whether the diagnostic represents the latest cleanup failure or another documented concept.
- Whether it is cleared or retained across allowed transitions.
- Whether a successful RESET can leave misleading stale diagnostics.
- Whether recording the diagnostic itself can throw.
- Whether the property could imply a different session state than the actual state.

Assess binary and source compatibility with existing consumers.

Do not demand an API change merely for stylistic preference.

If behavior is not specified, distinguish a documentation obligation from a correctness defect.

## 12. Recovery and regression preservation

Verify existing B3-B behavior remains intact, including:

- Authentication and initial state.
- RUN/PULL/DISCARD lifecycle.
- Bounded `PULL(n=-1)` behavior.
- Failed/IGNORED/RESET sequencing.
- Non-jump-ahead RESET.
- GOODBYE terminal behavior.
- Disconnect semantics.
- Session disposal idempotence.
- TELEMETRY behavior.
- Cursor lifecycle and batch ownership.
- Provider failure boundaries.

Preserve previously accepted qualifications unless independently demonstrated to be resolved.

Do not extend B3-B into B3-C transaction functionality.

## 13. Required independent execution

Run relevant existing and independently authored tests.

At minimum:

1. Build affected projects.
2. Run the B3-B remediation tests.
3. Run original B3-B-Q1 probes unchanged.
4. Run original independent cleanup probes.
5. Execute new adversarial terminal-state probes.
6. Execute new TELEMETRY metadata probes.
7. Check `LastCleanupFailure`.
8. Run session regression tests.
9. Run core tests.
10. Run message tests.
11. Run B0-B integration tests.
12. Inspect the final source and public API delta.

Producer baselines:

- Core: 1,025 passed.
- Session: 101 passed.
- Messages: 98 passed.
- B0-B integration: 19 passed.
- Two pre-existing `xUnit2012` advisories.
- Original Q1 probe: 118 checks, one stale X04 mismatch.

Treat these as producer claims until independently reproduced.

Report exact test counts, warnings, errors, and exclusions.

Do not claim the original Q1 probe passes unchanged when its X04 expectation still differs.

## 14. Independent findings

For each material finding, record:

- Finding ID.
- Severity.
- Contract or invariant affected.
- Source location.
- Reproduction procedure.
- Expected result.
- Observed result.
- Protocol and state-machine impact.
- Whether it blocks B3-B qualification.
- Recommended remediation or retained obligation.

Distinguish:

- Demonstrated implementation defects.
- Specification ambiguity.
- Stale test expectations.
- New public API or compatibility concerns.
- Existing accepted qualifications.
- Non-blocking documentation issues.

Do not classify a theoretical concern as a demonstrated defect without a reachable execution path.

## 15. Formal Q2 disposition

Issue exactly one:

**SUPPORTED**

Both F-01 and F-02 are independently verified as corrected without material remaining qualifications.

**SUPPORTED WITH QUALIFICATIONS**

The remediation satisfies the required contracts with bounded residual obligations.

**REMEDIATION REQUIRED**

A demonstrated correctness, protocol, disposal, or terminal-state defect remains.

**INCONCLUSIVE**

Required evidence is missing or the accepted contract cannot resolve a material question.

Do not issue owner acceptance.

Do not authorize B3-C or B5.

## 16. Deliverables

Create:

`docs/reviews/B3-B-Q2.md`

Preserve independent reviewer evidence under:

`docs/reviews/evidence/B3-B-Q2/`

Include:

1. Verified baseline and reviewed implementation revision.
2. Independent cleanup and ownership analysis.
3. Exception-containment results.
4. At-most-once disposal evidence.
5. Terminal-state transition matrix.
6. X04 adjudication.
7. Primary-failure precedence analysis.
8. TELEMETRY metadata verification.
9. `LastCleanupFailure` compatibility assessment.
10. Original and new probe results.
11. Full regression results.
12. Findings and retained qualifications.
13. Formal Q2 disposition.
14. Recommended next owner decision or remediation commission.

Retain sufficient evidence for reproducibility.

Use external scratch space for disposable binaries and generated probe artifacts where practical.

Do not modify existing Q1 evidence or historical reports.

## 17. Repository restrictions

- Work on canonical `main`.
- Keep exactly one worktree.
- No production or existing test modifications.
- No remediation.
- No B3-C or B5 implementation.
- No owner acceptance.
- No public API redesign.
- No branch, reset, clean, rebase, or history rewrite.
- Preserve all existing untracked artifacts.
- No push.

Commit only qualification reports and retained reviewer evidence.

If a material defect is found, document it and stop rather than modifying production code.

## 18. Final handoff

Report:

1. Starting and final HEAD/tree.
2. Implementation revision reviewed.
3. Files changed.
4. Independent F-01 assessment.
5. X04 classification and justification.
6. Cleanup/disposal probe outcomes.
7. Terminal-state and recovery behavior.
8. F-02 TELEMETRY assessment.
9. `LastCleanupFailure` findings.
10. Executed test counts.
11. Build warnings/errors.
12. Remaining qualifications.
13. Formal Q2 disposition.
14. Recommended next commission.
15. Branch, worktree, cleanliness, and push status.

If supported, recommend:

`B3-B-OWNER-ACCEPTANCE-01`

**Stop after committing the independent Q2 report and evidence. Do not remediate, perform owner acceptance, begin B3-C/B5, or push automatically.**