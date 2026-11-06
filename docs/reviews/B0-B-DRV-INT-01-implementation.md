# B0-B-DRV-INT-01 — Official Neo4j driver integration harness and protocol discovery

**Disposition: B0-B-DRV-INT-01: IMPLEMENTED, READY FOR INDEPENDENT QUALIFICATION.** This is a
producer implementation record, not owner acceptance and not an independent qualification.

**Executor:** DeepSeek V4.1 Flash (provider `nvidia`) in OpenCode. Author independence remains
UNVERIFIED; the producer does not self-declare independent qualification.

**Baseline:** `a4429ef969a3f150c4ab1b4908a193ffaddcce2a` (B0-A-TRACE ACCEPTED WITH QUALIFICATIONS,
CLOSED), canonical `main`, one worktree.

**Driver:** `Neo4j.Driver` **6.4.0**, exact pin, test-only, direct `bolt://127.0.0.1:{port}` plaintext
loopback.

**Selected revision:** Bolt **5.8**, PackStream **1.0** (provisional per `docs/compatibility-target.md`).

---

## 1. Scope and non-claims

This milestone builds a reusable integration environment in which the official .NET driver talks to a
controlled Bolt endpoint, records what the driver sends, and establishes the current interoperability
boundary of BoltSharp. It is deliberately **not** a Bolt server implementation.

**Explicitly not established:** no HELLO/LOGON/session/query interoperability with the *BoltSharp
production listener* (that requires B3/B5); no general Cypher processing; no TLS (`bolt+s://`); no
routing/cluster semantics; no Neo4j compatibility or certification; no public network listener; no
production code change. All results are Windows 10.0.26200 / net8.0, plaintext loopback, synthetic
controlled traffic.

---

## 2. Preflight and reuse

- HEAD/tree verified against the B0-A-TRACE acceptance baseline; `main`; single worktree; no tracked
  modifications. No historical evidence, report or accepted test was modified.
- **Reused, not duplicated:**
  - Production B2-A framing (`BoltMessageReader`) and B2-B negotiation (`BoltHandshakeNegotiator`,
    `BoltHandshakeOptions`, `BoltProtocolVersion`) as the request decoder and the real listener.
  - The genuine accepted test-only `LoopbackHandshakeListener` (T08), linked unmodified from
    `tests/BoltSharp.DriverIntegrationTests/HandshakeTrace/` — the same non-invasive reuse pattern the
    historical review probe hosts use.
  - The existing pinned-driver convention (`Neo4j.Driver` 6.4.0, exact) and the environment-driven
    direct-URI validation model.
- **No parallel driver scaffold was created.** `tests/BoltSharp.DriverIntegrationTests/` is unchanged;
  gated D01–D10 remain gated.

### Solution membership (deliberate repository convention)

`BoltSharp.slnx` lists only `src/BoltSharp` and `tests/BoltSharp.Tests`. The driver-dependent project
`BoltSharp.DriverIntegrationTests` is **deliberately excluded** so the core solution restore/build
stays free of the official driver (documented in its csproj and README). The new
`BoltSharp.IntegrationTests` follows the **same convention and is likewise excluded**. Adding it to
`BoltSharp.slnx` would pull `Neo4j.Driver` into the core solution and change the accepted 826-test
core-solution baseline, contradicting the milestone's isolation and baseline-preservation
requirements. This deviation from the literal "add to the solution" suggestion is recorded here for
the owner and the independent reviewer.

---

## 3. Architecture

```text
tests/BoltSharp.IntegrationTests/
  BoltSharp.IntegrationTests.csproj        # xunit + Neo4j.Driver 6.4.0 (exact); links the real listener
  Infrastructure/
    ProtocolFixtures.cs                    # independent PackStream/Bolt byte oracle (no BoltSharp writer)
    ScriptedBoltServer.cs                  # loopback fake server (handshake + scripted messages)
    BoltConnectionScript.cs                # declarative request→reply script; split/coalesce/close/raw
    WireCapture.cs                         # transcript + request model + redaction
    DriverHarness.cs                       # real driver construction (plaintext loopback)
    ScenarioSupport.cs                     # standard scripts, proposal decode, evidence documents
    EvidenceSink.cs                        # env-gated evidence writers (no-op without the env var)
    B0BCollection.cs                       # serializes scenarios
  Scenarios/ HandshakeTests, InitializationTests, QueryTests, GraphValueTests,
             PaginationTests, FailureRecoveryTests, FragmentationTests.cs
  ActualServer/ BoltSharpListenerTests.cs  # T08 against the genuine listener
```

**Fake server.** `ScriptedBoltServer` binds `TcpListener(IPAddress.Loopback, 0)` (OS-assigned port),
accepts a bounded number of connections, performs the handshake, then reads client messages with the
production `BoltMessageReader` and replies from the script. It is cancellation- and timeout-bounded,
disposes every socket/stream/task, uses no fixed port and no sleep to establish a phase, and records a
full ordered transcript. An unexpected request is recorded (never silently ignored) and, with strict
scenarios, causes the test to fail.

**Scripting.** `BoltConnectionScript` maps message names to ordered replies; a reply can emit one or
more messages, write raw bytes (for deliberately incomplete frames), split writes per-byte or in fixed
chunks, coalesce messages into one write, and close the connection. Replies for a name are consumed in
order and the last repeats.

**Request decoding** uses BoltSharp's reader (client→server); **responses** are built only from the
independent fixtures. This split keeps the response oracle independent (see §5).

---

## 4. Required scenarios (B0-B-T01 … T08)

All scenarios execute the **real** `Neo4j.Driver` 6.4.0. Counts below are actual results; evidence is
under `docs/reviews/evidence/B0-B-DRV-INT-01/`.

| Scenario | What it proves | Evidence |
| --- | --- | --- |
| **T01** handshake discovery | Magic + four proposals captured and decoded; legacy 5.8 selected; reject-all fails without HELLO | `t01-handshake.json`, `t01-handshake-rejected.json` |
| **T02** initialization & auth | HELLO then LOGON observed with required metadata keys; HELLO SUCCESS requires `server`+`connection_id`; invalid auth fails at a documented boundary; credentials redacted | `t02-initialization.json`, `t02-invalid-credentials.json` |
| **T03** basic read-only query | `RETURN 42 AS answer` returns the integer `42`; RUN query string and PULL captured | `t03-query-scalar.json` |
| **T04** graph value reconstruction | typed `INode`/`IRelationship`; labels, properties, element ids, direction, and distinct identity despite reused numeric id 1 | `t04-graph-values.json` |
| **T05** pagination & cursor lifecycle | finite `PULL(n=1)` drives one PULL per record with `has_more`; `DISCARD` drains; early session disposal is deterministic | `t05-pagination.json`, `t05-discard.json`, `t05-early-disposal.json` |
| **T06** failure & recovery | protocol `FAILURE` surfaces as `Neo4jException`; post-failure `PULL` is `IGNORED`; abrupt termination and truncated frame are errors, not clean EOF | `t06-protocol-failure.json`, `t06-socket-termination.json`, `t06-incomplete-response.json` |
| **T07** transport fragmentation | identical observable result under whole / per-byte / fixed-chunk / coalesced writes; documents that the driver rejects a fragmented handshake response | `t07-fragmentation-*.json`, `t07-handshake-fragmentation.json` |
| **T08** real BoltSharp listener | genuine B2-B+B2-A listener negotiates 5.8 (legacy & manifest) and observes HELLO; first unsupported step is HELLO; boundary classified | `t08-real-listener-legacy.json`, `t08-real-listener-manifest.json` |

### Status reporting

All current tests are **Group A (deterministic fake-server tests)** and are asserted strictly; the
misclassification-prone failure paths (T06) assert that unexpected termination is an error. T08 is the
**Group B (actual BoltSharp interoperability probe)**: it records a reproducible, classified boundary.
No test uses broad exception-swallowing to manufacture a pass, and no `EXPECTED_UNSUPPORTED`/`BLOCKED`
outcome is counted as a passing compatibility test. T08's production-flow steps beyond HELLO are not
claimed to pass; they are recorded as unreached.

---

## 5. Independent protocol oracle

Response bytes are produced by `ProtocolFixtures`, written directly from the published Bolt 5.8 /
PackStream 1.0 specification (marker ranges, message signatures, Node 4-field / Relationship 8-field
structure semantics). It does **not** call `PackStreamWriter` or `BoltMessageWriter`. A shared
serialization mistake therefore cannot make both the fake responder and BoltSharp appear correct.

The driver's independent reconstruction (e.g. `answer=42`, Alice/Bob/KNOWS with correct ids, element
ids, labels, properties and direction) confirms the fixtures are spec-correct on the driver side. The
same fixtures are the source for the accepted B5 graph layout.

---

## 6. Test execution, environment and provenance

Environment: Windows `10.0.26200`, win-x64; .NET SDK `10.0.401`; MSBuild `18.9.11`; target `net8.0`;
runtime `8.0.31`; `Neo4j.Driver` 6.4.0.

| Set | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| `BoltSharp.IntegrationTests` (B0-B, this milestone) | **19** | 0 | 0 |
| Core solution `BoltSharp.slnx` (unchanged baseline) | **826** | 0 | 0 |

Build: **0 warnings, 0 errors**. Overlapping sets are not summed. The core solution is unchanged
because `src/` and `tests/BoltSharp.Tests/` were not modified.

Command and environment receipts, the evidence manifest and SHA-256 hashes are under
`docs/reviews/evidence/B0-B-DRV-INT-01/`. Reproduce with:

```powershell
$env:BOLTSHARP_B0B_EVIDENCE_DIR="$PWD\docs\reviews\evidence\B0-B-DRV-INT-01"
dotnet test tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj -c Debug
```

---

## 7. Discovered interoperability facts

These are recorded for the gap matrix (`docs/reviews/B0-B-DRV-INT-01-gap-matrix.md`):

1. The driver offers Manifest v1 plus concrete ranges (5.0–5.8, 4.2–4.4, 3.0) and selects 5.8.
2. HELLO SUCCESS **must** include `server` and `connection_id`; empty metadata NREs the driver.
3. The driver's handshake reader is **not** tolerant of a split 4-byte handshake response.
4. After a `FAILURE` the driver may pipeline `PULL`; the server must answer `IGNORED` until `RESET`.
5. `DISCARD` is emitted when a partially-fetched result is consumed.

---

## 8. Remaining implementation gaps

Every gap maps to an existing work package (see the gap matrix). **No gap is implemented here:**

- **B3-A** — HELLO/LOGON/RUN/PULL/DISCARD/RESET/GOODBYE/SUCCESS/RECORD/FAILURE/IGNORED schemas.
- **B3-B** — authentication-before-query, FAILED/IGNORED/RESET recovery, GOODBYE.
- **B3-C** — read-only RUN, finite PULL/`has_more`, DISCARD, synthetic Alice/Bob/KNOWS graph.
- **B5-A / B5-B** — durable TCP/TLS transport and the official-driver typed-graph acceptance.

---

## 9. Limitations

- Windows/net8.0 only; plaintext loopback only; TLS (`bolt+s://`) not exercised.
- Synthetic controlled traffic; not a real outage, server, or Neo4j instance.
- Bolt 5.8 / PackStream 1.0 selection remains **provisional** (`docs/compatibility-target.md`).
- Author independence UNVERIFIED; the producer does not self-qualify.
- The fake server decodes client requests with BoltSharp's reader; only responses use the independent
  oracle. Request-side decoding is therefore not an independent check of BoltSharp's reader, which is
  already covered by the accepted B1/B2 suites.

---

## 10. Stop conditions honored

No production code change; no B3/B5 implementation; no compatibility claim; no new branch or linked
worktree; no reset/rebase/push; no modification of historical evidence or accepted tests; no
public listener. Valid work was committed at a stable point if a stop condition had been reached.

---

## 11. Handoff

**B0-B-DRV-INT-01: IMPLEMENTED, READY FOR INDEPENDENT QUALIFICATION.**

Deliverables: the `BoltSharp.IntegrationTests` project and executable scenarios; the programmable fake
Bolt server, script API and independent `ProtocolFixtures` oracle; real-driver handshake/initialization,
scalar and graph-value evidence; real-listener comparison probe; the gap matrix; and this record with
its evidence manifest.

The independent reviewer (B0-B-Q1) must validate the fixtures against the specification, challenge the
tests for false success, confirm the fake server does not mask BoltSharp defects, reproduce at least
one real-driver handshake and one controlled-response scenario, and check cleanup, segmentation,
failure classification, evidence provenance and historical preservation. The producer does not declare
independent qualification.
