# BoltSharp initial protocol compatibility target

Task B0-A deliverable. Research only. This document selects a **provisional** first
Bolt protocol revision for BoltSharp. It contains **no implementation** and makes
**no claim that BoltSharp works with Neo4j, Neo4j Browser, or any driver**.

---

## 1. Research status and environment

| Item | Value |
| --- | --- |
| Date | 2026-11-02 |
| Repository | `jenolaszlo-sketch/bolt-sharp` |
| Working branch | `feature/protocol-spec` |
| Base commit | `e8475a4787b86b7596286d55fefffd777499829a` |
| Deliverable | `docs/compatibility-target.md` (only file added) |
| Live Neo4j server available | NO |
| Neo4j Browser client available | NO |
| Official .NET driver test available | NO |
| Live client probe status | **NOT RUN — ENVIRONMENT UNAVAILABLE** |
| Decision status | **PROVISIONAL — no live client negotiation evidence** |

All S1–S8 sources were retrieved successfully (Section 2). No running Neo4j
instance, Browser build, or driver installation was available to this task and
none was installed (task boundary). Therefore every version-compatibility claim
about a *client* (Browser or `.NET` driver) is **UNKNOWN**; only *Neo4j server*
support is documented.

**Evidence categories used below**

- **Documented fact** — directly stated in an S1–S8 page (source ID + heading).
- **Inference** — a conclusion drawn from documented facts; labelled as such.
- **Observation** — a real capture. *There is none in this task.*
- **Unknown** — not established by the available sources or the environment.

---

## 2. Sources consulted

All pages accessed **2026-11-02**. S1–S8 are the required canonical sources.
Because the published `neo4j.com/docs/bolt/current/` pages are generated from the
official `neo4j-drivers/docs-bolt` AsciiDoc repository, the upstream AsciiDoc
files were also read to obtain the complete "Summary of changes per version"
sections that the rendered HTML truncated. These are the same documents, not
third-party implementations.

| ID | Authority | URL | Accessed | Notes |
| --- | --- | --- | --- | --- |
| S1 | Neo4j Bolt compatibility matrix | https://neo4j.com/docs/bolt/current/bolt-compatibility/ | Yes | Server support per Neo4j version |
| S2 | Bolt handshake spec | https://neo4j.com/docs/bolt/current/bolt/handshake/ | Yes | Magic bytes, 4 proposals, Manifest v1, VarInt |
| S3 | Bolt message spec | https://neo4j.com/docs/bolt/current/bolt/message/ | Yes | Messages, fields, per-version change summary |
| S4 | Bolt server-state spec | https://neo4j.com/docs/bolt/current/bolt/server-state/ | Yes | States and transitions |
| S5 | Structure semantics | https://neo4j.com/docs/bolt/current/bolt/structure-semantics/ | Yes | Node/Relationship fields per version |
| S6 | PackStream spec | https://neo4j.com/docs/bolt/current/packstream/ | Yes | PackStream 1.0 / 1.1, markers |
| S7 | Browser connectivity | https://neo4j.com/docs/browser/operations/dbms-connection/ | Yes | Schemes and transport assumptions |
| S8 | .NET driver install | https://neo4j.com/docs/dotnet-manual/current/install/ | Yes | Driver *client* server-version support |
| S3′ | Message spec upstream AsciiDoc | https://raw.githubusercontent.com/neo4j-drivers/docs-bolt/main/modules/ROOT/pages/bolt/message.adoc | Yes | Complete version summaries for S3 |
| S2′ | Handshake upstream AsciiDoc | https://raw.githubusercontent.com/neo4j-drivers/docs-bolt/main/modules/ROOT/pages/bolt/handshake.adoc | Yes | Confirms S2 has no post-5.7 handshake version |
| S1′ | Compatibility upstream AsciiDoc | https://raw.githubusercontent.com/neo4j-drivers/docs-bolt/main/modules/ROOT/pages/bolt-compatibility.adoc | Yes | Confirms S1 tables and 5.5 note |

---

## 3. Version difference matrix

Candidate columns are fixed by the task: `Bolt 5.8`, `Bolt 6.0`, `Bolt 6.1`.
Each cell states a status keyword plus a short technical fact. Status keywords:
**SUPPORTED BY SPEC**, **DIFFERS**, **NOT DOCUMENTED**, **NOT CHECKED** (and
**UNKNOWN** where a fact cannot be established).

### 3.1 The nine required dimensions

| # | Requirement | Bolt 5.8 | Bolt 6.0 | Bolt 6.1 | Evidence (S# + section) | Status |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Version existence and **Neo4j server** versions (explicitly **not** client support) | SUPPORTED BY SPEC — negotiated by Neo4j `5.26` – current | SUPPORTED BY SPEC — negotiated by Neo4j `2025.10`–`2026.08` and `2026.09`–current | SUPPORTED BY SPEC — negotiated by Neo4j `2026.09`–current | S1 "Bolt 6.x" and "Bolt 5.x" tables; footnote " - current" | SUPPORTED BY SPEC. This is **server** support only; it says nothing about Browser or driver offers. |
| 2 | Legacy (4-proposal) vs Manifest v1 handshake and selection | SUPPORTED BY SPEC — legacy 4-proposal and Manifest v1 both available (Manifest v1 from 5.7) | NOT DOCUMENTED (for 6.x) — no handshake change is stated after 5.7 | NOT DOCUMENTED (for 6.x) — same | S2 "Version negotiation", "Bolt version 5.7 → Handshake Manifest v1" | SUPPORTED BY SPEC for 5.7+; 6.0/6.1 handshake is **UNKNOWN** (S2 only documents up to 5.7 and gives no 6.x section). |
| 3 | `HELLO`/`LOGON` separation and startup metadata | SUPPORTED BY SPEC — `HELLO` (`01`) carries non-auth startup metadata; `LOGON` (`6A`) carries auth (since 5.1); `LOGON` SUCCESS may carry `advertised_address` (5.8) and `credentials_expired` | SUPPORTED BY SPEC — same separation; no documented `HELLO`/`LOGON` change at 6.0 | SUPPORTED BY SPEC — same separation; no documented change at 6.1 | S3 "Request message `HELLO`", "Request message `LOGON`", "Summary of changes per version → 5.1, 5.8" | SUPPORTED BY SPEC. `HELLO` fields include `user_agent`, `routing`, `notifications_minimum_severity`, `notifications_disabled_classifications`, `bolt_agent`. |
| 4 | Whether `TELEMETRY` is defined | SUPPORTED BY SPEC — `TELEMETRY` (`54`), field `api::Integer`, valid only in `READY` (added 5.4) | SUPPORTED BY SPEC — inherited; no documented change | SUPPORTED BY SPEC — inherited; no documented change | S3 "Request message `TELEMETRY`"; S3 "Summary of changes per version → 5.4" | SUPPORTED BY SPEC. Presence of the message does not imply any client sends it. |
| 5 | `RUN`/`PULL`/`DISCARD` read-only flow | SUPPORTED BY SPEC — `RUN` (`10`); `PULL` (`3F`) / `DISCARD` (`2F`) with `extra{n,qid}`; `SUCCESS.has_more`; `DISCARD n=-1` discards all; 5.8 adds home-db `db` to `RUN` SUCCESS | SUPPORTED BY SPEC — same message set; no read-flow change documented | SUPPORTED BY SPEC — same | S3 "Request message `RUN`/`PULL`/`DISCARD`"; S3 "Summary of changes per version → 5.8" | SUPPORTED BY SPEC as protocol definition. **Not** a claim that BoltSharp implements it. |
| 6 | `RESET`/`GOODBYE`/failure recovery and state handling | SUPPORTED BY SPEC — `RESET` (`0F`) triggers `<INTERRUPT>`, returns to `READY`; `GOODBYE` (`02`) closes with no response; `FAILED`/`IGNORED`/`DEFUNCT` states | NOT DOCUMENTED (for 6.x) — S4's per-version summary stops at 5.2 | NOT DOCUMENTED (for 6.x) — same | S3 "`RESET`", "`GOODBYE`"; S4 "Server states", "Summary of changes per version" | SUPPORTED BY SPEC for the base state machine; 6.x-specific state changes are **UNKNOWN** (none documented). |
| 7 | `FAILURE` fields / version-specific GQL status behaviour | DIFFERS from ≤5.6 — `FAILURE` metadata is `gql_status`, `neo4j_code`, `description`, `diagnostic_record`, `cause` (5.7+); `code` removed after 5.6 | DIFFERS from 5.7 — additionally, "Since Bolt 6.0" the `message` text is explicitly **not** a stable marker | SUPPORTED BY SPEC — same field set as 6.0 (no 6.1 change documented) | S3 "Summary message `FAILURE`"; S3 "Summary of changes per version → 5.7, 6.0"; S3 "Summary of changes per version → 6.1" | SUPPORTED BY SPEC. Use `gql_status`, not `message`, as the stable identifier. |
| 8 | Node/relationship and element ID representation | SUPPORTED BY SPEC — `Node` (`4E`) 4 fields incl. `element_id`; `Relationship` (`52`) 8 fields incl. `element_id`/`start_node_element_id`/`end_node_element_id` (element IDs since 5.0) | DIFFERS — same graph structures, **plus** new `Vector` (`56`) and `UnsupportedType` (`3F`) structures | SUPPORTED BY SPEC — same as 6.0; no 6.1 structure change documented | S5 "`Node`", "`Relationship`", "`UnboundRelationship`", "Summary of changes per version → 6.0, 5.0" | SUPPORTED BY SPEC for Node/Relationship. 6.0 introduces two additional structures not needed for a two-node graph. |
| 9 | PackStream revision and optional/special values | SUPPORTED BY SPEC — PackStream **1.0** (`< 6.0` row); no `UUID` marker | NOT DOCUMENTED — S6's table lists `< 6.0 → 1.0` and `>= 6.1 → 1.1`, omitting 6.0 | SUPPORTED BY SPEC — PackStream **1.1**, adds `UUID` marker `E0` (128-bit) | S6 "Versioning" table; S6 "Changelog → Version 1.1"; S3 "Summary of changes per version → 6.1" | 5.8 = 1.0 (SUPPORTED BY SPEC); 6.1 = 1.1 (SUPPORTED BY SPEC); **6.0 PackStream version is UNKNOWN** (see 3.3). |

### 3.2 Protocol-framing facts common to all three candidates

These apply identically to 5.8, 6.0 and 6.1 and are **documented facts**, not
inferences:

- Bolt identification is the four bytes `60 60 B0 17` (S2 "Bolt identification").
- The client submits exactly four 32-bit big-endian version proposals; zero is a
  placeholder (S2 "Version negotiation").
- Legacy handshake: the server returns one 32-bit version, or zero then closes;
  the first matching proposal wins (S2 "Version negotiation").
- Manifest v1 (from 5.7): a proposal of `00 00 01 FF`; the server answers
  `00 00 01 FF`, a VarInt `N`, `N` versions in Bolt-4.3-style encodings, then a
  VarInt capabilities bitmask; the client replies with a 4.0-style 4-byte
  version and a VarInt capabilities subset, and may pipeline its first message
  (S2 "Handshake Manifest v1").
- Messages are PackStream structures. The message spec states "**Serialization
  is specified with PackStream Version 1**" (S3 "Serialization").
- Chunking: 2-byte big-endian chunk header, `00 00` end-of-message sentinel,
  65,535-byte maximum chunk; `NOOP` empty chunk from 4.1+ (S3 "Chunking").
- Bolt is "intended to take place over a TCP connection", default `TCP 7687`
  (S2 "Connection and disconnection").

### 3.3 Recorded source conflict / gap

- **PackStream version of Bolt 6.0 is not stated.** S6's "Versioning" table lists
  only `< 6.0` and `>= 6.1`. The 6.0 row is absent. Inference (not fact): 6.0 is
  probably PackStream 1.0 because the changelog ties 1.1/UUID to 6.1. This is
  **UNKNOWN** and must be resolved before any 6.0 implementation.
- **Two statements about "PackStream Version 1".** S3 "Serialization" says
  serialization is "PackStream Version 1", while S6 documents both 1.0 and 1.1.
  S6 is the authority on PackStream; S3's sentence appears not to have been
  updated for 1.1. No automatic reconciliation is performed here; treat S6 as
  authoritative and mark S3's wording as a documentation lag, not a version
  decision.
- **S4 has no per-version summary after 5.2.** S4 "Summary of changes per
  version" lists 5.2 → 5.1 → 5.0 → 4.4 … 2, and does not describe 5.3–6.1. State
  handling for 5.8/6.0/6.1 is therefore inherited by inference, not restated.

### 3.4 Bolt 5.5

**Documented fact:** S1 states "Bolt version 5.5 is not used, and no Neo4j server
will ever negotiate it." S3 "Summary of changes per version → 5.5" repeats this.
Bolt 5.5 is therefore **not** a candidate and is not proposed anywhere in this
document.

### 3.5 Compact unsupported/unknown client-behaviour table

These are the facts this task could **not** establish. None are documented in
S1–S8, and no client was observed.

| Client behaviour | Status | Why |
| --- | --- | --- |
| Bolt versions offered by a current Neo4j Browser build | **UNKNOWN** | S7 describes schemes and that Browser uses the Neo4j JavaScript Driver, but not the offered versions |
| Bundled JavaScript driver version inside a Browser build | **UNKNOWN** | Not in S1–S8 |
| Whether Browser's `bolt+s://` avoids sending `ROUTE` | **UNKNOWN** | S7 does not state message-level behaviour |
| Bolt versions a current `Neo4j.Driver` 6.x client offers | **UNKNOWN** | S8 states supported Neo4j *server* versions (4.4.x, 5.x, 2025.x, 2026.x), not negotiated Bolt versions |
| Whether Browser sends `TELEMETRY` | **UNKNOWN** | Not documented; message existence ≠ client usage |
| Whether Browser/JS driver requires home-database or `ssr.enabled` metadata | **UNKNOWN** | Fields are optional metadata; no client requirement is documented |
| Whether Browser reaches a Bolt server over raw TCP or WebSocket | **UNKNOWN from S1–S8** | S2 says Bolt is *intended* over TCP; S7 says Browser uses the JS driver. Neither states the Browser→server transport. The parent spec's WSS requirement (`boltsharp-spec.md` §6.4) is a **project assumption to be validated in B4-B**, not a documented fact. |

---

## 4. Minimum read-only graph-session hypothesis

**This is a hypothesis, not a captured trace.** It is annotated per step as
specified or client-observed. Nothing here was observed.

Target candidate: **Bolt 5.8 + PackStream 1.0** (see Section 6).

```text
TCP (or, per parent spec, WebSocket binary) transport established        [SPECIFIED: S2 "Connection"; transport for Browser UNVERIFIED]
Bolt magic: 60 60 B0 17                                                  [SPECIFIED: S2 "Bolt identification"]
Client offers four 32-bit versions; probably includes Manifest v1 00 00 01 FF
                                                                         [SPECIFIED format: S2; ACTUAL OFFERS: UNKNOWN UNTIL PROBE]
Server negotiation response:
  - Manifest v1 path: 00 00 01 FF + VarInt N + N versions + capabilities [SPECIFIED: S2 "Handshake Manifest v1"]
  - legacy path: single 32-bit selected version, or 0 + close            [SPECIFIED: S2 "Version negotiation"]
Client selects 5.8 (00 00 05 08) + capabilities (manifest path only)      [INFERENCE if manifest is negotiated]
HELLO {user_agent, routing?, notifications_*, bolt_agent?} -> SUCCESS     [SPECIFIED: S3 HELLO]
   SUCCESS metadata may include server, connection_id, protocol_version   [SPECIFIED: S3 HELLO → SUCCESS]
LOGON {scheme:"basic", principal, credentials} -> SUCCESS                 [SPECIFIED: S3 LOGON; REQUIRED since 5.1]
   SUCCESS may include advertised_address (5.8), credentials_expired      [SPECIFIED: S3 LOGON → SUCCESS]
RUN "MATCH (a)-[r]->(b) RETURN a,r,b" {} {mode:"r", db?} -> SUCCESS(fields) [SPECIFIED: S3 RUN]
   SUCCESS may include db (home database, 5.8)                            [SPECIFIED: S3 RUN → SUCCESS]
PULL {n:-1, qid:-1} -> RECORD* -> SUCCESS {has_more:false}                [SPECIFIED: S3 PULL]
GOODBYE (no response) / close                                            [SPECIFIED: S3 GOODBYE]
```

- The query string `MATCH (a)-[r]->(b) RETURN a,r,b` is the reference-backend
  statement named by the parent spec (`boltsharp-spec.md` §7); it is not a
  Browser requirement.
- **additional messages / startup metadata: UNKNOWN UNTIL PROBE.** Candidates
  that a real client may or may not send, and that this task cannot resolve:
  `ROUTE` (`66`, added 4.3; relevant if the client uses a routing scheme such as
  `neo4j://`), `TELEMETRY` (`54`), `LOGOFF` (`6B`), database-selection metadata
  in `RUN`/`BEGIN` (`db`), and any driver-specific hints. S7 notes Browser can
  use `neo4j+s://` (driver-handled routing) as well as direct `bolt+s://`.
- **This is not an exact Browser trace.** No Browser was available.

---

## 5. Candidate cost comparison

Qualitative only (`LOWER` / `SIMILAR` / `HIGHER` relative to the 5.8 baseline).
No person-days or line counts are invented. Cost is driven only by **documented
differences**.

| Candidate | Relative effort | Documented cost-driving features |
| --- | --- | --- |
| **Bolt 5.8** (baseline) | **LOWER** | `HELLO`/`LOGON` separation (5.1); full message set `RUN`/`PULL`/`DISCARD`/`RESET`/`GOODBYE`/`SUCCESS`/`RECORD`/`FAILURE`/`IGNORED`; 5.7+ GQL `FAILURE` field set; 5.8 optional metadata (`db`, `advertised_address`, `ssr.enabled` hint); Manifest v1 (5.7) optional; PackStream 1.0. |
| **Bolt 6.0** | **SIMILAR–HIGHER** | Everything in 5.8, plus the `Vector` (`56`) and `UnsupportedType` (`3F`) structures (S5 6.0 summary) and the 6.0 `FAILURE.message`-instability rule (S3 6.0 summary). Message flow that BoltSharp needs is otherwise unchanged. PackStream version is **undocumented** for 6.0 (Section 3.3), which is itself a research cost. |
| **Bolt 6.1** | **HIGHER** | Everything in 6.0, plus PackStream **1.1** with the `UUID` marker `E0` (S6; S3 6.1 summary). Adds a new primitive type and its rejection/validation paths that a two-node visualization does not require. |

Cost drivers by name: **Manifest v1**, **`HELLO`/`LOGON` split**, **GQL `FAILURE`
fields (`gql_status`/`diagnostic_record`/`cause`)**, **`PULL`/`DISCARD` `has_more`
pagination**, **`Vector`**, **`UnsupportedType`**, **PackStream 1.1 `UUID`**.

The primary product proof needs only Node and Relationship structures, a read-only
`RUN`/`PULL` flow, and authentication. `Vector`, `UnsupportedType` and `UUID` add
type-system surface with no bearing on that proof, so tracking the newest
revision (6.1) is not justified by this goal (task "Decision priority" #3).

---

## 6. Provisional recommendation and alternative

### Recommended starting revision

> **Bolt 5.8 with PackStream 1.0.**
> Decision status: **PROVISIONAL — no live client negotiation evidence.**

**Rationale (task Decision priority):**

1. *Verified client compatibility:* not available — no Browser/driver was
   observed, so priority #1 cannot be satisfied (Section 1, Section 3.5).
2. *Smallest version-specific semantics that could plausibly meet the goal:*
   Bolt 5.8 is the newest 5.x line, is documented as supported by Neo4j
   `5.26` – current (S1), and requires only the semantics a two-node read-only
   graph view needs: authenticated session (`HELLO` + `LOGON`), read-only
   `RUN`/`PULL`, `RECORD` with `Node`/`Relationship` (4-field/8-field forms,
   S5), and `SUCCESS`/`FAILURE` handling with the 5.7+ GQL field set (S3).
3. *Avoid unrelated type-system growth:* 6.0 adds `Vector`/`UnsupportedType`
   (S5) and 6.1 adds PackStream 1.1 `UUID` (S6). Neither is needed, so the
   recommendation does not track 6.x.

Bolt 5.8's only additions beyond 5.7 are **optional metadata** (`advertised_address`
on `LOGON` SUCCESS, resolved home `db` on `RUN`/`BEGIN` SUCCESS, and the
`ssr.enabled` hint) plus the fact that 5.8 is in the current server support line.
These do not force new messages, so 5.8 costs essentially the same as 5.7 while
sitting on the supported loop.

### Alternative (fallback)

> **Bolt 6.0 with PackStream 1.0**, only if a live probe shows a current client
> refuses 5.8 and requires a 6.x revision.

- Choose 6.0, **not 6.1**, to avoid PackStream 1.1 `UUID` (S6).
- Before committing, resolve the undocumented PackStream version for 6.0
  (Section 3.3).

**Only one candidate is recommended.** Do **not** implement 5.8, 6.0 and 6.1
together.

### Explicit non-claims

- This is **not** a claim of Neo4j Browser compatibility, Neo4j compatibility,
  or `.NET` driver compatibility.
- Bolt **5.5** is explicitly rejected and is not proposed (S1, S3).
- No Browser build/version, driver version, offered version list, or negotiated
  version is invented.

---

## 7. Live-client validation recipe and observation status

**Status: `NOT RUN — ENVIRONMENT UNAVAILABLE`.** No Neo4j server, Browser build, or
driver was available; none was installed (task boundary). The following is the
ordered recipe for a later milestone. It is a plan, not code, and not evidence.

1. **Record the environment before connecting:** Neo4j DBMS version; exact
   Neo4j Browser build/version; web browser and OS; official `Neo4j.Driver`
   package version if tested; connection scheme (`bolt://`, `bolt+s://`,
   `neo4j://`, `neo4j+s://`, or `https://`); TLS mode and certificate trust.
2. **Capture the handshake's offered and selected versions** using lawful local
   test instrumentation — for example a local logging proxy, the server's own
   connection/protocol logging, or an in-process test client that records the
   bytes the client sends. Record:
   - the four client proposals and whether one is `00 00 01 FF`;
   - whether the server answered with Manifest v1 or a single legacy version;
   - the finally selected version and capability bitmask.
   **Note:** WSS/TLS payloads are encrypted. A plain browser network view may not
   expose decoded Bolt bytes. Do **not** claim to have seen encrypted bytes;
   capture at a lawful endpoint you control.
3. **Record the negotiated session only if verifiable:** whether Manifest v1 was
   used; the actual `HELLO` fields and `LOGOFF`/`LOGON` exchange; any `ROUTE`,
   `TELEMETRY`, hints, or database metadata actually sent.
4. **Keep sanitized evidence:** strip credentials (`credentials`/`principal`)
   and any tokens or personal data. Retain the command/tool used, OS, and
   reproducible steps.
5. **If no server or trace tooling is accessible:** mark the probe
   `NOT RUN — ENVIRONMENT UNAVAILABLE`, document exactly what is missing, and
   stop. **Do not** install or build a substitute environment inside B0-A.

Open trace questions this recipe must answer are listed in Section 8.

---

## 8. Open questions and stop conditions

### Open questions

1. Which Bolt versions does the current Neo4j Browser build (its bundled
   JavaScript driver) actually offer on the wire?
2. Does Browser send `ROUTE`, and under which scheme? `bolt+s://` is documented
   as single-server, but message-level behaviour is not documented (S7).
3. Which PackStream revision applies to Bolt 6.0? S6 omits the 6.0 row.
4. Does Browser/JS driver require any home-database (`db`) or `ssr.enabled`
   metadata, or are they safe to omit as optional?
5. What transport does Browser use to reach the Bolt server over TLS: raw TCP or
   WebSocket? Not established by S1–S8; the parent spec assumes WSS.
6. Does Browser send `TELEMETRY` (`54`)?

### Stop conditions / risks

- **Current Browser (or driver) rejects the candidate.** Stop; do not widen scope
  to many versions without evidence.
- **Mandatory routing metadata conflicts with a single-node backend.** If the
  client insists on `ROUTE`/cluster semantics that a synthetic single-node
  backend cannot truthfully satisfy, mark the version/client plan unresolved.
- **A required protocol state or feature has no compliant read-only mapping.**
  Return the version-appropriate documented `FAILURE`/`IGNORED`; never fabricate
  `SUCCESS`.
- **Source evidence contradicts the provisional recommendation.** Reopen this
  decision and record an ADR rather than silently reconciling versions.
- **Bolt 5.5 is never a fallback** (S1, S3).

No implementation may begin until the owner reviews this candidate and a live
negotiation confirms or refutes it.

---

## 9. Evidence / handoff receipt

```text
Task: B0-A
Base commit: e8475a4787b86b7596286d55fefffd777499829a
Files changed: docs/compatibility-target.md only
Commands/tools used: git clone/switch (PowerShell); web fetch of S1-S8 and the
                     upstream docs-bolt AsciiDoc sources S1',S2',S3'
Source documents accessed: S1-S8 (all successful) plus upstream AsciiDoc
                     sources for the per-version change summaries
Observed client build/version: UNKNOWN
Live handshake captured: NO
Recommended revision: Bolt 5.8 (PackStream 1.0)
Alternative: Bolt 6.0 (PackStream 1.0), only if a live probe requires 6.x
Decision status: PROVISIONAL
Tests: NOT APPLICABLE (research only)
Evidence limitations:
  - No Neo4j server, Browser, or .NET driver was available; probe NOT RUN.
  - Client-side behaviour (offered versions, ROUTE, TELEMETRY, transport) is
    UNKNOWN; S1-S8 document server support and protocol shape, not a specific
    client's negotiation.
  - Bolt 6.0's PackStream revision is not documented in S6.
  - S4's per-version state summary stops at 5.2.
  - S3 "Serialization" says "PackStream Version 1" while S6 documents 1.0/1.1.
Commit SHA: <fill after commit>
Independent review: PENDING
Owner acceptance: PENDING
```

**Status: READY FOR OWNER REVIEW.** This document is a research deliverable, not
an approved protocol decision. B0-B and any version-sensitive implementation
must wait for owner review and the live negotiation probe.
