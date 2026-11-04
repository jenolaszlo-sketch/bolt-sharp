# B2 — Bolt integration scope, contract, acceptance and qualification plan

**Status:** AUTHORIZED TO BEGIN — scope established; one owner confirmation requested (§8)
**Date:** 2026-11-04
**Canonical checkout:** `main` (owner acceptance commit `4583293`, see
`docs/reviews/B1-A1-packstream-owner-acceptance.md` §11)
**Governing contract:** [boltsharp-spec.md](../boltsharp-spec.md) §6.2 and §9;
[implementation-tasks.md](../implementation-tasks.md) B2;
[compatibility-target.md](../compatibility-target.md) (B0-A, provisional)

This document is planning and qualification scope only. It contains **no implementation**.
It makes **no claim** of Neo4j, Neo4j Browser, or official `Neo4j.Driver` compatibility.

---

## 1. Purpose and reconciliation with the governing spec

The owner authorized B2 to begin on canonical `main` and named the *intended next area* as
**"Bolt protocol integration and mock-server compatibility with the official Neo4j .NET
driver"**, with an explicit rule: **driver compatibility must not be claimed until it is
independently demonstrated by executable integration tests.**

The governing spec defines the milestone boundaries differently:

| Milestone | Spec definition | Exit evidence |
| --- | --- | --- |
| B2 | Handshake + chunk framing only | In-memory split/pipeline/negative protocol proof |
| B3 | State machine + backend contract | Scripted `HELLO`/`LOGON`/`RUN`/`PULL`/`RESET`/`GOODBYE` and failure recovery |
| B5 | TCP/TLS + official driver | Driver returns typed graph using the same backend |

A real driver connection cannot succeed on framing and handshake alone: the driver performs
a handshake and then immediately sends `HELLO` (and `LOGON`), and fails if those are not
answered correctly. Therefore **"mock-server compatibility with the official driver" is a
B3/B5-level proof, not a B2 deliverable.** B2 can only build the foundation and the harness
that the later proof will use.

**Resolution adopted here (flagged for owner confirmation, §8):** B2 delivers the
spec-bounded foundation **plus** the integration harness and a pinned, *gated* driver test
scaffold. The driver-compatibility claim stays exclusively at the later executable
integration milestone.

## 2. Scope

### In scope

- **B2-A — Chunked message codec.** 2-byte big-endian chunk headers, `00 00` end-of-message
  sentinel, 65,535-byte maximum chunk, a total-message cap distinct from the per-chunk cap,
  split/coalesced byte streams, back-to-back messages, and deterministic EOF/truncation
  handling — over an abstract ordered byte-stream interface, with no socket or ASP.NET
  dependency.
- **B2-B — Bolt handshake.** Magic `60 60 B0 17`, exactly four 32-bit big-endian version
  proposals, legacy selection response (a version, or `0` then close), and Manifest v1
  (marker `00 00 01 FF`, supported-version list, VarInt capabilities, client selection,
  pipelined first message) per the B0-A decision.
- **B2-C — Integration harness.** An in-memory fragmenting transport and a pinned
  `Neo4j.Driver` integration-test scaffold. The driver tests are **compiled but gated
  (skipped) until the protocol path is complete**, and assert nothing about compatibility
  while gated.

### Out of scope (do not implement in B2)

- Message schemas, session/connection state, authentication, backend contract, cursors.
- Sockets, `SslStream`, Kestrel, WebSocket, real Neo4j servers, Neo4j Browser.
- Any Neo4j / Browser / driver compatibility claim.
- NuGet package publication.
- Finalizing a protocol revision (B0-A remains **provisional**; the live client probe is
  still `NOT RUN`).

## 3. Implementation contract

1. **Transport independence.** Framing and handshake operate on an abstract ordered
   byte-stream (async read/write or a pipe/segment abstraction). No dependency on sockets,
   TLS, or WebSocket; no use of transport frame boundaries as Bolt message boundaries.
2. **Framing rules.** Chunk header = `uint16` big-endian; `0x0000` terminates a message;
   a chunk payload is at most 65,535 bytes; the total message is bounded by a configured
   cap; empty `NOOP` chunks (Bolt 4.1+) must be ignored without ending a message or
   crashing. Over-cap, truncated header, and truncated body must fail deterministically
   with a typed exception and a recorded position.
3. **Handshake rules.** Exactly four proposals; big-endian; unsupported versions produce
   the version-appropriate rejection (`0` then close for legacy) — never a fabricated
   success. Manifest v1 offer/response/capabilities follow the B0-A cited revision. The
   server **advertises only versions it actually implements**; no unadvertised capabilities.
4. **Version honesty.** The candidate revision remains Bolt 5.8 + PackStream 1.0 while
   provisional. B2 code must not hard-code behaviour for a revision that the live probe has
   not confirmed; handshake selection is table-driven and reversible.
5. **Limits.** Introduce explicit, documented caps (max chunk is fixed at 65,535; a separate
   total-message cap, a handshake read cap, and any pending-version count cap) with usable
   defaults and tests. No unbounded allocation driven by an attacker-controlled length.
6. **Errors.** Dedicated exception types (e.g. framing/handshake format errors) with
   deterministic, documented behaviour; failures must not leave half-updated public state.
7. **No new core dependencies.** `src/BoltSharp` stays free of ASP.NET, sockets, TLS, and
   `Neo4j.Driver`. Any driver reference is **test-only**.

## 4. Acceptance criteria (evidence, not estimation)

### B2-A — Chunked message codec

- Payload lengths `0`, `1`, `65,535`, `65,536` handled per the framing rules.
- Random segmentation: the same byte sequence split at arbitrary boundaries reconstructs
  exactly the same messages (property-style, multiple seeds).
- Multiple back-to-back and pipelined messages decode in order.
- Truncated header, truncated body, repeated terminator (`00 00`), and oversized total
  message fail deterministically.
- Round-trip encode→fragment→decode equality, plus independent golden chunk vectors
  (not round-trip-only).

### B2-B — Bolt handshake

- Golden byte sequences for the legacy handshake (magic, four proposals, selected version)
  and for the reject path (`0` then close).
- Manifest v1 offer/response round trip with VarInt lengths and capabilities, per B0-A.
- Malformed length, unsupported version, invalid capabilities, and early disconnect fail
  deterministically; no unsupported protocol version is ever advertised.

### B2-C — Integration harness

- The in-memory fragmenting transport feeds the codec the same byte sequence in arbitrary
  splits and yields identical messages.
- A pinned `Neo4j.Driver` integration-test project exists with the **exact package version
  recorded**, and its tests are **gated/skipped** with an explicit reason until the protocol
  path is complete. While gated they must assert nothing about driver compatibility.

## 5. Independent qualification requirements

- **Independence.** The qualifier must not be the implementer. A separate model/human
  reviews against the official Bolt handshake/message specifications and golden bytes, and
  treats the implementation as potentially incorrect.
- **Falsification.** The qualifier attempts to falsify: boundary lengths, split points,
  truncation, repeated markers, manifest/VarInt encodings, version rejection, capability
  handling, and resource-exhaustion attempts. Passing happy-path tests is insufficient.
- **Regression gate.** The B1 baseline must stay green: `dotnet test` must continue to
  report 509 passed (before any B2 tests), 0 failed, 0 skipped; no new core dependencies.
- **Admissibility.** A driver-compatibility claim is admissible **only** with an executed
  integration test against a pinned `Neo4j.Driver` version, with environment, OS, TLS mode,
  command, and output recorded. Compilation, screenshots, or descriptions are not evidence.
- **Evidence binding.** Every claim binds to an exact source commit, command, output,
  environment, and the selected protocol revision.

## 6. Execution sequence

1. **B2-A** chunked message codec + focused tests (first bounded task, §7).
2. **B2-B** Bolt handshake + focused tests (legacy first; Manifest v1 if B0 requires it).
3. **B2-C** in-memory harness + pinned, gated driver test scaffold.
4. **Independent qualification** of B2-A/B2-B, then of the whole B2 deliverable.
5. **Owner acceptance** of B2.
6. Later milestones (B3, B4, B5) build messages/state/transport; **driver compatibility is
   claimed only at the B5-level executable integration proof.**

## 7. First bounded implementation task — B2-A

**Assignment.** Implement the chunked message codec only, over an abstract ordered
byte-stream interface, under `src/BoltSharp/Framing/` with tests under
`tests/BoltSharp.Tests/Framing/`.

**Does.** 2-byte big-endian chunk lengths, `00 00` end-of-message sentinel, 65,535-byte
per-chunk cap, configurable total-message cap, split/coalesced reads, multiple and
pipelined messages, `NOOP` empty-chunk handling, and deterministic truncation/overflow
errors with positions.

**Does not.** Implement handshake, messages, state, backend, sockets, WebSocket, TLS, or any
driver dependency. Do not finalize a protocol revision.

**Acceptance for B2-A.** The §4 B2-A criteria, plus exact command output, new dependency
list (expected: none in core), known limitations, and a task receipt. Stop when tests pass;
do not declare owner acceptance or begin B2-B.

**Receipt template.** Reuse the ledger's general acceptance receipt
([implementation-tasks.md](../implementation-tasks.md) §"General acceptance receipt"),
with `Independent reviewer verdict: PENDING` and `Owner acceptance: PENDING`.

## 8. Open items requiring owner confirmation

1. **B2 boundary.** Confirm the reconciliation in §1: B2 = framing + handshake + gated
   harness, with the driver-compatibility claim deferred to the later executable
   integration milestone — or keep B2 strictly at the spec's handshake+framing boundary and
   defer the harness entirely.
2. **Driver test project timing.** May the pinned, gated `Neo4j.Driver` integration-test
   project be added during B2 (test-only dependency), or should it be deferred to the
   milestone where the integration test can actually run?
3. **Protocol revision.** B0-A remains provisional with no live probe. Confirm that B2 may
   proceed against the spec-stable framing/handshake definitions while the final revision
   selection stays open.

Until these are confirmed, B2-A (which depends on none of them) may begin as the first
bounded task.
