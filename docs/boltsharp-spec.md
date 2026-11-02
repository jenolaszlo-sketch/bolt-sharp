# BoltSharp — Backend-neutral Bolt server for .NET

**Spec:** BS-001  
**Status:** Proposed, not implemented  
**Date:** 2026-11-02  
**Repository:** https://github.com/jenolaszlo-sketch/bolt-sharp  
**Initial target:** .NET 8, build/test with .NET 10 SDK  
**License:** Apache License 2.0 (existing repository license)  
**Package identity:** `BoltSharp` is a candidate; confirm NuGet availability and naming before publication.

## 1. Purpose and success criterion

Build a reusable, **server-side** implementation of the Neo4j Bolt wire protocol in managed .NET. BoltSharp handles protocol negotiation, PackStream, messages, state, streaming, authentication hooks, and network transports. Applications supply a database/query backend.

**Primary product proof:** A current, pinned Neo4j Browser connects to a BoltSharp server using an authenticated, TLS-protected WebSocket and **renders two nodes and their relationship from a synthetic, in-memory backend**. This must work before implementing any LatticeDB-specific adapter. A subsequent proof uses the official `Neo4j.Driver` .NET **client** over native TCP/TLS.

**Why:** Graph stores such as LatticeDB could reuse existing graph visualization tools instead of maintaining their own. BoltSharp must remain useful for other databases and must not claim to be a complete Neo4j server.

**Non-claims until verified:** No public Bolt compatibility, Neo4j Browser support, particular Bolt version, driver compatibility, NuGet availability, or security audit is claimed merely by creating this spec.

## 2. Design principles

1. **Protocol engine independent of transport and storage.** Raw TCP and WebSockets feed identical ordered byte streams into one Bolt engine, with explicit connection ownership.
2. **Standards by exact revision.** Select a specific Bolt minor version, message/state table, and PackStream revision. Do not implement generic "5.x" behavior or advertise unimplemented features.
3. **Small evidence-driven slices.** Codec first, handshake/framing second, sessions third, reference backend fourth, real clients afterward.
4. **Correctness over simulated success.** No invented bookmarks, routing topology, transaction commits, server versions, cancellation guarantees, or graph identities.
5. **One canonical checkout.** Use `main` for implementation after spec acceptance. Avoid linked worktrees and extensive branch fan-out; retain unique commits and evidence before integration.
6. **Adversarial verification.** Every layer gets positive, boundary, malformed, truncated, and lifecycle tests; independent review is distinct from implementation.
7. **Low running cost.** No external paid services or required cloud test infrastructure. Synthetic backend, local Neo4j test instance, and GitHub Actions where available. No LLM/runtime dependency in produced packages.

## 3. Scope

### Required for initial proof

- Correct PackStream primitives and versioned structure types required by the chosen Bolt revision.
- Legacy and/or Manifest v1 Bolt handshake as determined by a real client compatibility probe; no unsupported version advertisement.
- Bolt chunked message framing including split/coalesced reads, partial writes and explicit end-of-message markers.
- Connection/session finite state machine with `HELLO`, `LOGON` when required, `RUN`, `PULL`, `DISCARD`, `RESET`, `GOODBYE`, `SUCCESS`, `RECORD`, `FAILURE`, `IGNORED` and any **additional observed required** messages.
- Typed response values for node, relationship, scalars, lists/maps and properties; correct relationship direction/IDs.
- Backend interface that supplies a bounded, read-only result stream and explicit resource lifetime.
- Kestrel WebSocket transport over TLS for Browser. Native TCP/TLS support for official .NET driver in the subsequent milestone.
- Basic authentication via a pluggable verifier; failure and unsupported modes rejected. Test credentials used solely in samples.
- Safe defaults: loopback binding, TLS for browser access, origin/host restrictions, limits, deterministic cleanup and an honest feature matrix.

### Not initially required

- Cypher parser or query translator; LatticeDB driver or native P/Invoke; writes; full Neo4j transactions; routing/clustering/SSR; administrative commands; APOC/GDS; every graph/temporal/spatial type; arbitrary Neo4j plugins.
- Neo4j HTTP Query API; Browser bundling; database administration UI; custom graph rendering.
- A default publicly accessible service, remote authentication infrastructure, or open proxy.
- True query interruption when the backend cannot interrupt the operation; fake asynchronous wrappers around synchronous APIs.
- Comprehensive Bolt 4.x / 5.x / 6.x compatibility. More versions follow evidence, not assumptions.

## 4. Protocol research baseline and version gate (B0)

Use authoritative specifications:

- Overview: https://neo4j.com/docs/bolt/current/
- Compatibility: https://neo4j.com/docs/bolt/current/bolt-compatibility/
- Handshake, including 5.7 Manifest v1: https://neo4j.com/docs/bolt/current/bolt/handshake/
- Messages and protocol-specific changes: https://neo4j.com/docs/bolt/current/bolt/message/
- Server states: https://neo4j.com/docs/bolt/current/bolt/server-state/
- PackStream: https://neo4j.com/docs/bolt/current/packstream/
- Graph structure semantics: https://neo4j.com/docs/bolt/current/bolt/structure-semantics/
- Browser connection behavior: https://neo4j.com/docs/browser/operations/dbms-connection/

As of this spec, the Neo4j compatibility matrix includes Bolt **5.8**, **6.0**, and **6.1**; 5.5 is not a negotiable Bolt server version. Bolt 5.7 introduced Manifest v1 handshaking and different failure metadata. Bolt 6.1 adds PackStream 1.1 UUID support. A **candidate** first implementation is Bolt 5.8 + PackStream 1, subject to a real Browser/client negotiation trace. Do not assume this candidate will work.

**B0 task:** Pin exact Browser build/date and official .NET driver version. Capture a legitimate handshake from a local disposable Neo4j instance or a reproducible driver probe, recording offered versions, manifest negotiation, selected version, TLS mode, required startup messages, and metadata. Avoid recording credentials. Determine whether Bolt 5.8 works with both clients. If not, choose the smallest compatible alternative and record an ADR.

**B0 acceptance:** `docs/compatibility-target.md` names one exact protocol revision and the evidence supporting it. A version feature table identifies permitted messages, fields and graph structures. Nothing is called "Neo4j compatible" yet.

## 5. Proposed module boundaries

```text
src/
  BoltSharp/                      # versioned protocol core; no ASP.NET and no DB
    PackStream/                   # encoder, decoder, typed values
    Framing/                      # chunked message parser/writer
    Handshake/                    # protocol negotiation
    Messages/                     # version-specific message definitions
    Sessions/                     # state and cursor lifetime
    Backend/                      # backend-neutral contracts
  BoltSharp.AspNetCore/           # optional Kestrel WebSocket endpoint integration
  BoltSharp.Tcp/                  # optional native TCP/TLS host (later)
tests/
  BoltSharp.Tests/               # pure unit/fixture tests
  BoltSharp.ProtocolTests/       # fragmented bytes, state, fake backend
  BoltSharp.IntegrationTests/    # real Browser/driver smoke; opt-in where needed
samples/
  BoltSharp.ReferenceServer/     # small deterministic graph backend
docs/
  boltsharp-spec.md
  implementation-tasks.md
  compatibility-target.md        # created by B0
  compatibility-matrix.md        # expanded after client tests
```

These are **logical modules**, not a requirement to publish three separate NuGet packages immediately. If a single binary plus optional ASP.NET host is simpler, preserve these boundaries and defer package split. No references from `BoltSharp` core to `Neo4j.Driver` (except test projects), `LatticeDbSharp`, or `Microsoft.AspNetCore.*`.

API names below are sketches pending contract tests, not pre-approved signatures:

```csharp
public interface IBoltBackend
{
    ValueTask<IBoltSession> OpenSessionAsync(
        BoltSessionContext context,
        CancellationToken cancellationToken);
}

public interface IBoltSession : IAsyncDisposable
{
    ValueTask<IBoltResult> RunReadAsync(
        string statement,
        IReadOnlyDictionary<string, BoltValue> parameters,
        BoltRunOptions options,
        CancellationToken cancellationToken);
}

public interface IBoltResult : IAsyncDisposable
{
    IReadOnlyList<string> Fields { get; }
    ValueTask<BoltBatch> PullAsync(int maxRecords, CancellationToken cancellationToken);
    ValueTask DiscardAsync(CancellationToken cancellationToken);
}
```

Review transaction boundaries, `PULL(n=-1)` semantics, whether `PullAsync` can safely expose batches, cursor IDs, and disposal **before** stabilizing this API. This contract must not force providers to claim interruptible execution. A synchronous backend adapter may need a separately governed execution surface and concurrency limit.

**Public value model:** immutable typed `BoltValue` including Null, Boolean, signed Int64, Double, Bytes, UTF-8 String, List, Map, Node, Relationship. For 5.x graph structures, include numeric IDs and opaque element IDs as specified. Node/relationship IDs must not alias by raw numeric value; endpoints and labels/properties are authoritative backend-supplied data. Reject unsupported types rather than serializing them as arbitrary strings.

**Ownership:** transport owns socket/stream; protocol connection owns session; session owns query cursor; disposing or dropping connection disposes outstanding results and session. Backend chooses whether it owns its database; BoltSharp never closes a database it does not own.

## 6. Protocol mechanics and behavior

### 6.1 PackStream (B1)

Build a separate `PackStreamReader` / `PackStreamWriter` with no network dependencies. Cover:
- null, Boolean, signed 64-bit integer, float64, bytes, UTF-8 strings;
- tiny and extended lists/maps, tiny structures and signature tags;
- values at marker boundaries: -16/-17, 127/128, signed integer thresholds, list/map lengths 15/16, sizes 255/256 and 65535/65536;
- big-endian interpretation, overflow and underflow rejection, malformed UTF-8, duplicate map keys per documented semantics, unknown markers, recursion depth and allocation limits;
- round-trip and **independent byte-vector tests** from the PackStream specification (round trips alone can hide matching writer/reader bugs);
- graph structures encoded **per negotiated version**, not as ordinary dictionaries.

PackStream is **not** MessagePack and does not provide unsigned 64-bit numbers. Never cast an arbitrary unsigned native graph ID to a signed 64-bit integer without a proven bound.

### 6.2 Framing and handshake (B2)

- Establish the Bolt magic `60 60 B0 17` and four version proposals, honoring ordering and version ranges correctly.
- Implement Manifest v1 if selected by B0: recognized marker `00 00 01 FF`, supported-version list, VarInt capabilities, client selection and pipelined first message; reject unadvertised capabilities.
- Implement normal chunked messages with 16-bit big-endian chunk lengths and zero-length end-of-message sentinel. The **maximum individual chunk** is 65,535 bytes; a complete message may span chunks but is bounded by configured total-message limits.
- Handle partial reads/writes, merged messages, multiple pipeline messages, EOF mid-handshake or frame, invalid lengths, timeouts, resource disposal and unknown protocol versions without crashing the listener.
- Never use WebSocket frame boundaries as Bolt message boundaries. WebSocket frames carry an ordered byte stream for the Bolt parser.

**B2 acceptance:** In-memory fragmenting transport can deliver the same byte sequence in arbitrary splits and the protocol reconstructs exactly the same messages; malformed messages fail deterministically.

### 6.3 Session and message state (B3)

Model protocol states directly from the **selected** Neo4j state specification. For Bolt 5.1+ this includes separate negotiation/authentication and `LOGON` after `HELLO`.

Required exercised flows:

```text
handshake
HELLO -> SUCCESS
LOGON -> SUCCESS                 # for negotiated versions requiring it
RUN read-only query -> SUCCESS(fields, ...)
PULL(n=...) -> RECORD* + SUCCESS(has_more...)
DISCARD -> SUCCESS
RESET -> SUCCESS (when legally recoverable)
GOODBYE -> disconnect without fabricated summary
```

- Enforce which messages are valid in each state, per-version metadata and summary fields, and correct failed/ignored/defunct behavior.
- Support pipelined requests with correct ordering and backpressure. Do not execute queued requests once an earlier failure requires recovery.
- `PULL` must honor finite `n` and `has_more` without draining unbounded results. `DISCARD` disposes or advances the cursor according to version-specific semantics.
- For `RESET`, implement what is achievable and **do not claim preemption of a non-interruptible backend query**. If protocol conformance requires immediate cancellation, block the version/client compatibility claim until that requirement is resolved.
- On unsupported explicit transactions, routing `ROUTE`, impersonation, bookmarks, writes or metadata, return the *version-appropriate* documented failure/recovery behavior. Never return a fabricated `SUCCESS`.
- Version-specific failures: Bolt 5.7+ requires additional GQL status metadata; use exact selected revision rather than a generic error dictionary.
- Be explicit about auth: allow an injected credential verifier, reject invalid credentials and unsupported schemes, never accept arbitrary tokens or log them.

### 6.4 Transports (B4/B5)

**B4 WebSocket:** an opt-in `BoltSharp.AspNetCore` endpoint using Kestrel, configured TLS, allowed origins, host filtering, authentication, loopback default, explicit listen/stop. Validate upgrade handshake and Browser's actual URL/path/proxy expectations from a current Browser trace. Test no-WebSocket, text frames, binary fragmentation, ping/close, client abort, invalid auth, and CORS/Origin where applicable. HTTPS certificate must be trusted by the browser. In-process reference sample returns a fixed graph.

**B5 TCP:** native TCP with explicit TLS using `SslStream`, same Bolt engine, bounded connections and timeouts. Official `Neo4j.Driver` is used **only as a test client**. No routing table response or cluster discovery unless actually supported. Verify direct `bolt+s://` connection or another documented direct-driver mode, not `neo4j+s://` routing. Correct port and connection options must be in the smoke test.

**Security:** Loopback-only and authenticated by default for examples. No anonymous port reachable from the LAN. No plaintext credentials over unencrypted remote transport. Do not offer `bolt+ssc` as secure-equivalent to trusted certificates. Bound connection count, queued requests, result rows/bytes, total decoded bytes, string/map/list sizes, recursion depth, idle lifetime, and outstanding cursors; specify usable defaults with tests. Protect against resource exhaustion and DNS rebinding/Host/Origin bypass where relevant. A read-only backend must enforce read-only at its transaction boundary, not through keyword scanning.

## 7. Reference backend and client proof

Use a tiny **deterministic, synthetic** backend:
- Node A: numeric ID 1, element ID `n:1`, `Person`, `name=Alice`.
- Node B: numeric ID 2, element ID `n:2`, `Person`, `name=Bob`.
- Directed relationship: numeric ID 1, element ID `r:1`, `KNOWS`, from A to B, property `since=2024`.
- This intentionally reuses numeric ID `1` for node and relationship to catch type aliasing bugs.
- Read-only, fixed allowlisted statement(s); no fake general Cypher execution. Parameterized bounded `MATCH (a)-[r]->(b) RETURN a,r,b` can be recognized exactly in the reference backend only, with explicit unsupported errors for arbitrary Cypher.
- The exact `Node`/`Relationship` PackStream field layout follows the negotiated protocol revision and its structure semantics.

**B4 acceptance:** A current Neo4j Browser, version recorded in evidence, connects via WSS to the reference server and displays both labeled nodes, one directed relationship, and properties. Keep a sanitized connection trace/screenshots and note Browser functionality that does *not* work.

**B5 acceptance:** Official Neo4j .NET driver, exact package version pinned in the test, connects via direct TCP/TLS and returns correctly typed `INode`/`IRelationship` values. The driver must not be treated as a server-side NuGet dependency.

This is a **client-specific subset qualification**, not a certification of complete Bolt/Neo4j interoperability.

## 8. Verification strategy

### Mandatory unit and protocol cases

1. Spec-sourced golden bytes and boundary markers; no reliance exclusively on self-round-trip.
2. Arbitrary segmentation/chunking and pipelining across TCP and WebSocket adapters.
3. Valid versus invalid version/manifest negotiation, capabilities, endianness and truncated streams.
4. Valid/invalid state transitions, auth required, failed/ignored/reset, `PULL` pagination and cursor cleanup.
5. Node versus relationship ID collision, labels/properties/direction, nested maps, unsupported type errors.
6. Disconnection during handshake, query, records or TLS; no leaked session/cursor and clean host shutdown.
7. Limits/fuzzing for malformed PackStream, deep nesting, huge declared sizes, oversized chunks/messages and concurrent requests.
8. Correct rejection of write queries by backend-enforced read-only semantics and explicit unsupported Bolt features.
9. Browser/official driver differential smoke against real clients; pin versions and keep sanitized traces.
10. Linux, Windows, macOS managed test gates as feasible; no platform support claim without actual passed tests.

**Every claim must bind to:** exact source commit, command, test output, environment, accepted revision and client/version when relevant. Independent reviewer should attempt to falsify protocol conformance, not simply re-read implementation descriptions.

## 9. Milestones and hard gates

| Gate | Implementation slice | Exit evidence |
| --- | --- | --- |
| B0 | Pin protocol/version, Browser and client expectations | `compatibility-target.md` with sources, trace and decision |
| B1 | PackStream codec only | Golden vectors, boundary/malformed tests, standalone green suite |
| B2 | Handshake + chunk framing only | In-memory split/pipeline/negative protocol proof |
| B3 | State machine + backend contract | Scripted HELLO/LOGON/RUN/PULL/RESET/GOODBYE and failure recovery |
| B4 | Kestrel WebSocket + reference backend | **Real Browser visually renders synthetic graph** |
| B5 | TCP/TLS + official driver | Driver returns typed graph using same backend |
| B6 | Security, cross-platform, package hygiene | Independent review, compatibility matrix, clean consumer |
| L0 (other repo) | LatticeDbSharp graph entity ABI fix/adapter | Only after B4; separately governed |

No gate can be accepted based solely on compilation or a screenshot of a synthetic HTTP response. **Stop rather than widen scope** if B0 finds a client requires unimplemented, high-cost features. Further protocol versions follow a new explicit compatibility milestone.

## 10. LatticeDbSharp integration boundary (future project)

**Do not implement LatticeDB in this repository.** A later `LatticeDbSharp.Bolt` package implements BoltSharp's backend contract, hosted against an existing `LatticeDatabase` instance.

At LatticeDB v0.15.0 pinned by LatticeDbSharp, the C API converts query node/edge references to ordinary integers and lacks general property enumeration. This must be resolved in the native/wrapper layer with typed, snapshot-correct graph entities before the adapter can honestly render a LatticeDB graph. Do not infer graph type from integers or names. Any native changes follow LatticeDbSharp's pinned ABI/provenance/OS gates.

Adapter and graph engine remain independent consumers of BoltSharp; BoltSharp cannot special-case LatticeDB.

## 11. Contributor and AI-agent working contract

- Read this spec and `docs/implementation-tasks.md` before coding. Work **only one assigned task** at a time. Follow task preconditions and do not jump ahead because a neighboring feature appears easy.
- No speculative extra packages, reflection-heavy abstractions, source generators, logging frameworks or unnecessary repository scaffolding.
- Keep public API drafts minimal and reversible until end-to-end evidence exists. Prefer `System.Buffers`, `System.IO.Pipelines` only when justified by measured needs; a correct simple reader/writer is fine for first proof.
- Produce source and focused tests, run them, report exact command/output and remaining unsupported behavior. Do not claim tests passed without execution.
- Test invalid input and cleanup before acceptance. Record contradictions and assumptions in a short task receipt.
- Use the same canonical checkout; prefer small direct commits to `main` after this specification is accepted, not many concurrent worktrees. Preserve evidence and unique source changes before cleanup.
- A separate reviewer/model checks against the official protocol docs and golden fixtures; the implementer must remediate and re-run tests before the owner accepts.
- Do not publish any NuGet package or publicly exposed server without explicit owner acceptance and completed security/client qualification.

**Next action:** Complete B0, then delegate **B1-A: PackStream core scalar codec** from the task ledger. That is an appropriate initial slice for inexpensive/free coding models and requires no sockets, browser, or database.

## 12. Open decisions

- Exact first Bolt revision and Manifest v1 need based on current clients.
- Whether single package or optional `BoltSharp.AspNetCore` / `BoltSharp.Tcp` NuGet packages minimize consumer dependencies.
- Backend result/cursor API, especially truthful cancellation and streaming for synchronous providers.
- Minimal authentic Browser metadata/procedure expectations, including whether a synthetic backend suffices for initial graph display.
- Versioned temporal/spatial/path/UUID support; reject unsupported values until required.
- OSS package name availability, accurate project branding, release readiness.

**Design acceptance:** This spec is a starting contract. Revise B0 assumptions based on real version/trace evidence before shipping production protocol messages.