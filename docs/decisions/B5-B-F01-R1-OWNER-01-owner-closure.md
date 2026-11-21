# B5-B-F01-R1-OWNER-01 — Formal Owner Closure of Unsupported-Command Refusal Remediation

## 1. Decision metadata

| Field | Value |
| --- | --- |
| Task | `B5-B-F01-R1-OWNER-01` |
| Role | Owner-decision recording agent (documentation-only closure) |
| Milestone | B5-B-F01-R1 — restore unsupported-command refusal across the transport/session boundary |
| Protocol target | Bolt 5.8 / PackStream 1.0 (bounded, unchanged) |
| Official client | `Neo4j.Driver` 6.4.0 (exact pin, test-only) |
| Target qualification | `Q-B5B-03` |
| Proposed disposition verified | **B5-B-F01-R1 — ACCEPTED WITH QUALIFICATIONS — CLOSED** |
| Decision date | 2026-11-11 |
| Starting HEAD | `79f7965a0e8e351baf2dd388218290804c31389a` |
| Starting tree | `80bc7b4f3ec18341251ea4e04195d051489dacb5` |
| Next engineering work | Subject to separate authorization (see §26) |

This record is an owner decision. It contains **no implementation**, modifies no production, test,
probe, report, evidence, decision or historical file, and claims no capability beyond the bounded
scope in §24, restricted by §25.

## 2. Starting state verification and preflight

Read from the canonical checkout before this decision was written:

```text
git branch --show-current  -> main
git rev-parse HEAD         -> 79f7965a0e8e351baf2dd388218290804c31389a (exact match)
git rev-parse HEAD^{tree}  -> 80bc7b4f3ec18341251ea4e04195d051489dacb5 (from Git; matches the
                              Q1 provenance chain recorded in the reviewer report §23.1)
git worktree list          -> exactly one worktree (D:/projects/bolt-sharp)
git status --porcelain     -> clean (no concurrent writer)
git stash list             -> empty
prior F01-R1-OWNER-01      -> none existed (docs/decisions/ contained no such decision)
push state                 -> see the recorded deviation below
```

All preflight gates passed: branch `main`; exact HEAD and tree; one canonical worktree; clean tree
and index; no stash; no concurrent writer; no prior closure decision; all five existing owner
decisions present; F01 implementation (`1e3c8f89`) and producer evidence (`4cebc7cc`) committed;
F01-R1-Q1 reviewer artifacts committed (`c3052db9`, `5c9fc5da`); B5-B owner acceptance
(`6b2a0e3`) and the F01 baseline (`3953bdd`) are ancestors of HEAD (verified with
`git merge-base --is-ancestor`).

**Push-state deviation (investigated and explained).** The commission's precondition expected
"63/66 commits ahead of origin/main, nothing pushed". Actual: after `git fetch origin`,
`origin/main` equals `main` (0 ahead, 0 behind). Cause: in the immediately preceding turn the
operator explicitly directed and performed a safe fast-forward push
(`9abf5e3..79f7965 main -> main`) of the completed work. HEAD/tree are unchanged by that push, the
closure decision did not previously exist, and nothing is duplicated; the deviation is therefore
recorded rather than treated as a stop condition. Per this commission's §27, **this decision
commit is not pushed.**

## 3. Acceptance authority and scope

This document is the formal F01-R1 owner closure. The producer implementation report
(`docs/reviews/B5-B-F01-R1-implementation.md`), the producer evidence
(`docs/reviews/evidence/B5-B-F01-R1/`) and the independent qualification
(`docs/reviews/B5-B-F01-R1-Q1-independent-qualification.md`, with its probes and evidence) are
**evidence inputs**; this decision is the **closure authority**. The accepted scope is exactly §24
restricted by §25. Nothing here authorizes remediation, F02 refinement, B4/B6 work, package
publication or repository push.

## 4. Evidence reviewed

Read in full or verified directly from committed bytes and fresh execution during this decision:

- `docs/reviews/B5-B-F01-R1-implementation.md`; `docs/reviews/evidence/B5-B-F01-R1/`
  (70-entry manifest, pre/post reproduction captures, probe source, driver scenarios, TRX, logs).
- `docs/reviews/B5-B-F01-R1-Q1-independent-qualification.md` (23 sections, including §20 findings
  `Q1-F01`–`Q1-F03` and §21 remaining qualifications);
  `docs/reviews/probes/B5-B-F01-R1-Q1/` (47-probe project, 19 tracked files);
  `docs/reviews/evidence/B5-B-F01-R1-Q1/` (31-entry manifest, TRX, logs).
- The five accepted owner decisions (B3-A, B3-B, B3-C, B5-A, B5-B) — byte-identity verified (§7).
- The actual F01 production diff (`1e3c8f89`) and the focused tests, inspected directly; the
  changed method `RunMessageLoopAsync` and the diagnostic peek `DescribePayloadSignature` read
  from source at HEAD.
- Fresh read-only re-execution of the regression suites (§8) plus repeated-run stability analysis
  that produced two new owner findings (§8.3).

## 5. Qualification chronology

| Stage | Disposition | Recorded revision |
| --- | --- | --- |
| B3-A / B3-B / B3-C | ACCEPTED WITH QUALIFICATIONS — CLOSED | owner acceptances `c2881ea5…`, `aae3d0e9…`, `85358065…` |
| B5-A | ACCEPTED WITH QUALIFICATIONS — CLOSED | `2bebd3ee77b12c190b2e48b763deb4444cc94e5d` (tree `346611f5…`) |
| B5-B | ACCEPTED WITH QUALIFICATIONS — CLOSED | `6b2a0e36654cd209bfd8a88a39bd9d58f5097aa6` (tree `76133ab…`) |
| B5-B-F01-R1 | IMPLEMENTED, READY FOR INDEPENDENT QUALIFICATION | `1e3c8f89f1bab38903001dc25edc96b7a7adbc01` (tree `b2219ee19e76031e45c5a4e95dc4aa415bfc2c05`; parent `3953bdd…`) |
| B5-B-F01-R1 producer evidence | evidence recorded | `4cebc7cc40785f8cf502aeb25be74e39d1883670` (tree `947d8826eb53ba70eb467c055cce852ea872a380`; parent `1e3c8f89…`) |
| B5-B-F01-R1-Q1 | SUPPORTED WITH QUALIFICATIONS | `c3052db9e29d826709965c155b754f269e5f0cf8` (tree `95a98682274fa755a61a661fb8bc6a339e38ad24`; parent `4cebc7cc…`) |
| Q1 reviewer evidence addendum | part of the reviewed chain | `5c9fc5da5f9966f778c5b4ea3e569d9f6ad52f8c` (tree `04079b8b2f7d433e799d69e147e37124585b180b`; parent `c3052db9…`) |
| Operator reviewer-provenance addendum | provenance only; no reviewer finding changed | `79f7965a0e8e351baf2dd388218290804c31389a` (tree `80bc7b4f3ec18341251ea4e04195d051489dacb5`; +18 lines, single report file) |
| **B5-B-F01-R1-OWNER-01** | **ACCEPTED WITH QUALIFICATIONS — CLOSED** (this decision) | decision commit recorded in the final handoff |

All revisions and trees were read directly from Git. The provenance addendum appends an
operator-recorded session-provenance subsection (§23.1 of the Q1 report) and does not modify any
reviewer finding, evidence or disposition; its diff is a single file with 18 added lines.

## 6. Reviewer independence acceptance

Recorded facts (Q1 report §1 and §23.1):

- **Producer:** DeepSeek V4.1 Flash, provider NVIDIA, OpenCode; producer session (implementation
  report binds the note).
- **Reviewer:** GLM 5.3 Flash (`nvidia/z-ai/glm-5.3-flash`), provider NVIDIA, in a **dedicated new
  child OpenCode session** `ses_e3957b5b2ffei7mSUdvmcu2Dk9` (operator-recorded, corroborated),
  fresh context; a prior spawn attempt (`zai/glm-5.3`) failed before any review work began.
- The reviewer independently authored all probes and oracles (own PackStream encoder, own raw
  Bolt client, recording proxy, reviewer-owned providers, direct `ReceiveBytes` entry point),
  performed a **fresh runtime reproduction** of the original F01 defect from an isolated
  `git archive` of `3953bdd` (canonical tree untouched), and did not derive expectations from the
  modified transport. The operator's post-review artifact verification was **not** represented as
  a second independent review.

**Owner conclusion: the required separate execution-session and different-model F01-R1-Q1
independence gate was satisfied.** Also recorded: same operator, host and repository; **no
organizational independence is claimed**, and no independence waiver was applied or needed.

## 7. Evidence integrity

Independently re-verified for this decision (raw-byte SHA-256 over working-tree bytes, read-only;
no evidence file was written, and no evidence artifact was created):

| Evidence package | Expected | Verified |
| --- | ---: | ---: |
| F01-R1-Q1 reviewer | 31 | **31/31** |
| F01-R1 producer | 70 | **70/70** |
| B5-B-Q1 reviewer | 76 | **76/76** |
| B5-B producer | 62 | **62/62** |
| B5-A-Q1 | 10 | **10/10** |
| B5-A producer | 18 | **18/18** |
| Eleven earlier historical packages | 352 | **352/352** |
| **Total (17 manifests)** | **619** | **619/619, zero mismatches** |

Additional integrity results:

- All five existing owner decisions remain byte-identical to their committed blobs
  (B3-A `ab84ffa9…`, B3-B `211a5d56…`, B3-C `323303bd…`, B5-A `385c820e…`, B5-B `7d23b6bb…`).
- **No historical manifest references the F01-modified source.** The only historically hashed
  `src/` paths are `BoltSharp/Messages/BoltMessageCodec.cs` and `BoltRequests.cs`
  (B3-A-R1-IMPL-01, verified 13/13 above and unchanged). The B3-B implementation-source manifests
  describe their original revisions and remain outside the verified aggregate by design. A
  repository-wide search found zero manifest references to `BoltTcpConnection`. The commission's
  assertion is therefore independently confirmed; no historical artifact was rewritten.
- **Credential hygiene:** committed evidence was inspected for accidental disclosure (plain,
  hex-encoded and UTF-16-hex-encoded sentinel scans across the F01-R1 and Q1 evidence trees:
  zero findings; no private-key blocks anywhere). No credential value is reproduced in this
  decision.

## 8. Regression results

### 8.1 Freshly executed during this decision (read-only; evidence env vars cleared; tree stayed clean)

| Suite | Command (canonical form) | Fresh result |
| --- | --- | --- |
| Production build | `dotnet build src/BoltSharp/BoltSharp.csproj -c Debug --no-incremental --nodeReuse:false` | Build succeeded, **0 warnings, 0 errors** |
| Solution build | `dotnet build BoltSharp.slnx -c Debug --no-incremental --nodeReuse:false` | **0 errors; exactly 2 warnings** — the historical `xUnit2012` advisories |
| Core full suite (33 runs) | `dotnet test tests/BoltSharp.Tests/… -c Debug --no-build` | **28 runs at 1,227/1,227**; 5 runs with one failure each (§8.3) |
| B5-A transport filter | `--filter 'FullyQualifiedName~BoltSharp.Tests.Transport'` | **90/90** |
| B3-C backend filter | `--filter 'FullyQualifiedName~BoltSharp.Tests.Backend'` | **94/94** |
| B3-B sessions filter | `--filter 'FullyQualifiedName~BoltSharp.Tests.Sessions'` | **119/119** |
| B3-A messages filter | `--filter 'FullyQualifiedName~BoltSharp.Tests.Messages'` | **98/98** |
| B5-B official-driver suite | `dotnet test tests/BoltSharp.DriverInteropTests/… -c Debug --no-build` | **41/41** |
| B0-B driver integration | `dotnet test tests/BoltSharp.IntegrationTests/… -c Debug --no-build` | **19/19** |

### 8.2 Verified from committed evidence (not re-executed here)

| Record | Result |
| --- | --- |
| F01-R1-Q1 reviewer probes TRX + logs | **47/47** (reviewer-reported, stable across fresh processes) |
| F01-R1 producer TRX (`f01r1-full-regression`, `f01r1-transport`, `f01r1-driver-suite`) | **1,227 / 90 / 41**, all passing |
| Q1 reviewer TRX (core ×3, transport, driver, B0-B) | passing at the counts above |

### 8.3 New owner findings from repeated-run stability analysis (records required by this commission)

The commission requires accurate reporting. Fresh repetition exposed two **test-level** findings.
Neither is a production defect; both are recorded openly and **no test was modified** (not
authorized by this commission).

- **B5-B-F01-R1-OWNER-F01 — F01-R1 producer pipelined tests are client-delivery-race sensitive.**
  `UnsupportedCommandRefusalTests.PipelinedUnsupportedCommand_NothingAfterTheTerminalFailureExecutes`
  and `…PipelinedMalformedMessage_NothingAfterTheTerminalFailureExecutes` read the terminal FAILURE
  immediately after a single pipelined write (`Assert.IsType<FailureResponse>(client.ReadResponse())`,
  test lines 217 / 246). When unread pipelined tail bytes remain in the server's receive buffer,
  the accepted terminal close is an OS **abortive close (RST)** — the phenomenon retained as
  `B5-B-F01-R1-Q1-F01` — and the RST can suppress client-side delivery of the in-flight FAILURE,
  so the read throws `IOException`/`SocketException` instead of yielding the FAILURE. Observed:
  **4 of 33 fresh full-suite runs** (2× malformed-pipelined, 1× unsupported-pipelined, plus one
  first-run failure of the same class) and **16 of 20 runs** of the filtered pipelined pair
  (isolation timing favors the RST; `PipelinedUnsupportedCommand` at ~2 ms). Server-side
  behavior remains per contract in every failing run (FAILURE generated/written; session Defunct;
  no tail execution — the test's later assertions are never reached because the read throws
  first). The non-pipelined refusal tests, the direct-session oracle, the official-driver BEGIN
  and ROUTE scenarios, and the reviewer's 47 probes are **deterministic and green**. Root cause
  is exactly `Q1-F01`+`Q1-F02` (this flake is direct corroboration of Q1-F02's clarification that
  abortive close can also suppress delivery of the FAILURE itself). Classification: **test
  robustness defect, non-blocking for the production acceptance**; smallest remedy at the next
  separately authorized test touch: make the two pipelined tests race-tolerant (assert
  terminality, no-tail-execution and accounting deterministically; treat the FAILURE read as
  delivered-if-read under the retained abortive-close race, keeping byte-exact FAILURE assertions
  in the deterministic non-pipelined tests). A production graceful-close/drain hardening remains a
  separate, optional release-hardening consideration under §26 — not authorized here.
- **B5-B-F01-R1-OWNER-F02 — pre-existing B5-A-era test race (unrelated to F01).**
  `MultiClientTests.FailureInOneClient_IsolatedFromOthers` (B5-A-era file, untouched by the F01
  commits — verified) flaked once in 33 runs: `Assert.Equal(1, server.Diagnostics.ConnectionsFailed)`
  observed `0` because the bad client's framing-garbage connection had not finished failure
  accounting when the assertion ran (line 78). Pre-existing test timing race; recorded for the
  test-stability backlog; no runtime impact and no relation to the F01 remediation.

No failure in any run was attributable to production behavior. All five anomalous runs are fully
explained by the two findings above.

## 9. Original F01 defect

Independently reproduced by the reviewer from an isolated `git archive` source copy of
`3953bdd9962a8e4dc4c43c0bb3996b5fed76fb08` (canonical tree unaltered), and corroborated by the
producer's committed pre-remediation capture:

1. The official driver / raw client sent valid Bolt BEGIN (`B1 11 A0`) — and COMMIT/ROLLBACK/ROUTE.
2. The transport received the complete message and attempted decoding **before** session
   processing (`codec.DecodeRequest`), hitting `codec:UnsupportedMessage`.
3. The connection closed with **no B3-B FAILURE response**.
4. The official driver observed `ServiceUnavailableException` instead of a protocol-level refusal.
5. The reference provider was never consulted.

**Owner conclusion: the original F01 inconsistency was independently reproduced — a verified
transport/session contract bypass, not a documentation interpretation.**

## 10. Implemented correction

Verified from the diff (`3953bdd..1e3c8f89`): the only modified production file is
`src/BoltSharp/Transport/BoltTcpConnection.cs` (+22/−7 net within a 29-line change), and the
transport now passes the complete payload to `BoltConnectionSession.ReceiveBytes`.

- Session-owned: structural validation, unsupported-message classification, malformed-message
  classification, state transitions, FAILURE generation, terminal disposition.
- Transport-retained: network framing, connection lifecycle, response writing, diagnostics,
  cleanup. One authoritative session-processing path; no second parser; no buffer-ownership
  change; no new dependency; no change to B3-A schemas, B3-B transitions, B3-C backend, B5-A
  configuration, `BoltSharp.slnx`, or the solution structure.

**Owner finding: the correction restores the existing authoritative session path; the diagnostic
signature peek (`DescribePayloadSignature`) is a bounded two-byte label used only in the
`MessageProcessed` event and is not part of authoritative message classification.**

## 11. Unsupported-command refusal acceptance

Independently verified by the reviewer for BEGIN, COMMIT, ROLLBACK and ROUTE over actual TCP:

- A single terminal **FAILURE** with `gql_status = 08N06`,
  `neo4j_code = Neo.ClientError.Request.Invalid` and the description
  `"Explicit transactions and routing are not supported by this server."`
- The session becomes **DEFUNCT** and closes; the provider is never consulted; no transaction or
  routing operation executes; no RECORD or SUCCESS is fabricated.
- **Three-way byte oracle agreed**: actual socket response == reviewer-derived independent
  PackStream encoding == direct accepted `ReceiveBytes` session response, for the verified
  command cases and the protocol-violation variant.

**Owner disposition: ACCEPT the restored unsupported-command refusal behavior through the actual
TCP transport. This accepts correct refusal; it does not authorize implementation of BEGIN,
COMMIT, ROLLBACK or ROUTE.**

## 12. Official-driver interoperability after correction

Verified (reviewer evidence; producer driver suite; fresh owner re-run 41/41):

- **BEGIN:** the official `Neo4j.Driver` 6.4.0 observes `ProtocolException` (a `Neo4jException`
  subtype), `Code = Neo.ClientError.Request.Invalid`, `GqlStatus = 08N06`, `IsRetriable = false`;
  the server transmits the accepted FAILURE before closing; no transaction begins; a subsequent
  supported query succeeds on a **distinct physical connection** (new-connection recovery, not
  same-session recovery).
- **ROUTE:** a real `neo4j://` attempt actually emitted ROUTE and received the accepted refusal
  (reviewer-observed). This is refusal coverage only — **not** routing support.
- **COMMIT/ROLLBACK:** raw-TCP and direct-session probes verified byte-identical refusals; the
  official-driver flow cannot reach them because BEGIN is refused first — the live-driver
  reachability limitation is preserved explicitly.

**Owner conclusion: ACCEPT official-driver protocol-level refusal for the verified BEGIN and
ROUTE paths, with the COMMIT/ROLLBACK live-driver reachability limitation retained.**

## 13. Malformed-input behavior

The remediation changed certain malformed-message network behavior from silent closure to the
accepted B3-B **terminal protocol-violation FAILURE** followed by closure. The reviewer executed
ten malformed-input probes (wrong arity, unknown signature, wrong field type, truncated
structure, malformed nested value, invalid metadata, invalid marker, invalid protocol tag,
one-byte payload): each received the terminal protocol-violation FAILURE with no recoverable
path, no provider operation and no post-terminal execution. Framing-level failures (truncated
chunk header/body, oversize, premature EOF) remain silent fail-closed per the accepted B2
contract.

**Owner disposition: ACCEPT the observed malformed-message handling as consistent with the
inherited B3-B `ReceiveBytes` contract** (which predates the fix and is the accepted
classification owner). The decision explicitly records that the wire-visible refusal shape
changed for certain invalid inputs (silent close → terminal FAILURE); it does **not** claim every
network byte sequence is unchanged. Malformed input never acquires a recoverable
unsupported-command path, and all framing-level refusal and resource-bound guarantees are
preserved.

## 14. Pipelining and terminality

Verified by the reviewer and by fresh owner runs (non-racy cases): a terminal FAILURE is emitted
exactly once; already-pipelined requests do not execute after the terminal outcome; RESET cannot
revive a DEFUNCT connection; no fabricated RECORD/SUCCESS follows a terminal unsupported-command
failure; no additional provider operation opens. The server-side accounting is exact
(`MessagesProcessed` counts the refused message; `ConnectionsFailed == 0` — terminal refusal is
not a transport fault). Where the platform abortive close races the client's read of the
terminal FAILURE (unread pipelined input), the retained abortive-close qualification and owner
finding F01 (§8.3, §20) apply.

**Owner conclusion: ACCEPT preservation of terminal session semantics.** No full RESET
jump-ahead or general cancellation functionality is newly claimed.

## 15. Diagnostic signature inspection

Reviewed specifically (`DescribePayloadSignature`): bounded two-byte read with length guard;
cannot throw on short/malformed payloads (`sig=unknown`); no nested PackStream decoding; no
allocation beyond the diagnostic string; no mutation or ownership change; runs after responses
are written; consultable only for event detail. The reviewer's response-equivalence control
confirms no influence on execution or closure; event accounting is exactly-once per message with
correct signature attribution.

**Owner disposition: ACCEPT the diagnostic adjustment as non-authoritative and
behavior-preserving within the tested scope.** The mechanism remains subordinate to B3-B message
processing; no new public diagnostic API commitment is established.

## 16. Supported functionality preserved

Fresh owner runs plus reviewer evidence confirm: conventional and Manifest v1 handshakes,
pipelined HELLO, chunk framing, fragmentation, NOOP handling, admission and TLS modes within
tested scope; HELLO/LOGON, credential validation, missing-verifier fail-closed, LOGOFF, terminal
state handling; `RETURN 42 AS answer` (Int64 42, golden bytes `B171912A`/`B170A0`), the typed
Alice/KNOWS/Bob graph record with correct element IDs/direction/`since=2024`, and typed
parameters with correct exhaustion, retirement and pooled connection reuse.

**Owner conclusion: ACCEPT that the F01 correction preserves the previously accepted B5-B
interoperability surface within the tested scope.**

## 17. F02 explicitly unchanged

`Q-B5B-04` remains exactly as accepted in the B5-B owner decision: unsupported B3-C queries
return the generic `50N42`/`Neo.ClientError.Request.Invalid`; the driver treats the refusal as
non-retriable and may discard the physical connection; recovery uses a fresh connection; no
duplicate execution; graph unchanged. The reviewer verified F01 did not alter this behavior, and
fresh runs of the F02 scenarios are green.

**Formal disposition: `Q-B5B-04` — REMAINS OPEN / ACCEPTED QUALIFICATION** (authoritative wording
retained). F02 refinement remains optional and separately authorized; it is not closed through
F01.

## 18. Live-driver IGNORED

Preserved and re-verified (reviewer; fresh driver suite 41/41): the driver pipelines RUN and
PULL; the refused RUN receives FAILURE (`50N42`); the pipelined PULL receives the zero-field
IGNORED with wire encoding **`B0 7E`**; the driver consumes the sequence without
desynchronization. The established B3-A qualification closure for encoding and driver consumption
remains valid.

**Formal disposition: `Q-B5B-05` — RETAINED.** Same-connection RESET recovery under the current
generic classification remains separately qualified; recovery on a new physical connection is not
recovery of the original connection.

## 19. Fail-closed resource ownership

Independently injected retirement failure (reviewer-owned fault provider through the public
`OperationProviderFactory` seam; official-driver and raw paths): exactly one operation opened;
exactly one disposal attempt; terminal FAILURE (`50N00` in the observed failure path) instead of
SUCCESS; session DEFUNCT; no RESET recovery; no second operation; no disposal retry; correct
connection closure; healthy new connections unaffected (per-connection identity tracked).

**Owner conclusion: ACCEPT continued enforcement of the B3-B failed-retirement ownership
contract after F01 remediation.** A failed disposal attempt does not prove successful resource
release; the accepted guarantee is failure containment and refusal to execute further operations
on the affected session. All inherited B3-B ownership qualifications remain binding.

## 20. Abortive-close observation (retained) and owner findings

**Established:** the accepted FAILURE is generated and written through the transport path; the
session becomes DEFUNCT; no additional command executes.
**Not established:** guaranteed client-visible delivery under every abortive-close condition;
fully qualified shutdown behavior with unread pipelined input; production-grade TCP drain/close
guarantees.

This decision additionally records the freshly measured corroboration from §8.3: the abortive
close can suppress client-side delivery of the in-flight FAILURE itself when unread pipelined
tail bytes are present (owner finding `B5-B-F01-R1-OWNER-F01`, ~16/20 filtered and 4/33
full-suite on loopback Windows), and a pre-existing B5-A-era counter race
(`B5-B-F01-R1-OWNER-F02`, 1/33, unrelated to F01). Neither is a production defect and neither
invalidates the accepted remediation.

**Formal disposition: NON-BLOCKING RELEASE-HARDENING QUALIFICATION — RETAINED**, recorded against
`Q-B5B-09` whose authoritative scope ("release-grade hardening pending … cancellation/shutdown
boundaries") supports it without redefinition, and cross-referenced to reviewer findings
`B5-B-F01-R1-Q1-F01`/`F02`. A bounded test-robustness fix for owner finding F01 and the
pre-existing F02 race is recommended at the next separately authorized test touch; no production
remediation is authorized without a reproducible production defect and a separate scope.

## 21. Reviewer documentation findings

Exact definitions read from the committed Q1 report (§20):

- **`B5-B-F01-R1-Q1-F02` (DOCUMENTATION):** the producer report's §15 wording ("tolerate the
  platform's abortive (RST) close when pipelined input remains in the socket buffer") can be read
  as covering only the post-FAILURE read; the abortive close can also suppress client-side
  delivery of the FAILURE itself under specific timing. No runtime impact; a one-sentence
  clarification was suggested for the next document touch.
- **`B5-B-F01-R1-Q1-F03` (DOCUMENTATION):** the malformed-input behavior change (silent close →
  terminal protocol-violation FAILURE) is verified to match the accepted B3-B `ReceiveBytes`
  contract; the reviewer confirms no accepted contract requires a different disposition.

Owner disposition: both are **ACCEPTED DOCUMENTATION OBLIGATIONS — NON-BLOCKING**. This decision
incorporates the clarification directly: see §13, §14 and §20, which state explicitly that the
abortive close can suppress delivery of the terminal FAILURE and that the malformed-input
refusal shape changed. No historical report or accepted decision was edited; no separate
documentation-cleanup commit is authorized.

## 22. Q-B5B-03 formal closure

**Target:** `Q-B5B-03 — Unsupported-command transport closure (F01)` (authoritative wording in
the B5-B owner decision §23.2).

**Closure evidence:**

1. Independent reproduction of the original defect (isolated `git archive` of `3953bdd`; fresh
   runtime, canonical tree untouched).
2. Single-file production correction (`BoltTcpConnection.cs`), session-owned classification.
3. Direct B3-B refusal oracle (`ReceiveBytes`) for BEGIN/COMMIT/ROLLBACK/ROUTE.
4. Independent PackStream response oracle and three-way byte agreement.
5. Real TCP FAILURE transmission verified from the socket before closure.
6. Official-driver BEGIN refusal (`ProtocolException`/`08N06`/`Request.Invalid`/non-retriable)
   and live ROUTE refusal.
7. Valid-but-unsupported classification distinguishes malformed input (terminal
   protocol-violation classification), verified by ten probes.
8. No execution after terminal refusal; pipelined tails never processed; RESET cannot revive.
9. F02 non-regression (`Q-B5B-04` untouched).
10. Supported scalar/graph/parameter/interop non-regression.
11. Fail-closed retirement independently re-verified.
12. Independent review with session/model separation (§6).
13. Fresh owner regressions with fully characterized stability findings (§8).
14. Evidence integrity 619/619; five owner decisions byte-identical; no manifest references the
    changed source.

**Owner disposition: `Q-B5B-03` — CLOSED BY VERIFIED REMEDIATION.**

**Scope of closure:** accepted unsupported-command refusal restored for BEGIN, COMMIT, ROLLBACK
and ROUTE via direct-session and raw-TCP evidence; official-driver BEGIN and ROUTE refusal
independently verified; terminal state and no-execution guarantees preserved; malformed input
remains terminal; one authoritative session-processing path restored.

**Closure non-claims:** this does not mean transactions or routing are implemented; all
unsupported Bolt signatures have been exhaustively qualified; every malformed network input is
fully fuzz-qualified; every TCP close guarantees final-response delivery (see §20); F02 is
resolved; same-connection RESET recovery is resolved; or full Bolt 5.8 conformance is
established. The prior B5-B owner decision is not rewritten; this later decision records the
closure.

## 23. Remaining binding qualifications

**B5-B (authoritative wording retained):** `Q-B5B-01` official-driver version/scope; `Q-B5B-02`
limited query support; `Q-B5B-04` generic refusal/error classification (F02); `Q-B5B-05`
same-connection recovery after IGNORED not established; `Q-B5B-06` TLS trust-arrangement
limitations; `Q-B5B-07` no general transactions/routing/multi-database; `Q-B5B-08` no
cross-driver production interoperability claim; `Q-B5B-09` release-grade hardening pending
(now additionally carrying the abortive-close observation, §20). `Q-B5B-03` is closed only
within the §22 scope.

**B5-A (retained):** `Q-B5A-06` blocked-write backpressure (open); `Q-B5A-07` session-options
passthrough limitation; `Q-B5A-02`/`Q-B5A-03` TLS policy depth; `Q-B5A-05` slow-provider
responsiveness; `Q-B5A-10` shutdown edge qualifications; `Q-B5A-09` no performance claims; plus
`Q-B5A-01` continuing separate-executor requirement for release-grade work (satisfied for
F01-R1-Q1 per §6).

**B3-B (retained):** single-owner session execution; permanent DEFUNCT after failed retirement
and exactly-once disposal; bounded incremental PULL; no full RESET jump-ahead; `ReceiveBatch`
ownership; provisional public APIs; no general cancellation API; diagnostic semantics; and all
other accepted B3-B qualifications.

**B3-C (retained):** exact-form read-only query allowlist; finite reference graph; supported
parameter surface; no general Cypher; no writes or general transactions.

No additional qualification is discharged merely because F01 has been corrected.

## 24. Explicit accepted scope

Within the evidence scope cited in each section, the F01 remediation is accepted for exactly:
one authoritative session-message processing path; correct classification of valid unsupported
commands; correct terminal FAILURE responses with `08N06` and `Neo.ClientError.Request.Invalid`;
actual TCP response transmission in the verified scenarios; official-driver BEGIN refusal;
official-driver ROUTE refusal (observed); raw COMMIT/ROLLBACK refusal; malformed-message
terminality under the accepted B3-B classification; no unsupported-command execution; no session
recovery after terminal refusal; no later pipelined execution; safe non-authoritative diagnostic
signature inspection; correct message-event accounting; preserved scalar/graph/parameter
behavior; preserved live-driver IGNORED; preserved F02 semantics; preserved authentication;
preserved fail-closed resource ownership; regression stability as qualified in §8.3; independent
qualification; and historical evidence integrity.

**This is a bounded remediation acceptance — not a new BoltSharp feature milestone.**

## 25. Explicit non-claims

This decision does not imply: general transaction support; ROUTE support; cluster support;
multi-database behavior; full Cypher execution; general graph mutation; complete Bolt 5.8
conformance; universal driver compatibility; production-grade network shutdown; guaranteed
final-response delivery under every TCP close; resolved blocked-write backpressure; resolved
generic error classification (F02); same-connection RESET recovery after the observed driver
refusal; complete TLS deployment hardening; a stable public API; production readiness; or NuGet
release readiness. The existing bounded B5-B scope is retained.

## 26. Release-hardening obligations and recommended next work (none authorized here)

- **Owner finding F01 (test robustness, recommended):** make the two pipelined F01 tests
  race-tolerant (deterministic terminality/no-execution/accounting assertions; FAILURE-read
  conditional under the retained abortive-close race). Small, separately authorized test-only
  change with a focused re-run.
- **Owner finding F02 (pre-existing):** `MultiClientTests` counter race — test-stability backlog;
  fix at the next authorized test touch.
- **Production graceful-close/drain hardening** (optional; only if release-grade work concludes
  the retained abortive-close delivery limitation matters for real clients): investigate
  graceful-versus-abortive close, unread pipelined bytes, bounded flush/drain, and healthy-client
  isolation — as part of `Q-B5B-09` release hardening, **not** as an F01 continuation.
- **Remaining roadmap unchanged:** F02 refinement (optional, `Q-B5B-04`); B4 Browser/WebSocket;
  B6 hardening/packaging/release qualification; blocked-write backpressure (`Q-B5A-06`); TLS
  policy; public API stability; security review; performance budgets. None is authorized by this
  decision.

## 27. Formal owner disposition

**B5-B-F01-R1 — ACCEPTED WITH QUALIFICATIONS — CLOSED.**

**`Q-B5B-03` — CLOSED BY VERIFIED REMEDIATION.**

Recorded on the basis of:

1. The original F01 defect independently reproduced (fresh runtime, isolated archive).
2. Root cause verified (transport pre-decode bypassing the session entry point).
3. Single production-file correction accepted; one authoritative session-processing path.
4. Correct B3-B refusal restored through TCP; byte-verified three ways; transmitted before
   closure.
5. Correct official-driver BEGIN and ROUTE behavior; COMMIT/ROLLBACK raw-protocol qualification
   with the live-driver reachability limit recorded.
6. Malformed messages remain terminal; no recoverable path introduced.
7. No unsupported execution; no terminal-session revival.
8. Supported functionality preserved; F02 unchanged; live-driver IGNORED preserved; fail-closed
   resource ownership preserved.
9. Independent review requirement satisfied at session/model level (§6); no organizational
   independence claimed.
10. Regressions passing with two newly recorded, fully characterized test-level findings
    (§8.3) — no production-behavior failure in any run.
11. Evidence integrity verified (619/619; five owner decisions byte-identical; no manifest
    references the changed source; credential scans clean).
12. The OS abortive-close observation retained as a non-blocking release-hardening qualification
    (§20); reviewer documentation findings accepted and their clarification incorporated (§21).
13. All inherited qualifications retained (§23); explicit non-claims recorded (§25); no release
    certification implied.

## 28. Commit and verification record

- **Decision document:** `docs/decisions/B5-B-F01-R1-OWNER-01-owner-closure.md` (this file; the
  only file authorized and committed by B5-B-F01-R1-OWNER-01).
- **Parent commit:** `79f7965a0e8e351baf2dd388218290804c31389a`
  (tree `80bc7b4f3ec18341251ea4e04195d051489dacb5`).
- **Commit message:** `docs(boltsharp): close B5-B F01 refusal remediation with qualifications`.
- **Decision commit/tree:** recorded in the final handoff (verifiable via
  `git log --format='%H %T' -1` after the commit).
- **Post-commit verification performed per commission §28** (read-back from the committed blob):
  F01-R1 closure and explicit `Q-B5B-03` closure; scope-limited closure; BEGIN/ROUTE driver
  evidence accurately described; COMMIT/ROLLBACK limits explicit; malformed-input terminality;
  no unsupported capability claimed; F02 unchanged; `Q-B5B-05` open; abortive-close retained;
  `Q-B5B-09` referenced consistently; inherited qualifications binding; blocked-write
  backpressure open; reviewer documentation findings accurate; historical decisions
  byte-identical; exactly one new file committed; no production/test modifications; `main`; one
  worktree; clean; **no push**. Results recorded in the final handoff.

