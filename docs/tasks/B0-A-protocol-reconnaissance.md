# B0-A — Bolt protocol target reconnaissance

**Task ID:** B0-A
**Parent:** [BoltSharp specification](../boltsharp-spec.md) and [implementation ledger](../implementation-tasks.md)
**Status:** READY FOR RESEARCH (not accepted)
**Target implementer:** DeepSeek / GLM Flash or another inexpensive model
**Scope:** Documentation and evidence only. Do not implement Bolt.
**Repository:** `jenolaszlo-sketch/bolt-sharp`
**Working branch for this assignment:** `feature/protocol-spec`
**Date of specification:** 2026-11-02

## 1. Goal

Recommend **one provisional first Bolt protocol revision** for BoltSharp, optimizing for a practical initial Neo4j Browser graph-visualization proof with as little unnecessary implementation as possible. Describe *exactly* which handshake and protocol features that revision requires.

This is a **research task**, not a decision that BoltSharp already works with Browser. A model reading Neo4j's server-compatibility table cannot conclude which version a particular Neo4j Browser build will negotiate. Only an observed connection or a sufficiently specific authoritative client source can establish that fact.

## 2. Strict task boundary

**Allowed:**
- Read the official documentation listed below and the two parent repository documents.
- Create or update **one deliverable**: `docs/compatibility-target.md`.
- Record exact, source-backed facts, unresolved questions, and a provisional recommendation.
- If a running Neo4j instance and a client are **already available**, optionally record an observed handshake without installing or configuring new services. Distinguish what was observed from what documentation implies.

**Do not:**
- Write or modify C# files, tests, solutions, package references, CI, networking components, or runtime code.
- Implement PackStream, version negotiation, a socket, WebSocket handler, state machine, or Cypher.
- Install Neo4j, Docker, packages, binaries, or browser extensions; do not create cloud resources.
- Create additional branches/worktrees or change the `main` branch.
- Search unrelated third-party protocol implementations unless the official specification is unavailable.
- Present simulated/example bytes as a captured handshake.
- Claim a current Browser or .NET driver accepts the candidate version without direct evidence.
- Continue with B0-B or B1-A after this task.

**If there is a conflict between sources:** report it and mark the affected decision unresolved. Do not silently reconcile inconsistent versions.

## 3. Required sources (read these first)

Use **these specific pages** as the canonical sources. Record the URL and the section/heading of every nontrivial claim. If a page is unavailable, say so and make that item UNKNOWN rather than guessing.

| ID | Authority | URL | Extract |
| --- | --- | --- | --- |
| S1 | Neo4j compatibility matrix | https://neo4j.com/docs/bolt/current/bolt-compatibility/ | Which versions exist; distinguish **Neo4j server** support from client support |
| S2 | Bolt handshake | https://neo4j.com/docs/bolt/current/bolt/handshake/ | Magic bytes, four proposals, legacy negotiation, version ranges, Manifest v1 |
| S3 | Bolt message specification | https://neo4j.com/docs/bolt/current/bolt/message/ | HELLO, LOGON, LOGOFF, RUN, PULL, DISCARD, RESET, TELEMETRY, failure metadata |
| S4 | Server-state specification | https://neo4j.com/docs/bolt/current/bolt/server-state/ | Negotiation, authentication, READY, STREAMING, FAILED, DEFUNCT |
| S5 | Structure semantics | https://neo4j.com/docs/bolt/current/bolt/structure-semantics/ | Node, relationship and element ID wire fields by revision |
| S6 | PackStream | https://neo4j.com/docs/bolt/current/packstream/ | Serialization version required, especially 6.1 UUID change |
| S7 | Browser connectivity | https://neo4j.com/docs/browser/operations/dbms-connection/ | Browser scheme choices and transport assumptions |
| S8 | Official .NET driver documentation | https://neo4j.com/docs/dotnet-manual/current/install/ | Driver **client** compatibility claim; do not infer individual negotiated Bolt versions |

**Known starting observations (must be checked, not blindly repeated):**
- S1 lists Bolt 5.8, 6.0 and 6.1; S1 explicitly excludes Bolt **5.5** as a negotiated server protocol.
- S2 says Manifest v1 negotiation begins with Bolt 5.7. The manifest marker is `00 00 01 FF`; the basic Bolt identification is `60 60 B0 17`.
- S3 says Bolt 5.1 separated authentication into `LOGON`; 5.7 changed `FAILURE` fields; 5.8 added home-database and advertised-address metadata; 6.1 adds PackStream 1.1 UUID.
- Browser offers Bolt connection schemes, but its *actual* version offers for a pinned build have **not** been observed by this task definition.

Do not extend the implementation matrix beyond what you can cite.

## 4. Work instructions (follow in this order)

### Step A — Extract verified differences

Read S1–S8. Create a concise matrix **with exactly these candidate columns**:
`Requirement | Bolt 5.8 | Bolt 6.0 | Bolt 6.1 | Evidence (S# + section) | Status`.

Include at least these **nine rows**:
1. Version existence and available Neo4j server versions (explicitly **not** Browser support).
2. Legacy versus Manifest v1 handshake path and selection behavior.
3. `HELLO` / `LOGON` separation and required startup metadata.
4. Whether `TELEMETRY` is defined.
5. `RUN` / `PULL` / `DISCARD` and read-only flow (without declaring it already supported by BoltSharp).
6. `RESET` / `GOODBYE` / failure recovery and state handling.
7. `FAILURE` fields and version-specific GQL status behavior.
8. Node/relationship and element ID representation.
9. PackStream revision and optional/special values.

For each cell, use **SUPPORTED BY SPEC**, **DIFFERS**, **NOT DOCUMENTED**, or **NOT CHECKED**, plus a short technical fact. If uncertain, write **UNKNOWN**; never infer a fact from an adjacent revision.

### Step B — List the minimum successful session

Write a **hypothesized** ordered exchange, annotated with whether each step is specified or client-observed:

```text
TCP or WebSocket transport established
Bolt magic + version proposals
Server negotiation response (legacy or Manifest v1)
HELLO -> SUCCESS
LOGON -> SUCCESS [if chosen revision requires it]
RUN "MATCH (a)-[r]->(b) RETURN a,r,b" -> SUCCESS(fields)
PULL -> RECORD(s) -> SUCCESS(summary)
GOODBYE / close
```

Add **"additional messages / startup metadata: UNKNOWN UNTIL PROBE"** where appropriate. For example, `ROUTE`, `TELEMETRY`, `LOGOFF`, database-selection metadata, or extra Browser commands may be relevant. **Do not declare this example an exact Browser trace.**

### Step C — Compare candidate implementation cost

Give a **qualitative** estimate for each candidate (`LOWER` / `SIMILAR` / `HIGHER` relative effort), using only *documented differences*. State the cost-driving features by name. Do not invent person-days or line counts.

Select **one provisional candidate** and optionally **one fallback**. Explain why; do not recommend implementing all three.

**Decision priority:**
1. Verified compatibility with an actual current Neo4j Browser build, **if observed**.
2. Otherwise smallest version-specific semantics that could plausibly meet the primary Browser visualization goal.
3. Avoid adding unrelated type system or server behaviors merely to track the newest revision.

Clearly label the decision either:
- `PROVISIONAL — no live client negotiation evidence`; or
- `SUPPORTED BY CLIENT OBSERVATION — version/build/trace attached`.

### Step D — Define the next executable probe

Write an **ordered recipe**, not production code, to validate the provisional choice against a real Neo4j server:
- Record Neo4j DBMS version, exact Browser build/version, browser, OS, official driver version if tested, connection scheme and TLS mode.
- Capture the Bolt handshake's *offered* and *selected* versions where tooling permits. Note that WSS/TLS payloads are encrypted; a plain browser network view may not expose their decoded bytes. Use lawful local test instrumentation or a known test server, and do not claim to see encrypted bytes.
- Record whether Manifest v1 was negotiated; note actual HELLO/LOGON and any required ROUTE, TELEMETRY, hints or DB metadata **only if verifiable**.
- Keep sanitized metadata, byte samples if actually captured, command/tool used and reproducibility steps. Never capture credentials in the evidence.
- If there is no accessible server or trace tooling, mark this probe `NOT RUN — ENVIRONMENT UNAVAILABLE`, document what is missing, and end the assignment. **Do not install or build a substitute environment in B0-A.**

### Step E — Decision handoff

Record explicit risks and stop conditions:
- current Browser rejects the candidate;
- mandatory Browser metadata/routing conflicts with a single-node backend;
- a required protocol state/feature has no compliant read-only mapping;
- source evidence contradicts the provisional recommendation.

Do **not** write the implementation. Finish with `READY FOR OWNER REVIEW`, not `APPROVED`.

## 5. Output contract: one Markdown document

**Create exactly:** `docs/compatibility-target.md`

Use these headings **in this order**; avoid additional essays:

```markdown
# BoltSharp initial protocol compatibility target
## 1. Research status and environment
## 2. Sources consulted
## 3. Version difference matrix
## 4. Minimum read-only graph-session hypothesis
## 5. Candidate cost comparison
## 6. Provisional recommendation and alternative
## 7. Live-client validation recipe and observation status
## 8. Open questions and stop conditions
## 9. Evidence / handoff receipt
```

The document must include:
- Current date, repo and source revision; status of any client probe.
- At least **two** versions compared, ideally the three specified.
- Every material technical claim attached to source ID and URL/heading.
- **Documented fact**, **inference**, **observation** and **unknown** kept distinct.
- Exactly one recommended starting revision (provisional if no trace).
- A compact unsupported/unknown client-behavior table.
- No unsupported claims of Browser compatibility.

**Receipt in section 9:**
```text
Task: B0-A
Base commit:
Files changed: docs/compatibility-target.md only
Commands/tools used:
Source documents accessed:
Observed client build/version: UNKNOWN | value
Live handshake captured: NO | YES (+ evidence)
Recommended revision:
Decision status: PROVISIONAL | SUPPORTED BY CLIENT OBSERVATION
Tests: NOT APPLICABLE (research only)
Evidence limitations:
Commit SHA:
Independent review: PENDING
Owner acceptance: PENDING
```

If GitHub commit/push is unavailable, say so and provide the finished Markdown for an owner to commit. Never invent a commit SHA.

## 6. Acceptance checklist

The owner/reviewer must be able to check every box:

- [ ] Only `docs/compatibility-target.md` was added/modified.
- [ ] The S1–S8 documentation was consulted, or each failed source was explicitly marked unavailable.
- [ ] The candidate version matrix includes the nine requested dimensions and exact citations/sections.
- [ ] Bolt 5.5 is **not** proposed.
- [ ] Manifest v1 and the difference between ordinary TCP and Bolt over WebSocket are correctly described.
- [ ] Neo4j **server** compatibility is not misrepresented as Browser/driver client compatibility.
- [ ] A first protocol revision is named and is correctly marked provisional if no handshake was observed.
- [ ] No fake packet capture, client version, test success or server compatibility claim was introduced.
- [ ] The live probe is described, with status `NOT RUN` or actual sanitized evidence.
- [ ] No production files, packages, branching or worktrees were created.
- [ ] The research receipt identifies evidence gaps and requests independent review.

**STOP when the document and receipt are complete.** The owner must review the candidate before B0-B or version-sensitive implementation begins.

## 7. Single-message prompt for DeepSeek / GLM Flash

> Complete task **B0-A only** in `jenolaszlo-sketch/bolt-sharp`, reading `docs/tasks/B0-A-protocol-reconnaissance.md` and its two parent docs first. Read official Neo4j Bolt documentation S1–S8 and create **only** `docs/compatibility-target.md` using the exact nine headings and acceptance checklist. Compare Bolt 5.8, 6.0 and 6.1, identify version-specific handshake/messages/PackStream differences, then select exactly one **provisional** starting revision unless you have real client negotiation evidence. **Do not implement code, modify other files, install software, invent a Browser trace, or continue with another task.** Cite official source URL and section for each important fact. If a live client is unavailable, mark the trace `NOT RUN` and describe how to validate it later. Provide the handoff receipt and stop for owner/independent review.
