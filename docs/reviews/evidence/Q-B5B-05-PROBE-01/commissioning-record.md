# Commissioning Record — Q-B5B-05-PROBE-01

Recorded **before execution**, as required by commission §4.

## 1. Qualification requirements being addressed

`Q-B5B-05` (B5-B owner acceptance, §23.2):

> **Q-B5B-05 — Same-connection recovery after IGNORED not established.** After the observed
> FAILURE/IGNORED sequence the driver destroys the connection; RESET recovery of that connection
> was not demonstrated.

Related binding context (B5-B owner acceptance):
- §20 — the live-driver IGNORED scenario: the official driver pipelines
  `RUN MATCH (n) DETACH DELETE n {} {}` and `PULL {n=1000}` in one client write; the server
  answers FAILURE (`50N42`) for RUN and **zero-field IGNORED, byte-exact `B0 7E`**, for PULL;
  the connection is destroyed by the driver and recovery occurs on a fresh connection.
- §19 (F02) — `50N42` is the bounded classification; drivers may treat refusals as
  unrecoverable per-connection; refinement is optional and separately authorized.
- §25 (non-claims) — "same-connection RESET recovery after the observed IGNORED scenario" is
  explicitly **not** claimed.
- B5-B-TS1-OWNER-01 — `Q-B5B-05` **remains OPEN** as an independent qualification.
- B3-B §9 — the fail-closed retirement contract: DEFUNCT means no RESET recovery and no second
  operation (a terminal failure is therefore inadmissible for this demonstration).

## 2. Probe owner and session identity

| Field | Value |
|---|---|
| Commission | `Q-B5B-05-PROBE-01` |
| Role | Producer demonstration (bounded probe) — **not** an independent qualification |
| Probe owner | The commissioning agent session executing this commission (fresh session) |
| Producer scope | probe project, evidence package and producer report only |
| Independent reviewer separation | **Required and enforced.** The future independent reviewer must not have authored this probe or performed this producer verification. The probe source is committed under `docs/reviews/probes/Q-B5B-05-PROBE-01/` and the evidence under `docs/reviews/evidence/Q-B5B-05-PROBE-01/`, so a fresh session can verify it independently from the pinned commits. |
| Conflicts | No participation in the R1 plaintext remediation, its producer verification, Q1, Q2 or the owner acceptance. No production change is made by this commission. |

**No independent qualification is claimed by this commission.** The probe's own assertions are
producer oracles; their qualification is the next commission's job.

## 3. Frozen production revision

| Field | Value |
|---|---|
| Repository | `D:\projects\bolt-sharp` |
| Base commit | `e0b762a5ae716544c7d3a5fa3f4e7c08372b7ac1` |
| Base tree | `42c64bcc93c5d11206430fe611382fd665d5eb30` |
| Production source | Unmodified. Every scenario runs the accepted production engine (`BoltTcpServer`, `BoltTcpConnection`, `BoltConnectionSession`, `BoltMessageCodec`, `ReferenceOperationProvider`) behind its public seams only. |
| Test source | Unmodified. No existing test, oracle or harness file is edited or deleted. |
| New files | Only the probe project (`docs/reviews/probes/Q-B5B-05-PROBE-01/`) and its evidence (`docs/reviews/evidence/Q-B5B-05-PROBE-01/`). |

## 4. Authorized scope

| In scope | Out of scope |
|---|---|
| A bounded demonstration of recoverable FAILURE → RESET → subsequent operation on the same physical connection | Any production implementation or fix |
| The accepted plaintext loopback and the existing TLS loopback arrangement | Native Linux qualification (inherited release gate) |
| Official-driver (Neo4j.Driver 6.4.0) behavior observation | A different driver version or language driver |
| Raw protocol and server-side state-machine evidence | General transaction, routing, multi-database or Cypher qualification |
| Negative controls for the same-connection oracle | Reopening the deferred TLS graceful-close investigation |
| Windows and Ubuntu WSL2 execution | Representative production capacity characterization |
| Focused regression suites on the surfaces touched | Full B4/B6 entry or other roadmap milestones |

## 5. Experimental matrix (derived from Q-B5B-05 and the owner contracts)

| ID | Case | Evidence class |
|---|---|---|
| P-01 | Plaintext: valid operation → recoverable FAILURE → IGNORED → RESET → subsequent valid operation → orderly close | Server-side + raw protocol + proxy + provider identity |
| P-02 | Plaintext: three recoverable FAILURE/RESET cycles on one connection | Same |
| P-03 | Plaintext: the exact Q-B5B-05 pipelined RUN+PULL FAILURE+IGNORED scenario, then RESET recovery | Same |
| P-04 | TLS loopback: the same sequence on one encrypted connection | Server-side (a passive byte tap cannot observe inside TLS) |
| P-05 | Plaintext: an unaffected concurrent client while another session is failed, then independent recovery | Server-side + per-connection provider identity |
| D-01 | Official driver: the pipelined RUN/PULL refusal — driver classification and actual wire sequence | Official-driver + proxy |
| D-02 | Official driver: recovery attempt after the recoverable failure — physical connection disposition | Official-driver + proxy |
| D-03 | Official driver: retriable provider failure (50N00) with a retry policy — whether RESET is sent | Official-driver + proxy |
| D-04 | Official driver: three sequential sessions — pooling interaction, physical connections and RESETs | Official-driver + proxy |
| NC-01 | Replacement connection — the same-connection oracle must reject it | Control |
| NC-02 | Missing RESET — the oracle cannot report valid recovery | Control |
| NC-03 | Injected recovery failure (operation disposal throws during RESET) — the RESET oracle must detect the non-SUCCESS | Control |
| NC-04 | Terminal failure substituted for a recoverable one — inadmissible classification cannot satisfy the proof | Control |

## 6. Evidence and negative-control requirements recorded before execution

- Explicit Git-blob hashing semantics for the manifest and the verifier.
- Credential-free evidence: LOGON payloads are redacted and never stored.
- Negative controls must document the intended defect, the observed execution path, the
  detecting oracle and the result.
- Both platform runs retained, including any failures or skips.
- The probe's own assertions are producer oracles and are qualified only by the next
  commission.