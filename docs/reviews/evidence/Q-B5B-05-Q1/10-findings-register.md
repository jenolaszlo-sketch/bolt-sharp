# Findings Register — Q-B5B-05-Q1

Commission §15. Stable identifiers, per category. Severities: BLOCKING / HIGH / MEDIUM / LOW /
INFO. A finding records requirement, independent evidence, reproduction, severity, impact and the
required action or qualification.

---

## Q-B5B-05-Q1-F01 — Server-side RESET recovery after a recoverable FAILURE is correct

**Category: A. Server protocol correctness.**

- **Requirement.** Q-B5B-05: same-connection RESET recovery after the observed recoverable
  FAILURE/IGNORED sequence — the server capability gap "RESET recovery of that connection was not
  demonstrated" (`02-contract-interpretation.md` §3.1). Contract-derived sequence and markers:
  `50N42` FAILURE → byte-exact `IGNORED B0 7E` → `RESET B0 0F` → `SUCCESS B1 70 A0` → Ready →
  subsequent successful operation → `GOODBYE B0 02` completion.
- **Independent evidence.** Reviewer-owned harness, both platforms: 15/15 scenarios, 364/364
  assertions (`windows/q5b05q1-reviewer-results.json`, `wsl2/q5b05q1-reviewer-results.json`).
  Covers the commission's 15-step flow byte-for-byte (R-01), the exact pipelined Q-B5B-05 shape
  (R-03), 1/3/10 recovery cycles (R-01/02/06), TLS (R-04, `TlsHandshakeFailures=0`), concurrent
  clients (R-05), failed-state IGNORED and state evidence (server trace `state=Failed` /
  `state=Ready`), RESET in all applicable states, disposal-failure terminality and terminal
  refusal (NC-03/04). Independent decoding of the producer's 26 traces corroborates
  (`09-integrity-verification.txt`).
- **Reproduction.** Harness source in the session scratch tree; command in
  `07-campaign-windows.md` §2 and `08-campaign-wsl2.md` §2.
- **Severity.** None (demonstrated capability).
- **Impact.** The server-side recovery gap recorded by Q-B5B-05 no longer exists: the sequence is
  protocol-correct, byte-exact and repeatable on both platforms, plaintext and TLS.
- **Required action/qualification.** Commit as the demonstrated server capability; carry the
  driver-channel limitation separately as F03/F05. No production change.

## Q-B5B-05-Q1-F02 — Same-physical-connection identity holds across the full recovery sequence

**Category: B. Same-physical-connection identity.**

- **Requirement.** Commission §7 (principal gate): one physical connection from initial
  authentication through recovered operation and GOODBYE, by multiple independent observations.
- **Independent evidence.** `03-identity-matrix.md`. Six identity lines verified per accepted
  scenario on both platforms: exactly one accepted `conn-NNNN` carrying every
  `MessageProcessed` event; counters `accepted=1 active=1 completed=0 failed=0` mid and
  `completed=1 failed=0` post; unchanged client local port mid-sequence; one provider instance;
  byte-exact ordered transcript on one socket; one handshake and one LOGON per connection.
  Mid-sequence and post-GOODBYE verdicts are separate assertions.
- **Reproduction.** As F01; verdict lines quoted verbatim in the matrix.
- **Severity.** None (verified).
- **Impact.** Recovery-on-a-different-connection cannot be accepted as satisfying the
  qualification (see F01-R1 closure: "recovery on a new physical connection is not recovery of
  the original connection").
- **Required action/qualification.** None. A successful query on a reused logical session or
  after pool reconnection remains explicitly insufficient evidence.

## Q-B5B-05-Q1-F03 — Official driver does not RESET a recoverable-failed connection; recovery is a new physical connection

**Category: C. Official-driver behavior.**

- **Requirement.** Commission §9: independently reproduce D-01..D-04 with Neo4j.Driver 6.4.0.
- **Independent evidence.** `05-driver-behavior-assessment.md`, both platforms. D-01:
  `ProtocolException code=Neo.ClientError.Request.Invalid gqlStatus=50N42 gqlClassification=UNKNOWN
  retriable=False`, wire `FAILURE → IGNORED B0 7E`. D-02: recovery on a **new** physical
  connection (new handshake, new authentication; providers `#1 prepare=1 success=0`,
  `#2 prepare=1 success=1`; server `accepted=2 failed=0`), connection #1 ending at `IGNORED` with
  no RESET. D-03: `DatabaseException 50N00 gqlClassification=UNKNOWN retriable=False` **with
  `WithMaxTransactionRetryTime(5s)`** — no RESET, `GOODBYE` on the same connection, no retry
  (retry eligibility is not conferred by retry policy). D-04: one physical connection for three
  clean sessions, each followed by `RESET B0 0F → SUCCESS B1 70 A0`; dirty-session variant adds
  three more same-connection RESETs. D-05: even a second run on the same failed session
  transparently uses a new physical connection.
- **Reproduction.** As F01; per-scenario evidence in `windows/d0*.txt`, `wsl2/d0*.txt`.
- **Severity.** HIGH (for the operational picture), **not a BoltSharp defect**.
- **Impact.** The retained qualification's operational content — "the driver destroys the
  connection instead of sending RESET" — is independently confirmed with the pinned driver, and
  the driver provably possesses the same-connection RESET capability in its pooled protocol. The
  limitation is a driver behavior interacting with the accepted 50N42 classification, not a
  production session-state defect (F01 disproves a defect).
- **Required action/qualification.** Record as a documented official-driver limitation; do not
  claim official-driver post-failure RESET interoperability (none was demonstrated); owner
  decision per `11-owner-recommendation.md`.

## Q-B5B-05-Q1-F04 — BoltSharp's failure metadata is protocol-correct; classification must not change under this commission

**Category: D. Error classification and retry eligibility.**

- **Requirement.** Commission §10: determine whether BoltSharp is returning protocol-correct
  error metadata; distinguish observed behavior from inferred internals; do not conclude a
  production change from the driver's reconnection choice.
- **Independent evidence.** The server's refusal is `gql_status 50N42` +
  `neo4j_code Neo.ClientError.Request.Invalid` + description + `diagnostic_record {}`, exactly
  the accepted B3-C/Q-B3C-02 classification, reconstructed from the wire in every raw and driver
  trace on both platforms. Observed driver mapping (F03) is consistent with the driver's
  documented non-retriable treatment of `Neo.ClientError` codes; nothing in the observed wire
  behavior is inconsistent with either side's contract. Driver internals beyond the wire
  (pool policy implementation, session failure policy implementation) are **not** inferred.
- **Severity.** None (no defect found).
- **Impact.** The driver's connection destruction is a classification-driven policy choice the
  owner already accepted under B5-B §19/F02; it is not evidence of a BoltSharp error-metadata
  defect.
- **Required action/qualification.** `50N42`/`50N00` behavior stays unchanged; any
  classification question remains inside Q-B5B-04 (OPEN) and is **not** started by this
  commission. Do not change error classification merely because the driver reconnects, and do
  not assume a classification change would make the driver reuse the connection.

## Q-B5B-05-Q1-F05 — Same-connection oracle rejects all four false-positive recovery shapes

**Category: E. Evidence and oracle integrity.**

- **Requirement.** Commission §11: repeat or independently challenge NC-01..NC-04; confirm the
  intended oracle detects each defect; reject unrelated fixture failures.
- **Independent evidence.** `06-negative-controls.md`, `windows/nc0*.txt`, `wsl2/nc0*.txt`.
  NC-01 replacement connection → verdict `SAME_PHYSICAL_CONNECTION=False` (accepted=2, two ids,
  two provider instances, two ports). NC-02 missing RESET → work stays byte-exact `IGNORED B0 7E`,
  session stays `Failed`, provider not consulted until the RESET. NC-03 disposal failure during
  RESET retirement → terminal `50N00` FAILURE (never `B1 70 A0`), one disposal attempt, Defunct,
  no further work. NC-04 terminal `08N06` substitution → no RESET response at all, provider never
  consulted. Detection in each control is the intended oracle's assertion, with the fault path
  reached (armed once, observed once).
- **Severity.** None (controls effective).
- **Impact.** The producer's oracle claims are independently confirmed; a passing producer
  scenario cannot be a false positive.
- **Required action/qualification.** None.

## Q-B5B-05-Q1-F06 — Producer evidence package is intact; three documentation-precision issues (non-blocking)

**Category: E. Evidence and oracle integrity.**

- **Requirement.** Commission §5: independently verify the producer package; distinguish
  inherited checkout behavior from defects; do not normalize files.
- **Independent evidence.** `09-integrity-verification.txt`: producer self-verification passes;
  reviewer-independent manifest check 51/51 (SHA-256, bytes, Git blob IDs over blob payloads);
  six TRX envelopes independently re-parsed; redaction scan clean; all 51 working-tree files
  re-hash to committed blob IDs under repo filters (24 full CRLF renders + 3 Windows TRX
  mixed-line-ending originals = inherited `core.autocrlf` checkout behavior, identical in class
  to the Q2 checkout finding — not evidence defects).
- **Precision issues (documentation, not evidence integrity):**
  1. `driver-behavior-assessment.md` §1 states the two acquisition RESETs were "observed in
     D-01, D-02, D-03 and D-04"; the package's own transcripts show no acquisition RESETs on the
     D-04 connection or on D-02's recovery connection #2 (acquisition RESETs appear only on the
     failing connections of D-01/D-02#1/D-03). Substance unaffected (all RESET observations in
     this Q1 were re-derived independently).
  2. The D-03 artifact header line calls the `50N00` failure "a retriable database error" while
     its own recorded classification is `retriable=False`. Header wording only.
  3. `README.md` says the manifest hashes "every package file (except itself)"; it also excludes
     `verify-evidence.py` (self-referential) and `q5b05-evidence.json` (checked semantically by
     the verifier instead).
- **Severity.** LOW (documentation precision).
- **Impact.** None on the qualifying conclusions; no weakening of accepted oracles; no
  normalization of historical evidence was performed.
- **Required action/qualification.** Preserve the producer package as committed; record these as
  accepted non-blocking documentation notes. Future producers should distinguish blob-byte from
  worktree-byte hashing and keep evidence headers consistent with recorded classifications.

## Q-B5B-05-Q1-F07 — Platform coverage complete for Windows and WSL2; native Linux outstanding

**Category: F. Platform limitations.**

- **Requirement.** Commission §12: execute on Windows and Ubuntu WSL2; record versions; no new
  infrastructure.
- **Independent evidence.** `07-campaign-windows.md`, `08-campaign-wsl2.md`. Windows NT
  10.0.26200.0 / SDK 10.0.401 / `Microsoft.NETCore.App 8.0.31`; WSL2 `6.6.114.1` / Ubuntu
  26.04.1 LTS / SDK 10.0.114 with `DOTNET_ROLL_FORWARD=Major` (only .NET 10 installed there —
  inherited arrangement). Protocol, identity, state, lifecycle, TLS continuity, accounting and
  cleanup results are identical across platforms; fully-independent reproduction on both
  (2 × 364 assertions).
- **Severity.** None (inherited limitation retained).
- **Impact.** None beyond the inherited release gate.
- **Required action/qualification.** Native Linux release qualification remains OUTSTANDING and
  unchanged (no native host authorized); representative production capacity remains OUTSTANDING.

## Q-B5B-05-Q1-F08 — 30-second message-read timeout closes an idle failed connection (accepted transport boundary)

**Category: A/F note (boundary observation, not a defect).**

- **Requirement.** Commission §8: verify the session state and provider behavior after each
  transition; do not classify terminal Defunct failures as recoverable.
- **Independent evidence.** During harness diagnostics a client that idled in `Failed` longer
  than the accepted `BoltTcpServerOptions.MessageReadTimeout` (30 s, `src/BoltSharp/…/
  BoltTcpServerOptions.cs`) had its connection closed by the listener with
  `ConnectionFailed timeout`; recoverable-failure handling itself is not affected — every
  scenario that progressed within the timeout recovered. This is the accepted B5-A transport
  boundary, not a Failed-state special case.
- **Severity.** INFO.
- **Impact.** A recoverable-failed connection is recoverable within the transport read timeout
  only; any client (official driver included, which acts immediately) is unaffected in practice.
- **Required action/qualification.** Record as a boundary observation. No contract impact.

---

## Findings summary

| ID | Category | Severity | Qualification effect |
|---|---|---|---|
| F01 | A | none (capability) | server-side recovery gap closed by demonstration |
| F02 | B | none (verified) | identity gate satisfied |
| F03 | C | HIGH (limitation, not defect) | documented official-driver limitation; no driver-interop claim |
| F04 | D | none (no defect) | `50N42`/`50N00` unchanged; Q-B5B-04 stays separate |
| F05 | E | none (controls effective) | oracle integrity confirmed |
| F06 | E | LOW (documentation) | producer package preserved; 3 precision notes accepted non-blocking |
| F07 | F | none (inherited) | native Linux / production capacity remain outstanding |
| F08 | A/F | INFO | 30 s read-timeout boundary recorded |

**No BLOCKING finding.** No incorrect RESET state transition, no physical-identity failure, no
false-positive recovery oracle, and no protocol contract violation was found by this commission.