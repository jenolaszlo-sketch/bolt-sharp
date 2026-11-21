# Q-B5B-05-OWNER-01 — Owner Acceptance and Closure

**Q-B5B-05 — ACCEPTED WITH QUALIFICATIONS — CLOSED.**

Decision date: 2026-11-20. This decision accepts and closes the retained `Q-B5B-05`
qualification ("Same-connection recovery after IGNORED not established") on the basis of the
independent **SUPPORTED WITH QUALIFICATIONS**
[Q-B5B-05-Q1 qualification](../reviews/Q-B5B-05-Q1-independent-qualification.md), executed on the
bounded [Q-B5B-05-PROBE-01 producer demonstration](../reviews/Q-B5B-05-PROBE-01-producer-report.md),
and in full consistency with the binding B5-B owner acceptance
([B5-B-OWNER-01](B5-B-OWNER-01-owner-acceptance.md)).

## Formal accepted records

1. **Same-connection server-side RESET recovery is QUALIFIED.** After the observed recoverable
   FAILURE/IGNORED sequence (`50N42 Neo.ClientError.Request.Invalid` FAILURE for RUN plus the
   byte-exact zero-field `IGNORED B0 7E` for pipelined PULL), the connection
   (a) enters `Failed` with the connection open, (b) answers a `RESET B0 0F` with the byte-exact
   `SUCCESS B1 70 A0`, returning the session to `Ready`, and (c) executes a subsequent valid
   operation successfully on the **same physical connection**, terminating orderly with
   `GOODBYE B0 02`. Independently reproduced by a reviewer-owned harness with byte-exact
   markers on Windows and Ubuntu WSL2, in plaintext and on one TLS loopback connection, over
   1/3/10 recovery cycles, with the four false-positive scenarios rejected
   (Q-B5B-05-Q1-F01/F02/F05).
2. **Physical connection identity preservation is QUALIFIED.** Every accepted scenario carries
   six independent identity lines — one accepted `conn-NNNN` across all protocol events,
   connection counters (`accepted=1 completed=1 failed=0`), unchanged client local port, one
   per-connection provider instance, byte-exact ordered transcript on one socket, and one
   handshake/authentication per connection — captured mid-sequence and after the orderly
   GOODBYE. Recovery on a new physical connection is **not** recovery of the original connection
   and was rejected by the oracle (Q-B5B-05-Q1-F02, NC-01).
3. **Official-driver post-failure same-connection RESET interoperability is NOT demonstrated and
   is not claimed.** With the pinned `Neo4j.Driver` 6.4.0, the driver does not RESET a
   connection that has received a recoverable FAILURE in the auto-commit path: it destroys the
   physical connection and the subsequent operation runs on a new physical connection with a new
   handshake and a new authentication, including on a retry of the same session and with a
   transaction-retry time configured (Q-B5B-05-Q1-F03, D-01..D-05).
4. **Official-driver connection replacement is ACCEPTED behavior** under the accepted bounded
   `50N42` classification (B5-B §19 / `Q-B5B-04`): drivers may treat refusals as unrecoverable
   per-connection; the behavior compromises reuse efficiency only, not correctness; no unsafe
   retry duplication exists. The driver does perform same-connection RESET→SUCCESS in its
   pooled-connection protocol (D-04), so the server capability is exercised — the missing piece
   is specifically the driver's post-failure path, which is driver behavior, not a production
   session-state defect.
5. **No production defect requiring remediation was identified.** No incorrect RESET state
   transition, no physical-connection identity failure, no false-positive recovery oracle, and
   no protocol contract violation was found by the independent qualification. Error metadata is
   protocol-correct per the accepted `Q-B3C-02` classification; `50N42`/`50N00` behavior is
   unchanged and must not change without a separate authorized contract assessment.

## Qualification of the reading applied

The closure follows the independent qualification's contract interpretation
([Q-B5B-05-Q1 evidence §02](reviews/evidence/Q-B5B-05-Q1/02-contract-interpretation.md)): the
original `Q-B5B-05` text requires demonstrating same-connection RESET recovery (agent-neutral)
and does not mandate driver-initiated RESET after FAILURE; B5-B §19/§20/§25 record the driver's
connection destruction as accepted behavior, and F02 refinement is explicitly "not required for
the current bounded acceptance". Fresh-connection recovery is not recorded as recovery of the
original connection, and no official-driver post-failure RESET interoperability is claimed.

## Owner scope of this acceptance

Accepted exactly for: the protocol-level same-connection RESET recovery after the recoverable
unsupported-query FAILURE on plaintext and TLS loopback, verified on Windows and Ubuntu WSL2 by
an independent reviewer (728 reproduced assertions, four rejected false positives), plus the
documented official-driver limitation under record 4.

**Not accepted and not claimed by this decision:** official-driver post-failure same-connection
RESET interoperability (record 3); a `50N42` classification change (Q-B5B-04 remains open and
separate; any change requires a separately authorized contract assessment and must not be
assumed to make the official driver reuse the connection); native Linux release qualification;
representative production capacity; full mid-flight RESET interruption; any transaction,
routing, or general-Cypher behavior; any other driver version.

## Evidence integrity and repository identity

| Object | Commit | Tree |
|---|---|---|
| Owner entry HEAD (Q1 qualification final) | `7704fe6a9400c24217cf327e3b5910669c2f7c70` | `ac9626dbcb714ec93c9e72ea5ab0b9de19aef89e` |
| Q1 integrity transcripts commit | `4c3b6895a7b1366521f80df44ebca371377b7d0e` | `f72ef1eb1212519045ba856696ded8f1e2c34f27` |
| Q1 TRX envelopes commit | `18bc294ebdb70d731e8361927c4a8cd6f5742981` | `d8969c086049053a2d588d37a83645ab40a586df` |
| Q1 report and evidence package commit | `4a8184790651ab6194955d02a1bee0860f80716a` | `94117c8680b4fd6d95373498bb35470af702c3ab` |
| Q1 qualified producer demonstration | `8d762dd50502260e32f569f42f9cbfa92e37b862` | `50f214a713ca3a490dcb437c831f9f881653a743` |

Each row's commit has the next row's commit as its sole parent. At entry, `main` was clean, 0
behind and 24 ahead of local `origin/main`, with one worktree and zero stashes. The owner
re-verified the repository identity and re-ran the self-verifying Q1 evidence package at the
entry HEAD (`VERIFICATION PASSED`: 57 package files over Git blobs, six TRX counters, both
platform result files, redaction).

## Findings disposition (Q-B5B-05-Q1-F01..F08)

| Finding | Owner disposition |
|---|---|
| F01 server-side recovery correct | **ACCEPTED** — qualification record 1. |
| F02 physical identity verified | **ACCEPTED** — qualification record 2; identity gate binding for any future claim. |
| F03 official-driver limitation | **ACCEPTED AS DOCUMENTED LIMITATION** — record 3/4; not a BoltSharp defect; not claimed as interoperability. |
| F04 metadata protocol-correct | **ACCEPTED** — `50N42`/`50N00` unchanged; classification remains within `Q-B5B-04`. |
| F05 oracle rejects false positives | **ACCEPTED** — control evidence; oracle integrity preserved. |
| F06 producer documentation precision (LOW) | **ACCEPTED, non-blocking** — producer package preserved as committed; future packages must distinguish blob-byte from worktree-byte hashing and keep evidence headers consistent with recorded classifications. |
| F07 platform coverage (inherited) | **ACKNOWLEDGED** — WSL2 is not native Linux. |
| F08 30 s read-timeout boundary (INFO) | **ACKNOWLEDGED** — accepted transport boundary; no contract impact. |

## Preserved qualifications and scope

| Qualification | Status after this decision |
|---|---|
| **`Q-B5B-05` — Same-connection recovery after IGNORED** | **CLOSED — ACCEPTED WITH QUALIFICATIONS** (records 1–5 above) |
| `Q-B5B-04` — Generic `50N42` refusal classification | **OPEN** (unchanged; optional refinement separately authorized) |
| `Q-B5B-09` — Release-grade hardening | **OPEN** (unchanged) |
| B5-B-TS1 | CLOSED (unchanged) |
| `Q-B5B-03` | CLOSED (unchanged) |
| Plaintext graceful-close remediation | CLOSED (unchanged) |
| TLS graceful-close remediation | DEFERRED (unchanged) |
| Observer isolation | DEFERRED BACKLOG (unchanged) |
| Native Linux release qualification | OUTSTANDING (unchanged) |
| Representative production capacity | OUTSTANDING (unchanged) |
| Portable typed-linger correction | ACCEPTED, Windows/WSL2 scope (unchanged) |

No production source, test, public API, project, package, dependency, historical evidence or
previously accepted owner decision was modified by this decision. The B5-B owner decision
(§23.2 identifier, §25 non-claim) is superseded in **status** by this decision and is not edited.
B4 and B6 are not authorized.

## Recommended next commission

With `Q-B5B-05` closed, the smallest appropriate next commission on the remaining roadmap is
**`Q-B5B-09-TLS` — bounded investigation of terminal TLS write backpressure and close-notification
behavior** (the DEFERRED TLS graceful-close remediation: Windows TLS terminal backpressure,
genuine close-notification interruption, encrypted-byte cap verification), using the accepted
loopback/TLS arrangements, before any release-grade graceful-close claim. Native Linux and
representative production capacity remain outstanding release gates requiring their own
authorization. `Q-B5B-04` remains open and separate, and no production change is implied by it
— the owner acceptance found no evidence that a classification change would make the official
driver reuse the connection.

This decision authorizes nothing beyond the acceptance and closure recorded here.

---

*Post-commit verification record (identity committed after this decision commit is created;
verifiable via `git log -1 --format='%H %T'` at the decision commit): the Q1 and owner evidence
verifiers pass; `main`, one worktree, clean; 25 ahead / 0 behind local `origin/main`; no push.*