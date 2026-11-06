# B0-B-DRV-INT-01 — Interoperability gap matrix

**Task:** B0-B — Official Neo4j Driver Integration Harness and Protocol Discovery
**Baseline:** `a4429ef969a3f150c4ab1b4908a193ffaddcce2a` (B0-A-TRACE closed)
**Driver:** `Neo4j.Driver` **6.4.0** (exact pin), direct `bolt://127.0.0.1:{port}`, plaintext loopback
**Selected revision:** **Bolt 5.8** (legacy handshake), **PackStream 1.0** (provisional per `docs/compatibility-target.md`)
**Disposition:** discovery results only. This is **not** owner acceptance and establishes **no** Neo4j compatibility claim.

This matrix is populated **only** from executed results recorded under
`docs/reviews/evidence/B0-B-DRV-INT-01/`. Nothing is pre-asserted as successful.

- **Fake-server proof** — the real driver against the scripted responder (`ScriptedBoltServer`), responses built from the independent byte fixtures in `Infrastructure/ProtocolFixtures.cs`.
- **Real BoltSharp proof** — the same real driver against the genuine, accepted test-only BoltSharp listener (production B2-B negotiation + B2-A framing), linked unmodified into the harness.

## Matrix

| Feature | Fake-server proof | Real BoltSharp proof | Required next step |
| --- | --- | --- | --- |
| **Handshake** | **PASS.** Driver sends magic `60 60 B0 17` + four proposals: slot 0 `000001FF` (Manifest v1), slot 1 `00080805` (range 5.0–5.8), slot 2 `00020404` (range 4.2–4.4), slot 3 `00000003` (3.0). Legacy selection `00 00 08 05` → **5.8**. Reject-all (`00 00 00 00`) → driver fails without sending HELLO. | **PASS (partial).** The real listener negotiates 5.8 in both legacy and Manifest v1 modes and observes the driver's first post-handshake message. | **None for handshake.** The B2-B negotiator is compatible with the pinned driver's offers/selection. |
| **HELLO** | **PASS.** Driver sends `HELLO` with `user_agent`, `routing`, `bolt_agent`. `SUCCESS {server, connection_id}` (with `hints`) → driver becomes ready. Empty SUCCESS metadata → driver `NullReferenceException` (metadata is **required**). | **OBSERVED, UNSUPPORTED.** The listener captures `HELLO` (`MESSAGE_READ`, signature `HELLO`) but implements no messaging protocol and sends no response, so initialization cannot proceed. | **B3-A** (message schemas) + **B3-B** (state machine). `server` and `connection_id` are mandatory in HELLO SUCCESS. |
| **LOGON** | **PASS.** Driver sends `LOGON` with `scheme`, `principal`, `credentials`. `SUCCESS {}` → ready. `FAILURE {neo4j_code: Neo.ClientError.Security.Unauthorized}` → `Neo4jException`; no query follows. Credentials never persisted. | **NOT REACHED** (HELLO unanswered). | **B3-B** (authentication state). Bolt 5.1+ requires `LOGON` after `HELLO`. |
| **RUN** | **PASS.** `RUN(query, parameters, extra)`; `SUCCESS {fields}` required before PULL. Query string and empty parameter map decoded from the real request. | **NOT REACHED.** | **B3-A** + **B3-C** (read-only RUN). |
| **PULL** | **PASS.** `PULL {n}`; `RECORD` + `SUCCESS {has_more}`. Finite `PULL(n=1)` yields one PULL per record with `has_more` true/false. `DISCARD` drains the remainder after a partial fetch. | **NOT REACHED.** | **B3-C** (cursor/`PULL`/`DISCARD` semantics). |
| **Graph values** | **PASS.** Driver reconstructs typed `INode` (id 1 `n:1` Person/Alice; id 2 `n:2` Person/Bob) and `IRelationship` (id 1 `r:1` KNOWS, start `n:1`, end `n:2`, `since=2024`). Numeric id 1 reused on node and relationship with distinct typed identities. | **NOT REACHED.** | **B3-C** (synthetic graph) using the accepted B5 field layout (Node 4-field, Relationship 8-field). |
| **RESET / failure** | **PASS.** Protocol `FAILURE` surfaces as `Neo4jException`; a pipelined `PULL` after a failed `RUN` is answered `IGNORED`; `RESET` returns to `READY`. Abrupt socket termination and a truncated frame surface as errors (never a clean end). | **NOT REACHED.** | **B3-B** (FAILED/IGNORED/RESET recovery). |

## Observed protocol facts (independently established)

1. **Mandatory HELLO metadata.** `SUCCESS` for `HELLO` must carry `server` and `connection_id`; without them the driver throws a `NullReferenceException` rather than reporting a clean protocol error. (`t01`, `t02`)
2. **Handshake response must be a single write.** The driver's handshake reader performs one read for the 4-byte version response and fails with `ServiceUnavailableException` if it is split across TCP segments (`t07-handshake-fragmentation`). A conforming server writes it whole.
3. **Failure is followed by IGNORED.** After a `FAILURE`, the driver may still pipeline a `PULL`; the server must answer `IGNORED` until `RESET`. (`t06-protocol-failure`)
4. **Auth failure requires `neo4j_code`.** A `FAILURE` on `LOGON` carrying `neo4j_code = Neo.ClientError.Security.Unauthorized` is surfaced as an authentication `Neo4jException`. (`t02-invalid-credentials`)
5. **DISCARD trigger.** A partial fetch followed by result consumption makes the driver `DISCARD` the remaining cursor. (`t05-discard`)

## Smallest recommended work packages

| Gap | Smallest appropriate package |
| --- | --- |
| HELLO/LOGON/RUN/PULL/DISCARD/RESET/SUCCESS/RECORD/FAILURE/IGNORED schemas | **B3-A** (message schemas for the pinned revision) |
| Authentication-before-query, FAILED/IGNORED/RESET recovery, GOODBYE | **B3-B** (connection state machine) |
| Read-only RUN, finite PULL/has_more, DISCARD, synthetic Alice/Bob/KNOWS graph | **B3-C** (backend contract + synthetic graph) |
| Durable TCP/TLS transport for the real driver, then typed-graph acceptance | **B5-A** (TCP/TLS), **B5-B** (official-driver integration) |

No new work package is proposed: every remaining gap maps to an existing B3/B5 task. **B0-B does not authorize implementing any of them.**
