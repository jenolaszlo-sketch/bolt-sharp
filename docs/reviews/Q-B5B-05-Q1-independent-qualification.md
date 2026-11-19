# Q-B5B-05-Q1 — Independent Same-Connection RESET Recovery Qualification

**Formal disposition: SUPPORTED WITH QUALIFICATIONS** (§12 below).

Independent qualification of the completed `Q-B5B-05-PROBE-01` producer demonstration
(`8d762dd`, tree `50f214a`; base `e0b762a`). This commission independently qualifies (a) whether
server-side RESET recovery after a recoverable Bolt FAILURE is correct, and (b) whether the
official-driver observations are independently reproducible. It does **not** issue owner
acceptance, does **not** close `Q-B5B-05`, does **not** modify production code, and does **not**
start `Q-B5B-04` remediation.

Producer disposition under review: **PRODUCTION LIMITATION IDENTIFIED — OWNER DECISION REQUIRED.**
This qualification does not assume that disposition; it re-derives every requirement from the
binding contracts.

Evidence package: [`docs/reviews/evidence/Q-B5B-05-Q1/`](evidence/Q-B5B-05-Q1/README.md).

---

## 1. Reviewer independence

- The reviewer session is fresh and executed with a different model/provider than the producer
  session.
- The reviewer did **not** author the probe project (`docs/reviews/probes/Q-B5B-05-PROBE-01/`),
  did **not** execute the producer verification, and did **not** write the producer report,
  evidence package, or any producer conclusion.
- Producer artifacts were consumed as **input**, verified against the pinned Git objects, and
  never as proof of correctness. Where producer text conflicted with its own artifacts, the
  artifacts (and this reviewer's reproduction) prevail — recorded as finding F06.
- All qualification requirements were re-derived from the binding contracts before any execution
  (`02-contract-interpretation.md`).
- All reviewer executions ran in two independent clones (`reviewer`, `reviewer-wsl2`); the
  canonical tree was used read-only, apart from this documentation commit.

## 2. Starting repository identity

Verified at commission start (`01-preflight.txt`):

| Property | Expected | Actual |
|---|---|---|
| Branch | `main` | `main` ✓ |
| HEAD | `8d762dd50502260e32f569f42f9cbfa92e37b862` | identical ✓ |
| Tree | `50f214a713ca3a490dcb437c831f9f881653a743` | identical ✓ |
| Parent | `e0b762a5ae716544c7d3a5fa3f4e7c08372b7ac1` | identical ✓ |
| Divergence | 20 ahead / 0 behind local `origin/main` | `020` (0 behind) ✓ |
| Worktrees | 1 | 1 ✓ |
| Stashes | 0 | 0 ✓ |
| Working tree | clean | clean (only this commission's own untracked package) ✓ |

Producer commit: 54 added files (all status `A`, +21,297 insertions), additions-only, confined to
the producer report, the probe project (9 source files, no build artifacts) and the evidence
package. **No production source, existing test, public API, project, package or historical
evidence change** (re-verified for commission §14; nothing reset or overwritten).

## 3. Evidence integrity

`09-integrity-verification.txt`:

- Producer self-verification: **PASSED** (51 files, six TRX envelopes, 26 traces, redaction,
  recorded audit, repository invariants).
- Reviewer-**independent** verification (own tooling over Git blobs — not the producer code):
  **51/51** manifest entries match recomputed SHA-256, byte counts and Git blob IDs; six TRX
  envelopes independently re-parsed (13 / 1,257 / 43 counter sets); redaction scan clean.
- Checkout portability: **24** files are full CRLF renders of LF blobs; **3** Windows TRX files
  are the producer's original mixed-line-ending files (100 CRLF + 5 LF-only lines ending in
  `&#xD;` entities); **all 51** working-tree files re-hash to their committed blob IDs under the
  repository filters (`git hash-object --path`). This is **inherited checkout behavior** (same
  class as the prior Q2 checkout finding: `core.autocrlf=true`), **not** a defect in this
  package; nothing was normalized.
- Documentation precision (accepted non-blocking; F06): the manifest's two self/audit
  exclusions; the acquisition-RESET over-generalization in `driver-behavior-assessment.md` §1;
  the D-03 artifact header wording vs. its own `retriable=False` record.

## 4. Original Q-B5B-05 contract interpretation

Verbatim (B5-B-OWNER-01 §23.2):

> **Q-B5B-05 — Same-connection recovery after IGNORED not established.** After the observed
> FAILURE/IGNORED sequence the driver destroys the connection; RESET recovery of that connection
> was not demonstrated.

Derivation (`02-contract-interpretation.md`), including §19 (F02: drivers may discard such
connections; `50N42` must not change), §20 (retained qualification), §25 (explicit non-claim),
the B5-B-TS1 closure (OPEN), and the B5-B-F01-R1 closure ("recovery on a new physical connection
is not recovery of the original connection"):

1. **The contract requires demonstrating the server capability for same-connection RESET recovery
   after the recoverable FAILURE.** The gap "RESET recovery of that connection was not
   demonstrated" is agent-neutral and is discharged by establishing that the recovery is possible
   and correct on one physical connection.
2. **The contract does not mandate driver-initiated same-connection RESET.** It records the
   driver's connection destruction as accepted behavior; no owner decision requires the driver
   to send RESET, and F02 refinement is explicitly "not required for the current bounded
   acceptance".
3. An alternative reading — that the driver path specifically was intended (B5-B being an
   interoperability milestone) — is recorded, not adopted; that fork is the owner's Option B,
   not a reviewer override.

Consequences honored: no silent weakening (fresh-connection recovery is not same-connection
recovery; no oracle relaxed), and no silent broadening (no official-driver post-failure RESET
interoperability is claimed anywhere in this qualification).

## 5. Server-side RESET recovery (F01)

Reproduced with a **reviewer-owned harness** (own raw Bolt 5.8 client, byte-exact assertions, own
TLS, own provider-identity counter, own diagnostics capture; no producer code reused). The full
commission flow: connection → negotiation → authentication → valid operation → recoverable
`50N42` FAILURE → session `Failed` → byte-exact `IGNORED B0 7E` → `RESET B0 0F` → byte-exact
`SUCCESS B1 70 A0` → `Ready` → subsequent successful operation (`RECORD B1 71 91 2A`) → `GOODBYE
B0 02` → clean completion.

| Reviewer scenario | Shape | Assertions |
|---|---|---|
| R-01 | commission 15-step flow, separate writes | 28 |
| R-02 | three recoverable FAILURE/RESET cycles | 44 |
| R-03 | the exact Q-B5B-05 pipelined RUN+PULL (one write) | 28 |
| R-04 | TLS loopback, same sequence | 24 |
| R-05 | concurrent healthy client during failure | 26 |
| R-06 | ten recovery cycles (bounded repeated recovery) | 100 |

**15/15 scenarios, 364/364 assertions, 0 failures on each platform** (728 total). RESET admissibility
verified against the production contract: `Ready`/`Streaming`/`Failed`/`Interrupted` → `Ready`;
outside → terminal violation (NC-04/NC-03 evidence). Terminal Defunct failures were never
classified as recoverable. State evidence: server traces `state=Failed` during ignored work,
`state=Ready` after RESET, per connection. Byte-exactness: equality assertions, not substring
searches (`04-protocol-traces.md`).

## 6. Same-physical-connection identity (F02)

The principal gate. Six independent lines, verified per accepted scenario on both platforms:
one accepted `conn-NNNN` carrying every `MessageProcessed` event; counters `accepted=1 active=1
completed=0 failed=0` mid-sequence and `accepted=1 completed=1 failed=0` post-GOODBYE; unchanged
client local port; one provider instance (production factory invoked once per negotiated
connection); byte-exact ordered transcript on one socket; one handshake and one LOGON per
connection (`03-identity-matrix.md`). Verified absent: replacement socket, additional handshake,
reauthentication, silent retry on another connection, new provider instance. A successful query
on a reused logical session, or after pool reconnection, was **not** used as evidence (and is
affirmatively falsified in D-02/D-05: success on a *different* physical connection).

## 7. Official-driver behavior (F03/F04)

Independently reproduced with the pinned **Neo4j.Driver 6.4.0** through a **reviewer-owned
recording proxy** (own handshake skipping with LEB128-robust varints, own transcript; no producer
harness reuse):

| ID | Case | Independent result (both platforms) |
|---|---|---|
| D-01 | pipelined RUN+PULL refusal | `ProtocolException code=Neo.ClientError.Request.Invalid gqlStatus=50N42 gqlClassification=UNKNOWN retriable=False`; wire `FAILURE → IGNORED B0 7E`; connection closed |
| D-02 | recovery after failure | **new physical connection** (new handshake, new authentication); connection #1 ends at `IGNORED`, no RESET; server `accepted=2 failed=0`; providers `#1 prepare=1 success=0`, `#2 prepare=1 success=1` |
| D-03 | `50N00` with `WithMaxTransactionRetryTime(5s)` | `DatabaseException 50N00 gqlClassification=UNKNOWN retriable=False`; **no** RESET, **no** retry, `GOODBYE` on the same connection (retry eligibility is a classification result, not a policy result) |
| D-04 | pooled reuse, three sessions | **one** physical connection; clean sessions each followed by `RESET B0 0F → SUCCESS B1 70 A0`; dirty sessions add three more same-connection RESETs; server-side counts match the proxy |
| D-05 | second run on the same failed session | returns `42` — on a **new** physical connection with new handshake/authentication; no RESET on the original |

Every material producer D-observation is independently reproduced. Error-metadata check: the
server's refusal metadata (`gql_status 50N42`, `neo4j_code Neo.ClientError.Request.Invalid`,
description, `diagnostic_record {}`) is the accepted protocol-correct classification
(Q-B3C-02/B5-B §19); the driver's behavior follows from its non-retriable treatment of that
class. No BoltSharp change is indicated; `50N42`/`50N00` behavior stays unchanged; any
classification question remains inside `Q-B5B-04` and was not started here.

## 8. Plaintext and TLS results

R-01/02/03/05/06 plaintext and R-04 TLS (TLS before Bolt, no STARTTLS, self-signed loopback
certificate accepted in-process, no trust store touched): all pass both platforms with the full
identity matrix; `TlsHandshakeFailures=0`; the same physical encrypted socket carries the whole
sequence (client port unchanged; server-side identity lines only, as a passive tap is blind
inside TLS). Protocol consistency, session state, provider lifecycle and connection accounting
are identical across plaintext and TLS, and identical across Windows and WSL2.

## 9. Negative-control results (F05)

Reviewer-owned executions (same harness, own injections): **all four detected**, both platforms
(`06-negative-controls.md`): NC-01 replacement connection (verdict `False`;
`accepted=2`, two ids, two provider instances, ports differ); NC-02 missing RESET (work stays
byte-exact `IGNORED B0 7E`, session stays `Failed`, provider not consulted until the RESET);
NC-03 disposal failure during RESET retirement (terminal `50N00`, never `B1 70 A0`, one disposal
attempt, Defunct, no further work — fail-closed B3-B-R2 reconfirmed); NC-04 terminal `08N06`
substitution (no RESET response at all, provider never consulted). Fault paths reached; no
unrelated fixture failure counted as detection.

## 10. Platform and regression campaigns (F07)

Both platforms, reviewer-run in independent clones:

| Suite | Windows | Ubuntu WSL2 |
|---|---|---|
| Reviewer harness (15 scenarios / 364 assertions) | 15/15, 0 failures | 15/15, 0 failures |
| Producer probe suite (13 tests) | 13/0/0 | 13/0/0 |
| Core solution (`BoltSharp.slnx`, 1,257 tests) | 1,257/0/0 | 1,257/0/0 |
| Official-driver interoperability (43 tests) | 43/0/0 | 43/0/0 |
| Focused: RESET/failure/disposal | 36/0/0 | 36/0/0 |
| Focused: multi-client accounting | 6/0/0 | 6/0/0 |
| Focused: plaintext transport | 36/0/0 | 36/0/0 |
| Focused: TLS integration | 9/0/0 | 9/0/0 |
| Focused: unsupported-command refusal | 10/0/0 | (covered by full core) |
| Interop `B5B2_06` (RESET→GOODBYE through the driver) | 1/0/0 | 1/0/0 |
| Bounded repeats (lifecycle races) | 5 × 34/0/0 | 3 × 34/0/0 |

Environments — Windows NT 10.0.26200.0, SDK 10.0.401, `Microsoft.NETCore.App 8.0.31`; WSL2
`6.6.114.1-microsoft-standard-WSL2`, Ubuntu 26.04.1 LTS, SDK 10.0.114 + `DOTNET_ROLL_FORWARD
=Major` (only .NET 10 installed there — inherited). Builds: solution 0 errors (2 pre-existing
`xUnit2012` advisories in `BoltRequestDecodeTests`); harness 0 errors/0 warnings; probe project
0 errors in both clones. Full records: `07-campaign-windows.md`, `08-campaign-wsl2.md`,
`windows-focused-campaign.txt`, `wsl2-focused-campaign.txt`. No failures to preserve — none
occurred. No infrastructure or cost incurred; native Linux remains an unclaimed inherited gate.

## 11. Findings and limitations (F01–F08)

`10-findings-register.md`. Summary: F01 server recovery correct (none); F02 identity verified
(none); F03 driver limitation (HIGH as a limitation, not a BoltSharp defect); F04 metadata
protocol-correct, classification unchanged (none); F05 oracle rejects all false positives (none);
F06 evidence intact + 3 documentation-precision notes (LOW); F07 platform coverage complete,
native Linux outstanding (inherited); F08 30 s `MessageReadTimeout` boundary closes an idle
failed connection (INFO, accepted transport boundary — observed during diagnostics; every
in-window recovery succeeds).

**Limitations of this qualification:** official-driver post-failure RESET interoperability is not
demonstrated and not claimed; WSL2 is not native Linux; representative production capacity is not
characterized; full mid-flight RESET interruption remains excluded by the engine's bounded RESET
semantics; driver internals beyond wire observations were not inferred.

## 12. Formal qualification disposition

**SUPPORTED WITH QUALIFICATIONS.**

Basis (commission §16):

- **An incorrect RESET state transition:** none found — byte-exact markers and state traces on
  both platforms, including across 1/3/10 cycles and TLS.
- **Physical-connection identity failure:** none — six identity lines satisfied per accepted
  scenario; the replacement-connection defect is detected by the oracle rather than accepted.
- **False-positive recovery oracle:** none — all four controls detected by their intended
  oracles.
- **Protocol contract violation:** none.
- The original acceptance requirement (§4 above) permits closing on the demonstrated server-side
  capability with a documented official-driver limitation **because it does not mandate
  driver-initiated RESET** (§19/§20/§25 record the driver behavior as accepted). Had it mandated
  that, this commission could not declare it satisfied — the driver version's behavior is
  recorded instead.

The qualifications carried are explicit: (i) official-driver post-failure same-connection RESET
interoperability is **not** demonstrated and **not** claimed — the driver destroys the connection
under the accepted classification; (ii) the disposition rests on the contract reading in §4,
which the owner may read differently (owner Option B); (iii) `Q-B5B-04` (classification) and
`Q-B5B-09` (release hardening) remain open and separate.

## 13. Recommended owner decision

`11-owner-recommendation.md`. **Option A — RECOMMENDED:** accept the demonstrated server-side
same-connection RESET recovery and close `Q-B5B-05` with the documented official-driver
limitation, because the binding acceptance contract permits it and the evidence is complete
(728 reviewer assertions, binary platforms, four rejected false positives). **Option B** (keep
`Q-B5B-05` open pending a separately authorized `Q-B5B-04` investigation) remains available if
the owner reads the requirement as the driver path. This commission authorizes neither; no
production change is proposed or implied (and no evidence indicates one would change the driver
behavior).

## 14. Remaining qualifications (preserved, unchanged)

| Qualification | Status after Q1 |
|---|---|
| B5-B-TS1 | CLOSED (unchanged) |
| Q-B5B-03 | CLOSED (unchanged) |
| Q-B5B-04 | OPEN (classification; **not** started by Q1) |
| Q-B5B-05 | OPEN (this commission qualifies; the owner decides) |
| Q-B5B-09 | OPEN (unchanged) |
| Plaintext graceful-close remediation | CLOSED (unchanged) |
| TLS graceful-close remediation | DEFERRED (unchanged) |
| Observer isolation | DEFERRED BACKLOG (unchanged) |
| Native Linux release qualification | OUTSTANDING (unchanged) |
| Representative production capacity | OUTSTANDING (unchanged) |
| Portable typed-linger correction | ACCEPTED (Windows and WSL2 scope; unchanged) |

B4 and B6 were not started. No owner decision was modified; no historical evidence was touched;
no public API, dependency, or protocol expansion was introduced by this demonstration.

## 15. Evidence commit and tree identities

This report and the `docs/reviews/evidence/Q-B5B-05-Q1/` package are committed to `main` as an
additions-only documentation commit. The commit and tree identities are recorded in the session
handoff after the commit, and are verifiable via `git log -1 --format='%H %T'` at the
qualification commit. Post-commit verification: re-running both evidence verifiers against the
committed tree, and re-deriving the package from a fresh checkout.

## 16. Final repository cleanliness and divergence

Recorded post-commit in the session handoff: working tree clean (tracked), one worktree, zero
stashes, no push, divergence `origin/main…HEAD` inspected (21 ahead / 0 behind before push; this
commission does not push).

## 17. Recommended next commission

- **Owner decision on `Q-B5B-05`** (Option A or B, §13) — the smallest next step after this
  qualification; or
- **Q-B5B-09-INV-04**-style bounded investigation only under separate authorization —
  explicitly not started here.
- `Q-B5B-04` (classification) remains a separately authorized investigation; **do not assume a
  classification change would make the official driver reuse the connection** — no evidence in
  this commission supports that assumption.