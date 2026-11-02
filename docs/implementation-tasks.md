# BoltSharp implementation ledger — small-model assignments

**Status:** Proposed; implementation has not started.  
**Governing contract:** [boltsharp-spec.md](boltsharp-spec.md)  
**Working policy:** One canonical checkout. Review and merge the spec branch first. Then implement sequential small slices on `main` unless an isolated high-risk experiment requires a short-lived branch. No linked worktrees or package publication by default.

## How to use with inexpensive/free coding models

Each task below is a **single bounded assignment**, not an invitation to implement the full Bolt server. Give the implementer this ledger, the spec, and exactly one task ID. Ask for a patch/commit plus tests and a short evidence receipt. Ask a second model to review the diff with the protocol specification open. If review fails, return only the findings for remediation, then requalify.

Prefer free models for B0 research, B1 codec, B2 framing and unit tests. Reserve a stronger model for the protocol-version choice, backend API/transaction semantics, failure-state reconciliation and final client/security review. The acceptance criterion is **evidence**, not model confidence.

### General acceptance receipt, required for every task

```text
Task:
Baseline commit:
Changed files:
Selected protocol revision (where applicable):
Commands run:
Tests: passed / failed / not run (with counts and reasons):
New golden fixtures (source URL and exact section):
Known limitations / unsupported cases:
Source commit:
Independent reviewer verdict:
Owner acceptance: PENDING
```

Never state green CI from locally unrun tests. A reviewer verdict is not an owner's final acceptance. Do not publish packages from a coding task.

## B0 — Protocol decision and project bootstrap

**B0-A: Version and Browser compatibility reconnaissance**

Files: `docs/compatibility-target.md` only; no production code.

Read the official Bolt compatibility, handshake, message, state and structure specification. Identify at least two candidate target revisions and their required differences (e.g. 5.8 vs 6.0/6.1; 5.7 Manifest v1, `HELLO`/`LOGON` and failure metadata, PackStream 1.1 UUID for 6.1). Record a provisional selection and exact citations. Attempt to verify offered/chosen revisions using current Browser / official driver against a local Neo4j instance; **if this is not available, mark the version choice PROVISIONAL and explicitly leave the live trace test open**. Do not invent one. Record current client version only if observed.

**Acceptance:** A table of revision-specific capabilities, explicit selection rationale, unverified assumptions and a plan to obtain actual negotiation evidence. `Bolt 5.x` alone is not a selection.

**B0-B: .NET solution scaffold**

Files: `BoltSharp.slnx`, `src/BoltSharp/BoltSharp.csproj`, `tests/BoltSharp.Tests/BoltSharp.Tests.csproj`, `Directory.Build.props` if necessary, and a minimal `.gitignore`.

Target `net8.0` with .NET 10 SDK; choose a conventional test framework already available offline/from normal NuGet restore. No ASP.NET dependencies in protocol core. Add one smoke test proving successful build/test. Do not add native libraries, database adapters, elaborate analyzers or additional packages just for scaffolding.

**Acceptance:** `dotnet restore`, `dotnet build`, `dotnet test` green from a clean checkout, commands and tool versions recorded. CI comes later.

## B1 — PackStream, no networking

**B1-A: Scalars and byte encoding**

Files limited to a small reader/writer and tests under `PackStream/`.

Implement **only** Null, Boolean, signed Int64 and Float64 using big-endian, explicit marker boundary handling and deterministic errors for truncated/unsupported markers. Do not add messages, graph types or a server. Prefer no third-party serialization package.

**Acceptance:** spec-sourced independent encoded-byte assertions for -17, -16, -1, 0, 1, 127, 128, 255, 256, Int64 min/max; float64 finite, signed zero and NaN as represented; malformed/truncated cases. Round-trips alone are insufficient.

**B1-B: Strings and bytes**

Depends on B1-A. Implement UTF-8 strings and bytes with tiny and length-prefixed markers, strict size/UTF-8 validation, no unbounded allocations. Document configured max byte/string length independently of spec theoretical maxima.

**Acceptance:** golden 0/15/16/255/256/65535/65536 lengths where suitable, non-ASCII, invalid UTF-8, early EOF, huge advertised length, allocation limit and cross-chunk independence. No network framing yet.

**B1-C: Lists, maps, structures**

Depends on B1-B. Implement recursive containers with nesting and element count caps; map string-key enforcement and documented duplicate-key behavior; structure tag and field count. Support tiny/extended lengths faithfully.

**Acceptance:** source-derived golden vectors for empty, 1, 15, 16, 255, 256 entries; nested combinations; duplicate map keys; invalid marker, deep recursion, overflowing lengths and truncated data. Verify a malformed input cannot cause runaway recursion/memory.

**B1-D: Graph structures**

Depends on B1-C and B0 version choice. Implement Node, Relationship and any required UnboundRelationship/Path representations **only for the chosen protocol revision**. Preserve numeric and opaque element IDs independently, typed labels, endpoint identity and property maps; no conversion of graph structures to plain maps.

**Acceptance:** fixture with Node(1), Relationship(1) and Node(2) decodes correctly without aliasing the two entities named ID 1. Exact protocol field-order golden tests; disallow unsupported graph/time/spatial/UUID structures until explicitly implemented.

## B2 — Handshake and framing, no query backend

**B2-A: Chunked message codec**

Depends on B1-C (does not need graph structures). Implement 16-bit big-endian chunks and zero-length end sentinel over an abstract ordered byte-stream interface. Message cap distinct from per-chunk cap. Split/coalesced writes, multiple back-to-back messages and EOF handling.

**Acceptance:** 0, 1, 65,535 and 65,536 payload bytes; random segmentation; pipelined messages; truncated header/body; repeated marker; oversized total message; no framing reliance on network/WebSocket frame boundaries.

**B2-B: Bolt handshake**

Depends on B2-A and B0. Implement Bolt magic and four proposals, version ranges and advertised selected version. Add Manifest v1 + VarInt offer/selection/capabilities if B0 requires it. Reject nonmatching revisions, invalid capabilities, malformed lengths and early disconnect.

**Acceptance:** official handshake golden byte sequences, 5.7 manifest-vector tests where applicable, mixed TCP buffer fragmentation, unsupported version produces correct response and close, and no advertised unsupported protocol version.

## B3 — Protocol session, backend abstraction and reference result

**B3-A: Message schemas for pinned revision**

Depends on B1-D/B2-B. Implement structured request and response codecs for required `HELLO`, `LOGON`, `RUN`, `PULL`, `DISCARD`, `RESET`, `GOODBYE`, `SUCCESS`, `RECORD`, `FAILURE`, `IGNORED`, plus observed extras required by the selected revision. Keep schemas version-bound. Do not implement session handling in this task.

**Acceptance:** byte fixtures and negative arity/tag/field tests. `FAILURE` metadata conforms to the selected revision. Unknown messages are not mis-decoded as successful no-ops.

**B3-B: Connection state machine**

Depends on B3-A. Implement legal transitions, authentication required before queries, `FAILED`/`IGNORED` recovery, `GOODBYE` semantics and deterministic disconnect. Do not pretend noninterruptible backends support `RESET` preemption. No socket code.

**Acceptance:** scripted valid and invalid transitions, pipelined RUN/RESET behavior, unauthorized query failure, invalid message puts connection in defined state/closure, and no cursor leak.

**B3-C: Backend contract and synthetic graph**

Depends on B3-B. Implement minimal `IBoltBackend`/session/result contract and one reference backend with deterministic `Person` nodes (Alice/Bob) and `KNOWS` relationship. Recognize only explicitly supported query string(s); reject all other queries, writes and transaction modes. Define `PULL(n)`, `has_more`, `DISCARD` and disposal. Do not build a general Cypher parser.

**Acceptance:** protocol tests produce `RUN` fields and `RECORD` values; `PULL(1)` twice returns records as expected for test rows; `DISCARD` closes remaining cursor; no query runs before auth; native graph identity is preserved and unknown query fails.

## B4 — Neo4j Browser via WebSocket

**B4-A: ASP.NET Core WebSocket adapter + reference host**

Depends on B3-C. Add opt-in WebSocket endpoint and a small reference host. Kestrel owns upgrade/TLS and the stream adapter; core protocol gets bytes, not ASP.NET request types. Support graceful close, disconnect and bounded clients; reject text frames/non-upgrade; configure trusted cert and loopback-only binding in sample. No public listening by default.

**Acceptance:** automated WebSocket client proves handshake, protocol byte stream, auth and query lifecycle with fragmented frames. Invalid origin/host/credentials and unexpected disconnections fail safely.

**B4-B: Real Browser probe (mandatory product proof)**

Depends on B4-A. Pin Browser build/driver and test actual connection using WSS. Use a sanitized browser network trace. If startup sends unexpected `ROUTE`, metadata or transactions, document exact requests and determine minimal correct behavior in an ADR; **do not fake Neo4j responses**. Add only genuinely necessary handlers as separately reviewed changes.

**Acceptance:** Browser graph renders exactly two nodes and one directed relationship with correct labels/properties/identity, or an explicit NO-GO report. `curl` or a unit test cannot replace this proof.

## B5 — Native TCP and official driver (second product proof)

**B5-A: TCP/TLS transport**

Depends on B3-C. Implement optional TCP listener and TLS via `SslStream`, sharing the same protocol engine. No routing/cluster semantics.

**Acceptance:** in-process test client negotiates, auths and reads over TLS; limit/idle/close cases pass; no leaked socket/task.

**B5-B: Official Neo4j .NET driver test**

Depends on B5-A and B0/B3 revision gates. Pin `Neo4j.Driver` in integration tests **only**. Use direct Bolt URI (not a routing scheme) and verify actual driver typed nodes and relationships from reference backend.

**Acceptance:** executable driver integration test with version/commit/OS/TLS evidence; unknown metadata requirements logged honestly.

## B6 — Security, CI, publishing decision

**B6-A: Adversarial protocol hardening**

Depends on B4-B and B5-B. Fuzz malformed PackStream/framing, oversized declared lengths, invalid protocol state, pipelining after failures, connection abuse, TLS/cert failure and cancellation/cleanup races; add precise limits and resource-leak tests.

**B6-B: CI and package-only consumer**

Build/test across Windows, Linux and macOS runners, pin dependencies, publish no artifacts by default. Create clean consumer sample; verify XML docs, license inclusion, version/capability matrix and NuGet package contents. Run an independent adversarial review.

**Acceptance:** no unresolved high-risk protocol/security issues, repeatable client integration proof and explicit owner permission before any public preview release.

## Copy/paste first assignment

Use this when handing B1-A to a free coding agent **after B0/solution scaffolding**, and include links to the spec and protocol docs:

> Implement **B1-A (PackStream scalar codec only)** in `jenolaszlo-sketch/bolt-sharp`. Read `docs/boltsharp-spec.md` and `docs/implementation-tasks.md` first. Work on the one canonical checkout, preserving existing files. Scope strictly to Null, Boolean, signed Int64 and Float64 serialization/deserialization and focused tests; no sockets, WebSocket, messages, graph models, broad abstractions or additional dependencies. Use published Neo4j PackStream golden bytes and test all marker boundaries, big-endian behavior, truncation and unsupported markers. Run `dotnet test`; provide changed-file list, commit SHA, exact test results, protocol-doc citations, known limitations and an evidence receipt. Stop when tests pass; do not declare the task owner-accepted or start B1-B. If B0 is still provisional, only implement spec-stable PackStream primitives; do not select an unverified Bolt version.

## Separate independent-review assignment

> Review only task **B1-A** of `jenolaszlo-sketch/bolt-sharp` against `docs/boltsharp-spec.md`, `docs/implementation-tasks.md` and the published PackStream spec. Treat the code as potentially incorrect. Check independently known byte vectors, every marker boundary, signed arithmetic, endianness, truncated/invalid inputs, allocation safety, error categorization and tests that could pass despite incorrect encoder/decoder symmetry. Run tests where possible and report concrete findings by severity with file/line and reproduction. Do not implement unrelated features, alter the package scope, or mark acceptance yourself.

## Scope-control reminders

- Build only what a **real client** needs to render a graph.
- Unknown/unsupported features must not be fabricated.
- Different models can implement and review without supervising each other; model agreement is not verification.
- When a task fails, fix that task and requalify. Do not widen protocol coverage to avoid addressing the failure.
- Recording unsupported capabilities is a successful result of an honest feasibility probe.
