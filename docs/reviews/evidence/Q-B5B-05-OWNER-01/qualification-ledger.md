# Qualification Ledger — Q-B5B-05-OWNER-01

Status of every qualification affected by or preserved through the `Q-B5B-05` closure. Entries
marked "unchanged" retain their exact prior dispositions; this decision edits no prior owner
record.

| Qualification | Before this decision | After this decision |
|---|---|---|
| **Q-B5B-05 — Same-connection recovery after IGNORED** | OPEN (retained across B5-B §20/§23.2, B5-B-TS1-OWNER-01, B5-B-F01-R1-OWNER-01, Q-B5B-09-PLAIN-OWNER-01; Q1: SUPPORTED WITH QUALIFICATIONS) | **CLOSED — ACCEPTED WITH QUALIFICATIONS** |
| Q-B5B-04 — Generic `50N42` refusal classification | OPEN (ACCEPTED QUALIFICATION; optional refinement) | OPEN (unchanged, separate) |
| Q-B5B-09 — Release-grade hardening | OPEN | OPEN (unchanged, separate) |
| B5-B-TS1 — test remediation findings | CLOSED | CLOSED (unchanged) |
| Q-B5B-03 — unsupported-command transport closure | CLOSED | CLOSED (unchanged) |
| Plaintext graceful-close remediation | CLOSED (owner-accepted) | CLOSED (unchanged) |
| TLS graceful-close remediation | DEFERRED | DEFERRED (unchanged) |
| Observer isolation | DEFERRED BACKLOG | DEFERRED BACKLOG (unchanged) |
| Native Linux release qualification | OUTSTANDING | OUTSTANDING (unchanged) |
| Representative production capacity | OUTSTANDING | OUTSTANDING (unchanged) |
| Portable typed-linger correction | ACCEPTED (Windows and WSL2 scope) | ACCEPTED (unchanged) |
| Blocked-write backpressure (`Q-B5A-06`) | OPEN (release obligation) | OPEN (unchanged; part of `Q-B5B-09` roadmap) |

## Notes

- The B5-B owner decision's §23.2 identifier and §25 non-claim are superseded **in status** by
  this decision; the historical decision file itself is not edited.
- The qualification carries its two qualifications verbatim: (i) official-driver post-failure
  same-connection RESET interoperability is not demonstrated and not claimed; (ii) the closure
  rests on the contract interpretation recorded in `Q-B5B-05-Q1` evidence §02, which the owner
  accepted.
- Future claims touching original-connection recovery must satisfy the six identity lines
  (Q1 `03-identity-matrix.md`); fresh-connection recovery remains inadmissible for that claim.