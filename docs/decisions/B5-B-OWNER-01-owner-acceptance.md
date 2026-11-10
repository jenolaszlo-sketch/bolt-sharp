# B5-B-OWNER-01 — Formal Owner Acceptance of Official Neo4j .NET Driver Interoperability

## 1. Decision metadata

| Field | Value |
| --- | --- |
| Task | `B5-B-OWNER-01` |
| Role | Owner-decision recording agent (documentation-only acceptance) |
| Milestone | B5-B — official Neo4j .NET driver interoperability |
| Protocol target | Bolt 5.8 / PackStream 1.0 (bounded) |
| Official client | `Neo4j.Driver` 6.4.0 (exact pin; test-only) |
| Proposed disposition verified | **B5-B — ACCEPTED WITH QUALIFICATIONS — CLOSED** |
| Decision date | 2026-11-10 |
| Starting HEAD | `6b2a0e36654cd209bfd8a88a39bd9d58f5097aa6` |
| Starting tree | `76133abce2ac3e9c2e7871b87528371a46ed3109` |
| Next candidate | B5-B-F01 bounded compatibility remediation (separately authorized; not started) |

This record is an owner decision. It contains **no implementation**, modifies no production, test,
probe, report, evidence, decision or historical file, and makes no new compatibility claim beyond
the bounded scope in §24. It records acceptance only for that scope, restricted by the non-claims
in §25, and references — rather than replaces — the producer report, the independent reviewer
qualification and their committed evidence.

## 2. Starting state verification and preflight

Read from the canonical checkout before this decision was written:

```text
git branch --show-current  -> main
git rev-parse HEAD         -> 6b2a0e36654cd209bfd8a88a39bd9d58f5097aa6 (exact match)
git rev-parse HEAD^{tree}  -> 76133abce2ac3e9c2e7871b87528371a46ed3109 (exact match)
git worktree list          -> exactly one worktree (D:/projects/bolt-sharp)
git status --short         -> clean (no concurrent writer)
git stash list             -> empty
prior B5-B-OWNER-01        -> none existed (docs/decisions/ contained no B5-B decision)
push state                 -> origin/main unchanged; 59 commits ahead; nothing pushed
```

All twelve commission preflight checks passed: branch `main`; exact HEAD and tree; one canonical
worktree; clean tree and index; no stash; no concurrent writer; no existing decision; B5-B
implementation and Q1 revisions committed; ancestry from all four accepted owner decisions
(verified with `git merge-base --is-ancestor` for `2bebd3ee`, `646c5729`, `41d048e9`, `6b2a0e3`);
no push; no production changes since the B5-A acceptance baseline (§6).

## 3. Acceptance authority and scope

This document is the formal B5-B acceptance record. The producer implementation report
(`docs/reviews/B5-B-IMPL-01.md`) and the independent qualification
(`docs/reviews/B5-B-Q1-independent-qualification.md`, plus its probes and evidence) are **evidence
inputs**; this decision is the **acceptance authority**. The accepted scope is exactly §24
restricted by §25. Nothing here authorizes remediation, B4/B6 work, additional qualification,
package publication or repository push.

## 4. Evidence reviewed

Read in full or verified directly from committed bytes during this decision:

- `docs/reviews/B5-B-IMPL-01.md`; `docs/reviews/evidence/B5-B-IMPL-01/` (62-entry manifest,
  scenario documents, TRX results, logs, scripts).
- `docs/reviews/B5-B-Q1-independent-qualification.md` (714 lines);
  `docs/reviews/probes/B5-B-Q1/` (18 source files, 53 probes) and
  `docs/reviews/evidence/B5-B-Q1/` (77 files, 76-entry manifest).
- `docs/decisions/B3-A-OWNER-01-owner-acceptance.md` (§6.3 — identifier `Q1-F02`),
  `B3-B-OWNER-01-owner-acceptance.md` (§9 normative retirement contract; §12 `Q-B3B-01…14`),
  `B3-C-OWNER-01-owner-acceptance.md` (§17 `Q-B3C-01…06`; §18 carry-forward),
  `B5-A-OWNER-01-owner-acceptance.md` (§20 `Q-B5A-01…10`; §21 inherited qualifications).
- Specification and planning context: `docs/boltsharp-spec.md`, `docs/implementation-tasks.md`,
  `docs/compatibility-target.md`; B0-B/B3-A/B3-B/B3-C/B5-A qualification chains as referenced.
- Production source paths relevant to the decisions: `src/BoltSharp/Transport/BoltTcpConnection.cs`,
  `BoltTcpServer.cs`, `BoltTcpServerOptions.cs`, `BoltTlsServerOptions.cs`,
  `src/BoltSharp/Sessions/BoltConnectionSession.cs`, `BoltSessionOptions.cs`,
  `src/BoltSharp/Messages/BoltMessageCodec.cs`, `src/BoltSharp/Backend/ReferenceOperationProvider.cs`.
- Normative Bolt 5.8 handshake/message/server-state semantics as used by both the producer and the
  reviewer (manifest v1 offer/selection; HELLO/LOGON metadata; RUN/PULL/DISCARD; IGNORED
  zero-field; FAILURE metadata; fail-closed state semantics).
- Direct evidence spot-checks performed for this decision (exact documents cited inline in §10–§22):
  `q1a02`, `q1a03`, `q1a04`, `q1b01`–`q1b04`, `q1c01`, `q1c02`, `q1d01`, `q1e03`, `q1f02`,
  `q1f05`, `q1g01`–`q1g06`, `q1-h01`–`q1-h03`, `q1i01`, `q1j01`, `q1k01`, `q1k02`,
  `q1a00-environment`; producer documents `b5b2-02`, `b5b3-02`; committed TRX counters.

## 5. Qualification chronology

| Stage | Disposition | Recorded revision |
| --- | --- | --- |
| B3-A | ACCEPTED WITH QUALIFICATIONS — CLOSED | owner acceptance `c2881ea5bee1d42629ff121eb25449bfeda52d79` (tree `4285faa8579033d981fa50919925583aea2e19c5`) |
| B3-B | ACCEPTED WITH QUALIFICATIONS — CLOSED | owner acceptance `aae3d0e986c3cef1dd4e0e49356cffafd06094d4` (tree `ef144bfd289e9b2b3599ed5c2b3f46111f5bee47`) |
| B3-C | ACCEPTED WITH QUALIFICATIONS — CLOSED | owner acceptance `853580659cc57ad959c026800c1e17de44cdddc1` (tree `a00ab7b189608da4ebf5a61ead6557e402d0bac4`) |
| B5-A | ACCEPTED WITH QUALIFICATIONS — CLOSED | owner acceptance `2bebd3ee77b12c190b2e48b763deb4444cc94e5d` (tree `346611f5ebcd878432c924e50f0ff9d255b6822b`) |
| B5-B-IMPL-01 | IMPLEMENTED, READY FOR INDEPENDENT B5-B-Q1 QUALIFICATION | final producer revision `646c572912de9a2ca415ec3548a99159cb2f4905` (tree `7670702e59933d84a685d09a0e44744a2dce34ec`) |
| B5-B-Q1 | SUPPORTED WITH QUALIFICATIONS | reviewer commit `41d048e9f6f611796bea5479ee1e8d8a9718551e` (tree `b40980ab494d62ef767136a1b5e79486935843ba`; parent exactly `646c5729`) |
| B5-B-Q1 reviewer session-provenance addendum | part of the reviewed evidence chain (not a new Q1 execution) | `6b2a0e36654cd209bfd8a88a39bd9d58f5097aa6` (tree `76133abce2ac3e9c2e7871b87528371a46ed3109`; parent `41d048e9`) |
| **B5-B-OWNER-01** | **ACCEPTED WITH QUALIFICATIONS — CLOSED** (this decision) | starting HEAD `6b2a0e3`; decision commit recorded in the final handoff |

Producer commits verified in order on `main`: `f79fb8d4` (harness, tree `4e4e81a3`) →
`b749a655` (scenario coverage, tree `339c77b2`) → `f8b46afe` (report + evidence, tree `003b5565`)
→ `d90ceab` (raw-byte checkout attributes, tree `596a5611`) → `646c5729` (evidence logs/TRX,
tree `7670702e`). Ancestry is linear and additive; every recorded commit and tree above was read
directly from Git during this decision. The reviewer addendum is provenance recording only — the
operator session added the reviewer session id and first-spawn note to report §15.1; it is not a
second review and changed no finding, evidence or disposition.

## 6. Evidence integrity and change isolation

Independently re-verified for this decision (raw-byte SHA-256 over working-tree bytes,
read-only; no evidence file was written or rewritten):

| Evidence package | Expected | Verified |
| --- | ---: | ---: |
| B5-B-Q1 reviewer | 76/76 | **76/76** |
| B5-B producer | 62/62 | **62/62** |
| B5-A-Q1 | 10/10 | **10/10** |
| B5-A producer | 18/18 | **18/18** |
| Earlier historical packages (B3-C-Q1 29; B3-C-IMPL-01 26; B3-B-Q3 33; B3-B-R2 29; B3-B-Q2 38; B3-B-Q1 12; B3-A-Q1 14; B3-A-R1 13; B3-A-Q2 15; B0-B producer 24; B0-B-Q1 119) | 352/352 | **352/352** |
| **Aggregate** | **518/518** | **518/518, 0 mismatched** |

Additional integrity results:

- The reviewer's own manifest-verification evidence (`evidence/B5-B-Q1/01-manifest-verification.txt`,
  `01-manifest-verification-detail.json`) records 442/442 across the historical + producer
  inventories; this decision independently re-verified all 15 manifests at 518/518 as tabulated.
- All four accepted owner decisions remain byte-identical to their committed blobs
  (B3-A `ab84ffa9…`, B3-B `211a5d56…`, B3-C `323303bd…`, B5-A `385c820e…`).
- Historical source evolution is accounted for, not rewritten: the B3-A-IMPL-01, B3-B-IMPL-01 and
  B3-B-R1 manifests describe their original revisions and include entries changed by the
  previously authorized R1/R2 remediations (as already recorded in the B3-A/B3-C owner decisions);
  none of those packages is part of the 352-entry verified set, and none was modified.
- **Change isolation since the B5-A acceptance (`2bebd3ee..HEAD`):** every changed path is an
  addition, confined to the B5-B producer report/evidence, the B5-B test-only project, the B5-B-Q1
  report/probes/evidence and the provenance addendum. Verified zero changes in `src/`,
  `tests/BoltSharp.Tests/`, the B0-B project, the B2-C scaffold, `BoltSharp.slnx` and
  `docs/decisions/`.
- **Credential hygiene:** a read-only scan of 1114 evidence/probe/report files found **zero**
  plaintext, hex-encoded or UTF-16-hex-encoded occurrences of the B5-B producer, B5-B-Q1 reviewer,
  B5-A probe or B0-B integration credential sentinels, and **zero** private-key markers. The only
  plain-text sentinel occurrences are the previously documented B5-A synthetic probe credential
  values in the B5-A evidence, which its README explicitly labels synthetic; those are historical,
  documented, test-only material outside B5-B scope (their literal values are deliberately not
  reproduced in this decision). LOGON payloads are redacted at
  decoded-message and raw-chunk level in both B5-B evidence sets.

## 7. Regression results

**Freshly executed during this decision** (read-only; no evidence files written; working tree
remained clean):

| Suite | Command (canonical form) | Fresh result |
| --- | --- | --- |
| Production build | `dotnet build src/BoltSharp/BoltSharp.csproj -c Debug --no-incremental --nodeReuse:false` | Build succeeded, **0 warnings, 0 errors** |
| Solution build | `dotnet build BoltSharp.slnx -c Debug --no-incremental --nodeReuse:false` | **0 errors; exactly 2 warnings** — the two historical `xUnit2012` advisories at `tests/BoltSharp.Tests/Messages/BoltRequestDecodeTests.cs:71-72` |
| Core total | `dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --no-build --nodeReuse:false` | **Passed! 1,217 / 1,217** |
| B5-A transport filter | `--filter 'FullyQualifiedName~BoltSharp.Tests.Transport'` | **80/80** |
| B3-C backend filter | `--filter 'FullyQualifiedName~BoltSharp.Tests.Backend'` | **94/94** |
| B3-B sessions filter | `--filter 'FullyQualifiedName~BoltSharp.Tests.Sessions'` | **119/119** |
| B3-A messages filter | `--filter 'FullyQualifiedName~BoltSharp.Tests.Messages'` | **98/98** |
| B5-B official-driver interoperability | `dotnet test tests/BoltSharp.DriverInteropTests/BoltSharp.DriverInteropTests.csproj -c Debug --no-build` (evidence env var explicitly cleared) | **40/40** |
| B0-B driver integration | `dotnet test tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj -c Debug` | **19/19** |

**Verified from committed evidence** (not re-executed here):

| Record | Result |
| --- | --- |
| B5-B-Q1 reviewer probes TRX (`b5bq1-probes.trx`) | **53/53 passing** (executed twice in fresh processes per the reviewer report) |
| B5-B-Q1 producer-suite TRX (`b5bq1-producer-suite.trx`) | **40/40** |
| B5-B-Q1 full-regression TRX (`b5bq1-full-regression.trx`) | **1,217/1,217** |
| B5-B producer TRX (`b5b-interop-suite.trx`, `full-regression.trx`) | **40/40; 1,217/1,217** |
| Q1 logs for transport/backend/sessions/messages/B0-B | match the fresh counts above |

No production or test file was changed to obtain these results. The two historical advisories
remain the only build warnings and are explicitly outside B5-B scope.

## 8. Reviewer independence acceptance

Recorded facts:

- **Producer:** DeepSeek V4.1 Flash, provider NVIDIA, OpenCode. Producer session provenance as
  recorded in the B5-B-IMPL-01 report; the producer did not self-qualify.
- **Reviewer:** GLM 5.3 Flash (model `nvidia/z-ai/glm-5.3-flash`), provider NVIDIA, OpenCode, in a
  **dedicated fresh child OpenCode session**, session id `ses_e3d0c2395ffeRFdJp7sKXquvv1`
  (operator-recorded, corroborated by session tooling, recorded in Q1 report §1.1 and §15.1).
- The reviewer used a **different model** from the producer, a **distinct execution session**,
  **fresh context** without the producer's conversation, **independently authored probes** (53,
  reviewer-owned infrastructure and oracles), historical and normative protocol oracles, and
  independent negative controls. The operator's later artifact and session-provenance verification
  was **not** a second independent review.
- A first spawn attempt with model `zai/glm-5.3` failed before any work began (provider balance);
  the review of record executed in the session above. This is recorded in Q1 report §15.1.

**Owner conclusion: the accepted B5-B requirement for separate model and executor-session
qualification was satisfied.** Also recorded: same operator, same host, same repository.
**No organizational independence is claimed, and this review must not be described as an external
audit.** The B3-C and B5-A bounded independence waivers were **not** applied and were not needed;
the committed evidence is consistent with this record.

## 9. Accepted official-driver identity

Verified from committed evidence (`B5-B-IMPL-01/00-environment.json`;
`B5-B-Q1/q1a00-environment.json`; wire captures `b5b2-02`, `q1b01`):

| Item | Value |
| --- | --- |
| Package | `Neo4j.Driver` **6.4.0** (exact pin, test-only; never upgraded or downgraded) |
| Assembly version | **6.4.12.0** |
| Informational version | `6.4.0+4ed413b01906c5d1682c62a3ee3c982cb28f99cd` |
| Wire `user_agent` | `neo4j-dotnet/6.4` |
| Wire `bolt_agent.product` | `neo4j-dotnet/6.4.12` |

Interoperability is accepted only for this verified client/version and protocol path. No claim is
made for other Neo4j.Driver versions or other language drivers.

## 10. Connectivity and handshake acceptance

Verified independently by the reviewer and re-checked from evidence (`q1a01`, `q1a02`, `q1a03`,
`q1a04`; producer `b5b2-02`, `b5b2-03`, `b5b2-04`):

- Constructing `IDriver` opens no connection; `VerifyConnectivityAsync()` over
  `bolt://localhost:{ephemeral}` establishes one real TCP connection, completes negotiation and
  authentication, and releases cleanly (`accepted=1, completed=1, failed=0, active→0`).
- **Manifest v1 is the negotiated path with the real driver.** Client preamble `6060B017` +
  proposals `000001FF` (marker), `00080805` (5.0–5.8), `00020404` (4.2–4.4), `00000003` (3.0);
  server offer `000001FF010000080500` (one range, capabilities 0); client selection
  `0000080500` (5.8, capability mask 0); the selection is pipelined directly with the framed
  HELLO and the server preserved it (first parsed message = HELLO); server-reported mode
  `ManifestV1`.
- Conventional fallback with manifest disabled is verified (`S: 00000805`, mode `Legacy`, full
  scalar query succeeds) — not inferred.
- Negative controls: server advertising nothing → driver fails with **zero HELLO sent**
  (`MessagesProcessed == 0`, handshake failure counted); connection-refused and early-EOF fail
  closed; driver retry behavior was bounded and driver-side with no additional server connections
  beyond the driver's own attempts.

**Owner disposition: ACCEPT real-driver Bolt 5.8 negotiation and Manifest v1 interoperability for
Neo4j.Driver 6.4.0**, complementing the previously accepted B5-A raw-TCP Manifest v1
qualification. No general manifest-version or multi-version negotiation support is claimed.

## 11. HELLO and LOGON acceptance

Verified (`q1b01`–`q1b04`; producer `b5b2-05`, `b5b2-20`–`b5b2-22`):

- HELLO keys `[user_agent, routing, bolt_agent]`; `user_agent=neo4j-dotnet/6.4`; `routing=null`;
  `bolt_agent.product=neo4j-dotnet/6.4.12` — the mandatory B3-A `bolt_agent` schema is exercised
  live by the real driver.
- LOGON keys `[scheme, principal, credentials]`, scheme `basic`; payload redacted at the
  raw-chunk level (`<redacted LOGON payload: 81 bytes>`); no credential value appears in any
  evidence document.
- HELLO SUCCESS `{server: "BoltSharp/0.1.0", connection_id: "bolt-b3b-1", hints: {}}`; LOGON
  SUCCESS `B170A0` (`SUCCESS {}`). The driver operates correctly **without** the optional
  `protocol_version` and `advertised_address` metadata; this is an observed driver-specific
  compatibility result for this client/configuration, **not** a universal statement that those
  optional fields are unnecessary for all drivers or protocol configurations.
- Authentication negative controls: invalid credentials, missing verifier and a throwing verifier
  all fail closed with terminal LOGON FAILURE (`50N42`/`Neo.ClientError.Security.Unauthorized`),
  exactly HELLO+LOGON processed, **no RUN ever sent**, no credential echo, listener healthy.

## 12. Scalar query acceptance

Verified (`q1c01`, `q1c02`; producer `b5b3-01`–`b5b3-03`): `RETURN 42 AS answer` through the
implicit auto-commit API yields one record with `answer` exposed as `System.Int64` `42`, correct
exhaustion (`FetchAsync` false afterwards) and cursor retirement; a subsequent query on the same
pooled connection returns 42 again. Wire oracle byte-exact: RUN
`B310D01352455455524E20343220415320616E73776572A0A0`, PULL `B13FA1816EC903E8`, RUN SUCCESS
`fields=["answer"]`, exactly one RECORD `B171912A`, terminal `B170A0`; no ROUTE/TELEMETRY/DISCARD
and no duplicate RUN.

**Owner disposition: ACCEPT scalar auto-commit query interoperability within the supported
reference-backend query surface.** No full expression evaluation or general Cypher support is
claimed.

## 13. Graph query acceptance

Verified (`q1d01`; producer `b5b4-01/02`): `MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b`
materializes through the driver's public result API:

- Alice — `INode`, numeric id 1, element id `n:1`, label `Person`, `name="Alice"`.
- Bob — `INode`, numeric id 2, element id `n:2`, label `Person`, `name="Bob"`.
- KNOWS — `IRelationship`, numeric id 1, element id `r:1`, start element id `n:1`, end element id
  `n:2`, type `KNOWS`, `since=2024`.

The deliberate node/relationship numeric-id collision preserves distinct typed identities. The
RECORD additionally matches the accepted golden bytes (reviewer `q1d01`; producer `b5b4-02`).

**Owner disposition: ACCEPT official-driver graph-value interoperability for the verified
reference graph query.** This is not acceptance of a general graph database.

## 14. Parameter acceptance

Verified (`q1e01-*` ten classes, `q1e02`, `q1e03`; producer `b5b5-01`–`b5b5-03`): positive,
negative and boundary integers, Unicode strings, booleans true/false, explicit null, lists, nested
maps and query-like string values are preserved with correct driver-visible types; query-like
parameter text is returned verbatim and not executed (graph verified unchanged); explicit null is
distinguished from a missing parameter, and the missing-parameter failure is classified as a
**server-side reference-backend refusal** (the RUN reached the wire with an empty parameter map;
`50N42`/`Request.Invalid`).

**Owner disposition: ACCEPT typed parameter interoperability within the accepted B3-C parameter
surface.** The exact-form `$value` query limitation (Q-B3C-01/Q-B3C-03) remains binding; no
arbitrary parameterized Cypher support is implied.

## 15. Cursor exhaustion and pooling acceptance

Verified (`q1c01`, `q1c02`, `q1f01`–`q1f06`; producer `b5b6-01`–`b5b6-05`): full and partial
result consumption, `DISCARD` after partial consumption, session disposal with an outstanding
result, multiple sequential queries, repeated sessions, exactly-once operation retirement, and
driver disposal. **Pooling is instrumented, not assumed:** the reviewer's per-connection provider
oracle (one provider instance per accepted connection) shows three sequential sessions reusing
**one physical connection** (`q1f05`: 3 prepares/3 operations/3 disposals/0 failures on one
connection; client sequence `HELLO,LOGON,RUN,PULL,RESET ×3,GOODBYE`).

**Owner disposition: ACCEPT physical connection reuse for the verified supported query paths.**
General concurrency and pooling scalability are not claimed.

## 16. Terminal `has_more` qualification closure

Previously deferred material: the B3-B bounded-`PULL`/terminal representation and the real-server
`has_more:false` byte reconciliation (`Q-B3C-06`; B5-A owner acceptance §21 carry-forward;
B3-B terminal-semantics note). The independent review established for Neo4j.Driver 6.4.0 and the
tested query/cursor paths: terminal `SUCCESS {}` (`B170A0`, absent `has_more`) is accepted as
exhaustion with **no** unnecessary additional PULL; `has_more:true` (fetch size 1) drives further
PULLs without duplication or omission (`q1c01`, `q1f01`, `q1f02`); the connection remains usable
after exhaustion.

**Owner disposition: CLOSE the deferred live-driver terminal-exhaustion interoperability
obligation for Neo4j.Driver 6.4.0 and the tested query/cursor paths** (scope-limited closure of
`Q-B3C-06`'s live-driver deferral and the B5-A §21 terminal-`has_more` reconciliation note).
Preserved: the underlying B3-B terminal SUCCESS representation, the historical byte-level
difference to the real-server golden, and the limited verified scope. Historical fixtures are not
rewritten. No claim is made for all future drivers, protocol versions or transaction modes.

## 17. TLS interoperability acceptance

Verified (`q1g01`–`q1g07`; producer `b5b7-01`–`b5b7-05`):

- `bolt://` plaintext: full connectivity and supported queries.
- `bolt+ssc://` self-signed test certificate: successful TLS negotiation, authentication and the
  scalar query (answer 42; clean counters).
- `bolt+s://`: the untrusted test certificate is **correctly rejected** under ordinary
  system-store trust (zero Bolt messages processed) — the explicit system-trust path.
- Isolated trust-list arrangement (`bolt://` + `EncryptionLevel.Encrypted` +
  `CertificateTrustRule.TrustList`): encrypted connectivity demonstrated under explicit trust.
  This is **not** machine-system-store trust and is not presented as such.
- Negative controls: plaintext client against TLS listener fails closed and is counted
  (`tls_handshake_failures=1`); TLS client against plaintext listener fails closed
  (`bolt_handshake_failures=1`); invalid credentials over TLS rejected
  (`Unauthorized`/`50N42`, non-retriable); no downgrade path observed. No permanent machine
  trust-store change was made.

**Owner disposition: ACCEPT TLS interoperability within the verified plaintext, self-signed and
isolated-trust scenarios.** All B5-A TLS policy-depth and production-configuration
qualifications (`Q-B5A-02`, `Q-B5A-03`) are retained. No production TLS certification is claimed.

## 18. F01 — unsupported-command transport/session mismatch

Producer `B5-B-IMPL-F01`; reviewer `B5-B-Q1-F01`. Independently reproduced (`q1-h01`, `q1-h02`,
`q1-h03`):

1. The official driver sends valid Bolt 5.8 messages: `BEGIN` (explicit
   `BeginTransactionAsync`; managed `ExecuteReadAsync` retried 5 BEGIN attempts within its retry
   budget) and `ROUTE` (`neo4j://`); `COMMIT`/`ROLLBACK` were **not** reached because BEGIN fails
   first, and their full transaction sequences were not tested.
2. The B3-A decode boundary rejects them (`codec:UnsupportedMessage`); `server_failure_responses`
   is empty — the transport closes the connection without the B3-B documented `08N06` FAILURE.
3. The B3-B documented unsupported-command path (`BoltConnectionSession.ReceiveBytes` →
   `UnsupportedMessageFailure`) is unreachable through the actual transport
   (`BoltTcpConnection.cs` decodes via the codec and calls `session.Receive`).
4. Driver-visible: `ServiceUnavailableException` (managed flow surfaced a retry-budget
   `TimeoutException`; routing flow `SessionExpiredException` wrapping the same cause).
5. The reviewer found the closure normatively **conformant** in the tested circumstances
   (a protocol refusal may end the connection) but inconsistent with BoltSharp's own accepted
   B3-B contract. No supported-query correctness failure; no execution; no graph change; no
   security bypass; server-side connection resources retired.

**Owner disposition: F01 — ACCEPTED WITH QUALIFICATION; SEPARATE REMEDIATION RECOMMENDED BEFORE
RELEASE.** The decision retains: the exact observed behavior; the conflict with the documented
B3-B refusal path; no supported-query correctness failure; no security bypass; the driver-visible
connection-closure effect; and the future remediation requirement. **No F01 production change is
authorized by this decision.**

## 19. F02 — generic error classification and connection disposal

Producer `B5-B-IMPL-F02`; reviewer `B5-B-Q1-F02`. Independently reproduced (`q1i01`, `q1j01`):
the reference backend refuses the unsupported query with the accepted generic
`50N42`/`Neo.ClientError.Request.Invalid`; the driver exposes a non-retriable
`ProtocolException` (GQL classification UNKNOWN); the affected physical connection is destroyed
by the driver (per-connection instrumentation: refused connection had one provider consultation,
zero operations, no reuse; recovery ran on a **fresh** physical connection); the reference graph
is unchanged; no duplicate execution occurred.

Adjudicated: the `50N42` refusal complies with the accepted bounded error-code contract
(`Q-B3C-02`); the driver is entitled to discard a connection it classified as unrecoverable; the
behavior compromises reuse efficiency only, not correctness; no unsafe retry duplication exists.

**Owner disposition: F02 — ACCEPTED CONFIGURATION/INTEROPERABILITY QUALIFICATION; OPTIONAL
REFINEMENT.** More precise classification might improve driver exception categorization,
same-connection recovery, pooling efficiency and diagnostics, but it is **not required** for the
current bounded acceptance. `50N42` must not be changed without a separate authorized contract
assessment, and new-connection recovery must not be described as RESET recovery of the original
connection.

## 20. Live-driver IGNORED qualification narrowing

Original obligation: B3-A owner acceptance §6.3, identifier **`Q1-F02`** (carried forward as
`Q-B3B-12`); the historical B0-B fake-server fixture's nonconforming one-field IGNORED (`B1 7E A0`)
is preserved unchanged.

Independent live-driver evidence (`q1j01`): the official driver naturally pipelines
`RUN MATCH (n) DETACH DELETE n {} {}` and `PULL {n=1000}` in one client write; the server
answers FAILURE (`50N42`) for RUN and **zero-field IGNORED, byte-exact `B0 7E`**, for PULL; the
driver consumes the sequence without desynchronization and reports the RUN failure; no fabricated
RECORD or SUCCESS; no unintended query execution (refused connection: one provider consultation,
zero operations opened); the connection is destroyed by the driver and recovery occurs on a fresh
connection.

**Owner disposition: NARROWLY CLOSE the B3-A `Q1-F02` live-driver obligation for correct IGNORED
encoding and successful official-driver consumption** (scope-limited resolution recorded here as
a later decision; the historical invalid fixture and the B3-A acceptance record are not modified).
**RETAIN a separate qualification for same-connection RESET recovery** after the observed
IGNORED scenario (new `Q-B5B-05`): it was **not established** — under the accepted classification
the driver destroys the connection instead of sending RESET. Same-connection recovery is **not**
covered by this narrowing, and no related recovery behavior is closed by implication.

## 21. Fail-closed operation retirement acceptance

The B3-B normative contract remains binding (B3-B owner acceptance §9): **an unsuccessful
retirement attempt permanently invalidates the connection** — DEFUNCT, no RESET recovery, no
subsequent RUN, no second disposal attempt; the external resource is never falsely reported as
released; the guarantee is containment and prohibition of further execution.

Independently verified through the official driver, the real B5-A transport and a reviewer-owned
fault-injecting provider (`q1k01`–`q1k04`): one operation opened; exactly one disposal attempt
(one failure); the terminal response was FAILURE
(`50N00`/`Neo.DatabaseError.General.UnknownError`), **never SUCCESS** (wire-oracled: server
sequence `SUCCESS,SUCCESS,SUCCESS,RECORD,FAILURE`); the affected connection became DEFUNCT and
closed with no RESET recovery and no second operation; no disposal retry; the driver observed the
failure (`DatabaseException`). Controls: recoverable provider failure (no operation opened),
recoverable failure with successful retirement, client disposal during an active result (single
retirement), server counters clean. A new driver-created connection remains distinct from the
failed session (per-connection provider identity).

**Owner disposition: ACCEPT the verified fail-closed retirement behavior through the real
driver.** Every inherited B3-B lifecycle qualification is preserved. No claim is made that a
throwing `Dispose` released its external resources.

## 22. Reviewer documentation findings

`B5-B-Q1-N01` — the producer test `ConnectivityTests.B5B2_07` guards its protocol-version
assertion with `if (versionText is not null)`, so the assertion silently disappears if the value
is absent (producer-suite robustness note; the reviewer's own probe `q1a01` asserts the
equivalent values unconditionally). Runtime behavior is not affected: the driver-visible
negotiated version and server agent are established by the reviewer's unconditional probes and by
other producer assertions. **Disposition: ACCEPTED DOCUMENTATION OBLIGATION — NON-BLOCKING.** No
producer-test rewrite is authorized; fold any cleanup into a later separately authorized change.

Additional non-blocking producer-test observations recorded by the reviewer (report §4.1):
a timing-based 250 ms negative check in `B5B2_01`, and the producer suite containing no in-probe
golden RUN/PULL hex comparison (independently closed by the reviewer's byte-exact captures).
Recorded as observations; no acceptance impact.

## 23. Binding qualifications

### 23.1 Inherited B3-A/B3-B/B3-C/B5-A qualifications (retained in substance with authoritative identifiers)

All applicable qualifications from the accepted owner decisions remain binding; none is silently
closed except where §16/§20 record an exact scope-limited closure:

- **B3-A** (`B3-A-OWNER-01`): `Q1-F01` closed; `F-Q2-01` accepted limitation; `Q1-F02` (zero-field
  IGNORED live verification) **narrowly closed per §20**; additional retained qualifications of
  B3-A §6.4 (PULL/DISCARD `n/qid < −1` policy; TELEMETRY decode vs session-domain split; capture
  redaction limits; author-independence caveat; NC-03b/NC-05b/NC-08 deferrals; zero-arity swap
  classification; pre-existing formatting/analyzer observations).
- **B3-B** (`B3-B-OWNER-01` §9, §12): the normative fail-closed retirement contract (§21);
  `Q-B3B-01` bounded `PULL`; `Q-B3B-02` no full RESET jump-ahead; `Q-B3B-03` `ReceiveBatch`
  ownership; `Q-B3B-04` single-owner execution; `Q-B3B-05` provisional public APIs;
  `Q-B3B-06` bounded failure metadata (its B5-B verification obligation is discharged for the
  observed refusal classes; the limitation remains); `Q-B3B-07` TELEMETRY structural boundary;
  `Q-B3B-08` no general cancellation API; `Q-B3B-09` driver interoperability (narrowed per §15
  for the verified paths; the broader qualification stands); `Q-B3B-10` historical advisories;
  `Q-B3B-11` historical reviewer-independence governance; `Q-B3B-12` B3-A F-02 (**narrowed per
  §20**); `Q-B3B-13` fail-closed supersession; `Q-B3B-14` diagnostic semantics.
- **B3-C** (`B3-C-OWNER-01` §17): `Q-B3C-01` exact-form query allowlist; `Q-B3C-02` refusal
  granularity (the `50N42` classification is retained — §19); `Q-B3C-03` parameter surface;
  `Q-B3C-04` bounded materialization; `Q-B3C-05` normalization limits; `Q-B3C-06` terminal
  SUCCESS representation (its live-driver deferral closed per §16 without changing the underlying
  representation).
- **B5-A** (`B5-A-OWNER-01` §20): `Q-B5A-01`/`Q-B5A-02`/`Q-B5A-03` retained (Q-B5A-01's
  continuing requirement is satisfied by the B5-B-Q1 session/model separation recorded in §8);
  `Q-B5A-04` remains closed (raw-TCP manifest); `Q-B5A-05` slow-provider responsiveness;
  `Q-B5A-06` blocked-write backpressure **remains open** (release obligation); `Q-B5A-07`
  session-options passthrough limitation; `Q-B5A-08` test-only request encoding; `Q-B5A-09` no
  performance claims; `Q-B5A-10` bounded shutdown.

### 23.2 New B5-B binding qualifications

- **Q-B5B-01 — Official-driver version/scope.** Interoperability is established only for
  `Neo4j.Driver` 6.4.0 (assembly 6.4.12.0) over direct `bolt://`/`bolt+ssc://` and the verified
  isolated-trust arrangement, on Windows/net8.0. No claim for other driver versions, other
  language drivers or other platforms.
- **Q-B5B-02 — Limited query support.** Only the accepted reference-backend query surface (five
  exact forms, `Q-B3C-01`) is exercised and accepted; general Cypher is not supported.
- **Q-B5B-03 — Unsupported-command transport closure (F01).** Valid but unsupported
  BEGIN/COMMIT/ROLLBACK/ROUTE are refused by connection closure at the decode boundary without
  the B3-B documented `08N06` FAILURE; remediation separately authorized and recommended before
  release (§26).
- **Q-B5B-04 — Generic refusal/error classification (F02).** `50N42` remains the bounded
  classification; drivers may treat refusals as unrecoverable per-connection and discard the
  physical connection; refinement optional and separately authorized.
- **Q-B5B-05 — Same-connection recovery after IGNORED not established.** After the observed
  FAILURE/IGNORED sequence the driver destroys the connection; RESET recovery of that connection
  was not demonstrated.
- **Q-B5B-06 — TLS trust-arrangement limitations.** `bolt+s://` is verified only as correct
  rejection of an untrusted certificate; the trust-list arrangement is isolated and not
  system-store validation; all B5-A TLS-depth qualifications retained.
- **Q-B5B-07 — No general transactions/routing/multi-database.** Explicit and managed
  transactions, routing and multi-database selection remain unsupported; refusals occur without
  execution or mutation.
- **Q-B5B-08 — No cross-driver production interoperability claim.** This milestone is a bounded
  interoperability demonstration, not a compatibility certification.
- **Q-B5B-09 — Release-grade hardening pending.** Blocked-write backpressure (`Q-B5A-06`),
  production TLS policy, cancellation/shutdown boundaries, security review, public API stability
  and performance/resource budgets remain future release obligations.

## 24. Formal accepted scope

Within the evidence scope cited in each section, B5-B is accepted for exactly:

1. Official `Neo4j.Driver` 6.4.0 direct connectivity (§10).
2. Bolt 5.8 negotiation (§10).
3. Manifest v1 with the actual driver (§10).
4. Conventional handshake fallback (§10).
5. HELLO and LOGON interoperability (§11).
6. Basic authentication with fail-closed negatives (§11).
7. Scalar auto-commit queries (§12).
8. Typed graph results (§13).
9. Supported parameter values (§14).
10. Result cursor exhaustion (§15).
11. Partial-result DISCARD (§15).
12. Sequential supported queries (§15).
13. Physical (pooled) connection reuse — instrumented (§15).
14. Terminal `SUCCESS {}` semantics — scope-limited closure (§16).
15. Incremental `has_more:true` pagination (§16).
16. Self-signed TLS interoperability (`bolt+ssc://`) (§17).
17. Isolated-trust TLS interoperability (explicit trust list) (§17).
18. Unsupported-query rejection with observable driver classification (§19).
19. Actual driver consumption of zero-field `B0 7E` IGNORED — narrow closure (§20).
20. Driver-observable fail-closed retirement (§21).
21. Connection lifecycle and multi-client isolation (§15, reviewer `q1l01`–`q1l04`).
22. Preserved historical regression results (§7).
23. Separately executed reviewer qualification (§8).

Every accepted claim above is tied to the cited committed evidence. **This is acceptance of
limited official-driver interoperability, not acceptance of full Neo4j server compatibility.**

## 25. Explicit non-claims

This decision does not claim: full Cypher execution; arbitrary graph patterns; graph mutation;
persistent graph storage; general explicit transactions; managed transaction support; general
ROUTE compatibility; multi-database support; clustering; universal driver compatibility;
compatibility with every `Neo4j.Driver` version; complete Bolt 5.8 conformance; complete
notification or result-summary metadata; same-connection RESET recovery after the observed
IGNORED scenario; full production error classification; configurable session options beyond the
accepted B5-A limits; proven blocked-write backpressure behavior; general provider cancellation;
full mid-flight RESET interruption; production-grade TLS certification; load or scalability
qualification; stable public API compatibility; Browser/WebSocket interoperability; production
deployment readiness; or NuGet release readiness.

**B5-B is an interoperability milestone, not final product certification.**

## 26. F01 follow-up boundary and sequencing

Recommended candidate (not authorized by this decision):
**B5-B-F01-R1 — Restore Accepted Unsupported-Command Refusal Behavior Across the
Transport/Session Boundary.** Candidate scope: investigate the B3-A decode path and the B3-B
`ReceiveBytes` entry point; preserve structural validation; restore the accepted
unsupported-command disposition; validate official-driver error behavior; preserve terminal state
and resource ownership; add independently reviewable regression evidence. A future remediation
must verify: valid unsupported messages receive the correct disposition; malformed messages still
fail appropriately; B3-A structural validation is not weakened; B3-B lifecycle semantics remain
intact; no second protocol parser is introduced; no session-state bypass appears;
official-driver connection behavior remains correct; B5-A transport tests remain passing; all
historical evidence remains preserved. It must not be combined with F02 refinement and must not
be silently broadened into explicit-transaction support; the goal is correct refusal behavior,
not implementing BEGIN/COMMIT/ROLLBACK semantics.

**Owner sequencing decision: accepted** — F01 may be remediated after B5-B owner acceptance
because the supported interoperability surface has no blocking defect, but it **must be revisited
before any release-grade conformance claim**.

## 27. B5 milestone closure relationship

B5 comprises B5-A (TCP/TLS transport, accepted and closed with qualifications) and B5-B (official
Neo4j driver interoperability, accepted and closed with qualifications by this decision). With
this decision, **B5 implementation scope is complete, subject to all retained qualifications.**
This does not mean BoltSharp is production-ready, and no separate umbrella B5 owner-decision file
is created. B5 completion does not close or authorize B4 or B6.

## 28. Future roadmap and release obligations

After B5-B acceptance, the remaining roadmap (each subject to separate authorization):

1. **B5-B-F01** — narrow cross-component refusal remediation (§26).
2. **B4** — Browser/WebSocket transport.
3. **B6** — hardening, packaging and release qualification, including at minimum: genuine
   blocked-write backpressure qualification (`Q-B5A-06`), production TLS policy and deployment
   configuration, cancellation and shutdown boundaries, a security review, public API stability,
   performance and resource budgets, and a release-grade qualification executed by a genuinely
   separate executor session/model.

None of these activities is authorized by this decision.

## 29. Formal owner disposition

**B5-B — ACCEPTED WITH QUALIFICATIONS — CLOSED.**

Recorded on the basis of:

1. B5-B producer implementation complete and committed (`646c5729`, tree `7670702e`).
2. B5-B-Q1 independent qualification **SUPPORTED WITH QUALIFICATIONS** (`41d048e9`, tree
   `b40980ab`; provenance addendum `6b2a0e3`, tree `76133ab`).
3. The separate model and executor-session qualification requirement satisfied (§8); no
   organizational independence claimed.
4. No blocking defect within the accepted interoperability surface: reviewer 53/53 probes,
   producer 40/40, fresh owner re-execution of all suites at their expected counts, evidence
   integrity 518/518.
5. Official `Neo4j.Driver` 6.4.0 connectivity, scalar and graph queries, typed parameters, cursor
   completion, instrumented connection reuse and Manifest v1 interoperability proven.
6. TLS interoperability accepted within the verified plaintext, self-signed and isolated-trust
   modes (§17); no production TLS certification claimed.
7. Terminal `has_more` compatibility established for the tested driver/query paths (§16,
   scope-limited).
8. Zero-field `B0 7E` IGNORED encoding and live-driver consumption established; the B3-A `Q1-F02`
   obligation narrowly closed (§20).
9. Same-connection RESET recovery after the observed IGNORED remains **unverified** (`Q-B5B-05`).
10. F01 remains a non-blocking cross-component inconsistency with separately authorized
    remediation recommended before release (§18, §26).
11. F02 remains an accepted generic-classification limitation with optional refinement (§19).
12. Fail-closed operation retirement remains binding and was independently verified through the
    real driver (§21).
13. Historical evidence integrity is preserved; all four prior owner decisions are byte-identical
    (§6).
14. All applicable inherited qualifications and the new B5-B qualifications remain binding (§23).
15. No release certification is implied (§25); no F01/F02 remediation, B4, B6, additional
    qualification, package publication or push is authorized.

## 30. Commit and verification record

- **Decision document:** `docs/decisions/B5-B-OWNER-01-owner-acceptance.md` (this file; the only
  file authorized and committed by B5-B-OWNER-01).
- **Parent commit:** `6b2a0e36654cd209bfd8a88a39bd9d58f5097aa6`
  (tree `76133abce2ac3e9c2e7871b87528371a46ed3109`).
- **Commit message:** `docs(boltsharp): accept B5-B official driver interoperability with qualifications`.
- **Decision commit and tree:** recorded in the commit record appended below and verifiable via
  `git log --format='%H %T' -1` after the decision commit.
- **Post-commit verification performed per commission §32** (read-back from the committed blob):
  formal disposition; exact accepted driver version; reviewer-independence evidence; bounded
  supported-query claims; F01/F02 classifications retained without remediation authorization;
  scope-limited `has_more` closure; narrow IGNORED closure; same-connection RESET recovery open;
  fail-closed retirement binding; inherited qualifications binding; blocked-write backpressure
  open; explicit non-claims present; B4/B6 not authorized; historical owner decisions unchanged;
  exactly one new file committed; `main`, one worktree, clean; no push. Results recorded in the
  final handoff.
