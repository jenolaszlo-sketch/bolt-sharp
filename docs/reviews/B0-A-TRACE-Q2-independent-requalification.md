# B0-A-TRACE-Q2 — Targeted independent listener shutdown requalification

**Disposition: REMEDIATION REQUIRED. Owner acceptance is not recommended.**

**TRACEQ1-F01: CLOSED for the original escaping endpoint/prologue lifecycle defect.** The authentic
driver experiment remains valid. However, R1 does not meet the required distinction between
expected closure and unexpected socket failure: controlled non-closure failures are classified
as harmless closure. A separate framed-read closure classification gap also remains. This report
does not approve the milestone and implements no fixes.

Date: **2026-11-06 Asia/Manila** (execution timestamps are UTC on 2026-11-05).
Qualified repository: `D:\projects\bolt-sharp`, canonical `main`.
Owner authorization: the B0-A-TRACE-Q2 request supplied to this session.

## 1. Identity, ownership and provenance

| Identity | Commit | Tree |
| --- | --- | --- |
| Q1 reviewer baseline | `f24afb79ddf9737583e17887e2874c0e781ab354` | `0ced7b6d95e02db24c16567888ddb8d827609c84` |
| R1 remediation | `262dc01814b25d99f2df6127746afc657f87a750` | `715e3569d1193d2b97654c385ff02a99203208c7` |
| R1 evidence / qualified HEAD | `3408e6b05af6c449e93979a0ff993ef7e52d9d3b` | `38ea330bec12666505f978310f39348d620428a3` |

Ancestry is exactly baseline -> remediation -> evidence HEAD, with each preceding commit the
next commit's sole parent. The reviewer commit containing this report adds only the three new
Q2 artifact paths; its commit/tree identities are provided in the final handoff.

Before any canonical write, elevated read-only inspection found the pinned HEAD/tree, `main`,
clean index and working tree, exactly one worktree, and no Git lock. No repository AGENTS.md
was found in the ancestors or applicable docs/tests subdirectories. The sole authorization to
modify this checkout in this assignment belongs to this session; no subagents were created.
Repeated pre-write checks found no active BoltSharp testhost or evidence runner. Ten existing
MSBuild worker nodes had identical CPU counters across two snapshots and an absent parent;
these were idle retained build workers, not evidence runners. OpenCode desktop/service was
running, but no BoltSharp command or active writer was demonstrated. The app's other active
chat concerned a different repository. This is observational exclusivity, not an OS-enforced
writer lock. Only Q2 paths changed during this review; unexpected concurrent writes were not
observed. The initial sandbox denial was resolved through approved elevated repository access.

Reviewer: this OpenAI Codex session, chat `01a1acb3-b403-7520-b3ca-fd3c38d39e40`, identified by
its runtime instructions as GPT-6-based Codex. This session did not author R1 or participate in
its implementation; it authored these Q2 probes, evidence and report only. Q1 identifies its
reviewing session as DeepSeek V4.1 Flash through OpenCode. **Author independence remains
UNVERIFIED**: Git metadata and these session statements cannot establish independent human,
organizational, or model provenance. A distinct reviewer Git author label is an attribution
label, not proof of independent signoff. Technical conclusions below rest on executable controls
and source assessment, rather than the producer's assurances.

## 2. Source and evidence integrity

[integrity.json](evidence/B0-A-TRACE-Q2/integrity.json) records the exact ancestry, complete
remediation change-set, manifest checks and independently parsed historical TRX results.

* R1 changes exactly two implementation/test paths: the test-only listener and the new ten-test
  `ListenerShutdownRegressionTests.cs`. Its other additions are its review document and evidence.
  There are no unauthorized implementation changes in the qualified range.
* `src/BoltSharp`, solution and shared build props are unchanged against both the Q1 baseline
  and the accepted pre-trace baseline `9abf5e3`. Producer evidence, Q1 report, Q1 probes
  (including T10) and Q1 evidence are unchanged against the pinned Q1 baseline.
* SHA-256 and byte lengths verify from **both Git blobs and working files**: producer **30/30**,
  Q1 **28/28**, R1 **29/29**. Each committed evidence directory contains precisely its listed
  entries plus the manifest itself; no unlisted/missing committed evidence was found.
* All 23 historical TRX files were independently parsed as XML. Result-element counts match
  total/passed/failed counters. Producer and Q1 driver results are 37 passed/10 NotExecuted;
  R1 is 47 passed/10 NotExecuted. Other historical totals match their reports. R1's 29-file
  manifest does not include a separate shutdown-only TRX; its post-fix console evidence reports
  ten passes and the driver TRX contains those ten cases. Q2 records a separate shutdown TRX.
* Q2 source controls are byte-faithful snapshots in new Q2 evidence paths. The pre-fix listener
  comes directly from the baseline Git blob. The GetStream variant replaces exactly one
  `client.GetStream()` expression with a reviewer callback wrapper; no exception handler,
  classification, algorithm or cleanup logic is changed. SHA-256 and transformation details
  are in [source-controls.json](evidence/B0-A-TRACE-Q2/source-controls.json).

## 3. Lifecycle assessment and F01 determination

The original preserved Q1 T10 failure contains an NRE stack from `SafeRemoteEndPoint` through
`HandleConnectionAsync` and `DisposeAsync`. Independently executing Q01/Q02 against the
pre-fix Git snapshot reproduces the null dereference deterministically, rather than hoping to
win the historical scheduling race. The root cause is `TcpClient.Close()` nulling `Client`
before endpoint access outside the handler's protected boundary. Pre-fix GetStream acquisition
was outside that boundary too.

R1 captures the endpoint in the accept loop **before publishing the accepted local client to
the shutdown client list**. A remote peer closing does not dispose that local TcpClient; Q04
tests that distinction. `client.Client?.RemoteEndPoint` evaluates a nullable socket reference;
the helper catches disposed/socket access failures. The handler trace now contains only
already-captured metadata. Socket option/stream acquisition and nullable CTS initialization
are inside the try; null-aware cancellation filters do not dereference an uninitialized CTS.
The acquisition guard handles the closed-client prologue, and the outer ODE catch handles the
negotiation read case. The explicit GetStream hook proves closure after NoDelay but immediately
before the real GetStream call produces CLOSED with an ODE and does not escape.

The accept loop publishes each handler before its loop task completes. Disposal cancels first,
closes a snapshot of clients, joins the accept loop, then snapshots and joins handlers. Thus a
client accepted after the initial client snapshot still receives the cancelled lifetime token,
and its handler is included in the later handler snapshot. Completed handlers can safely be
added to the tracking list; the connection cap bounds that list. The finally block removes
clients, publishes traces and closes TcpClient. Its tracing operations access managed capture
state, not disposed socket state. Later client socket inspection is within the observation
exception boundary. No reachable disposed-socket access outside a protected boundary was
demonstrated for the original NRE/ODE mechanism.

Final exact-source Q01/Q02/Q03/Q05/Q06/Q07/Q08/Q09/Q10/Q12/Q17/Q18/Q24, plus the single-site
GetStream hook Q19, establish the expected bounded lifecycle behavior under the declared
conditions. No unexpected NRE/ODE escaped, handler/accept tasks completed, tracked client lists
emptied, and sampled listening ports refused new connections. The forty controlled alternate
interleavings are additional coverage, not proof of all possible schedules. Cleanup checks do
not establish absence of every OS or GC-managed resource leak; none was demonstrated in the
final adapter runs.

**TRACEQ1-F01 final status: CLOSED.** There is no remaining counterexample to the original
escaping endpoint/prologue finding in the exercised conditions. This is a finding-specific
closure; the overall remediation still fails the diagnostic contract described below.

## 4. Independent probes and negative controls

The default Q2 project links the exact working listener source. Controlled TcpClient pairs and
reflection call its actual private helper/handler and populate its actual tracking lists.
Real accept-loop tests exercise four simultaneous wire handshakes. TaskCompletionSource
signals establish entry into reads before disposal/cancellation; timeouts (eight seconds per
bounded operation) detect deadlocks and never establish a phase. The listener's own handler
join bound remains five seconds. The decisive adapter delegates native reads to an exact
NetworkStream and owns its socket as TcpClient's ordinary stream does.

| Probes | Contract / control | Decisive result |
| --- | --- | --- |
| Q01–Q04 | Local close before endpoint capture, closed handler, close after capture, remote-close distinction | pass |
| Q05–Q06 | Real blocked read; local close and listener disposal; complete task joins | pass |
| Q07–Q08 | Alternate cancellation/closure order; concurrent cancellation/disposal behind a release barrier | four theory cases pass |
| Q09 | Pending accept, repeated disposal, cancellation after disposal | pass |
| Q10 | Four simultaneous connections through the real accept loop; wire-selected 5.8; complete drain | pass |
| Q11 / Q16 / Q22 | Unexpected handler / CTS-prologue / framed-read exception remains ERROR | pass |
| Q12–Q13 | Injected mid-negotiation ODE / connection reset has CLOSED outcome | pass |
| Q14 | NetworkDown, NetworkUnreachable, NoBufferSpaceAvailable, while sockets are open and shutdown is not requested | **three failures: CLOSED, expected ERROR** |
| Q15 | IOException wrapping NetworkDown during negotiation | **failure: CLOSED, expected ERROR** |
| Q17–Q18 | Forty alternate interleavings; real abortive peer reset; task/client drain | pass |
| Q19 (single-site hook) | Local close after NoDelay, immediately before the real GetStream | pass: CLOSED, ODE |
| Q20 (single-site hook) | Unexpected NetworkDown acquisition fault | **failure: CLOSED, expected ERROR** |
| Q21 | ODE during framed-message read after successful negotiation | **failure: post-handshake ERROR** |
| Q23 | IOException wrapping NetworkDown during framed-message read | **failure: CLEAN_EOF, expected ERROR** |
| Q24 | Listener disposal during real blocked framed-message read | pass |

Fault-injection controls intentionally supply exceptions to an open, connected test socket's
stream. They prove classification at the actual catch boundaries; **they do not claim a real
Windows network outage or resource exhaustion was induced**. Framed-read controls supply a
synthetic legacy preamble, never authenticated traffic, to reach the next lifecycle phase.
All such traffic is separate from authentic driver evidence.

Pre-fix controls use the same test source and unmodified baseline listener snapshot. Q01, Q02
and Q12 fail before R1 and pass after R1, distinguishing the lifecycle correction. Q11 remains
ERROR before and after. All three Q14 direct SocketException cases pass before R1 (ERROR) and
fail after it (CLOSED), demonstrating the new classification regression. Q15's wrapped socket
case fails both before and after: that IOException classification predates R1 and still fails
the explicit Q2 requirement. The pre-fix selected run has **4 passed / 4 failed** (exit 1), as
expected from those controls; it is not a regression-suite pass claim.

### Harness revisions and evidence interpretation

All exploratory logs/TRX are retained. Initial `q2` ran 22 cases (17 pass/5 fail); four were the
fault-classification failures, and Q05's timeout used an adapter with inappropriate socket
ownership and a derived NetworkStream base read path. The second-pass source is preserved in
`harness-revisions/second-pass-LifecycleProbes.cs`. Although new source had been copied for
`q2-corrected`, timestamp-preserving copies allowed incremental compilation to reuse the prior
22-case assembly (same 17/5); this run cannot qualify the newer adapter. The later `q2-final`
run recorded 26 cases (20 pass/6 fail) and is retained as corroboration. The forced rebuild
below removes intermediate assembly provenance ambiguity. Neither earlier Q05 timeout is
attributed to producer code.

The decisive `q2-verified-rebuild` uses `dotnet build --no-incremental`, followed by the
`q2-verified` test run with `--no-build`. This forces compilation of the final native-I/O
adapter and all 26 cases. Its results are **20 passed / 6 failed (exit 1)**. The separately compiled final
GetStream-hook run is **1 passed / 1 failed**, the latter Q20. The failed contract assertions
are retained, not weakened to make the suite green. Initial reviewer compilation produced
CS4014 (two intentional-but-unassigned task expressions) and CS0649 (unassigned hook field);
these were corrected in reviewer code. The decisive rebuild is warning-free. Full command
history and warnings are recorded rather than presenting superseded runs as final results.

## 5. Historical T10

T10 remains byte-identical to the pinned Q1 source and was independently rerun both in the
14-case Q1 suite and alone: **pass, exit 0**. It asserts completion of DisposeAsync within five
seconds, exactly one trace and no accepted handshake after a partial first connection. Its
second connection may be refused, queued or time out; `secondRefused` is assigned but never
asserted. It therefore does **not** prove strict refusal of a second TCP connect. Its useful
current contract is bounded, exception-free disposal and one trace.

The failure saved during original Q1 used the earlier diagnostic T10 name; the unchanged pinned
probe now has the name `T10_Disposal_With_Pending_Connection_Does_Not_Throw`. The saved NRE stack
still identifies the same original lifecycle defect. A pass alone cannot show that the accept
and close interleaving occurred, and the second connect's wait may give the handler time to
finish its prologue. R1's assertion that T10 now passes "deterministically" is stronger than
the test alone establishes. The reviewer-owned helper, handler and GetStream controls are
necessary to pin the formerly vulnerable boundaries. This review does not rerun the entire
pre-fix Q1 suite or edit the historical probe/evidence.

## 6. Regression results, commands and environment

Windows 10.0.26200, win-x64; .NET SDK 10.0.401; MSBuild 18.9.11; target net8.0;
test runtime .NET 8.0.31. Neo4j.Driver resolves exactly to 6.4.0 in project.assets.json.
DLL identity/hash is recorded in the independent results audit. Build/restore used existing
configured dependencies. D01–D10 remain gated and are explicitly listed as NotExecuted in
the driver TRX. Inherited BOLTSHARP_/NEO4J_ settings were removed from runner child environments;
only the dedicated authentic run received a new Q2 evidence directory. No credentials or
authentication payload bodies were persisted.

| Suite | Passed | Failed | Skipped / NotExecuted | Exit |
| --- | ---: | ---: | ---: | ---: |
| Full solution | 826 | 0 | 0 | 0 |
| Driver project | 47 | 0 | 10 | 0 |
| B2-A-Q2 | 18 | 0 | 0 | 0 |
| B2-B-Q1 | 67 | 0 | 0 | 0 |
| B2-B-Q2 | 46 | 0 | 0 | 0 |
| B2-C-Q2 | 40 | 0 | 0 | 0 |
| B0-A-TRACE-Q1 | 14 | 0 | 0 | 0 |
| T10 separately | 1 | 0 | 0 | 0 |
| Authentic handshake separately | 2 | 0 | 0 | 0 |
| R1 shutdown separately | 10 | 0 | 0 | 0 |
| Q2 decisive exact-source probes | 20 | 6 | 0 | 1 |
| Q2 final GetStream-hook probes | 1 | 1 | 0 | 1 |
| Q2 final pre-fix selected controls | 4 | 4 | 0 | 1 |

The full solution includes only the 826 unit tests. The 47 driver passes already include the
ten shutdown and two authentic cases. Q1 already includes T10. The separate reruns and Q2
controls are not added to an aggregate pass total. The table records separate executions.
The full-solution build has **zero warnings/errors**. Baseline suites and final reviewer rebuild
have no build warnings; superseded reviewer warnings are described above.

Every executed build/test command, full argument array, UTC start, duration, exit code, TRX
counts and warning lines is in [commands.json](evidence/B0-A-TRACE-Q2/commands.json). The argv
array is authoritative when rendering Windows quoting (notably the logger semicolon and
filter pipes). The reproducible commands are also encoded in the reviewer Python runners.
The independent XML/result and handshake audit is
[independent-results-audit.json](evidence/B0-A-TRACE-Q2/independent-results-audit.json).
The runner script exit itself is orchestration completion; individual test exits above remain
nonzero where assertions found defects.

## 7. Authentic experiment regression

The dedicated official-driver run passes **2/2**. Independently decoded handshake bytes and
sanitized metadata show:

| Observation | Manifest v1 | Legacy |
| --- | --- | --- |
| Client handshake | 25 bytes; magic + four proposals + selection | 20-byte magic/proposals |
| Server response | `00 00 01 FF 01 00 00 08 05 00` | `00 00 08 05` |
| Selection | `00 00 08 05 00`: 5.8, capabilities 0x0 | observed continuation after selected 5.8 |
| First framed message | B1, signature 01, 155-byte body | same |
| Next framed message | B1, signature 6A, 15-byte body | same |
| Post-handshake byte count | 178 | 178 |
| Driver outcome | ServiceUnavailableException after listener closure | same |

The second frame is observed without a server response to the first; source inspection confirms
the listener sends no post-handshake response. This supports pipelining. HELLO/LOGON names
refer to the existing signature mapping; message fields/authentication semantics are not
decoded or validated. **No HELLO/LOGON protocol compatibility is claimed.**

All four .hex files, including their textual encoding/newline bytes, are byte-identical to the
original committed producer evidence. Their SHA-256 hashes and independently decoded wire
lengths are in the audit. The manifest offer has exactly one 5.8 range and capabilities zero;
the selected zero subset is valid. The driver pin and genuine live connection are unchanged.
There is no claim of session, authentication, query, TLS, routing or B3/B4/B5 compatibility.

## 8. Findings and remaining qualifications

| Finding | Severity | Status and evidence |
| --- | --- | --- |
| TRACEQ1-F01 | MAJOR (original) | **CLOSED** for the escaping endpoint/GetStream prologue mechanism; deterministic pre/post controls plus bounded native lifecycle tests |
| TRACEQ2-F01 | **MAJOR** | **OPEN:** unexpected socket failures are classified as CLOSED/CLEAN_EOF; Q14 x3, Q15, Q20, Q23. R1 introduces direct SocketException suppression; wrapped-IOException behavior predates it |
| TRACEQ2-F02 | MINOR | **OPEN:** an ODE during framed-message read becomes post-handshake ERROR; Q21. No exception escapes and no authentic evidence is invalidated, but the claimed closure-classification correction does not cover this inner boundary |
| TRACEQ1-F02 | MINOR | Unchanged: CaptureTruncated is defensive/unreachable through the bounded negotiator; not a new correctness regression |
| TRACEQ2-O01 | OBSERVATION | Unexpected CTS initialization/prologue and generic handler exceptions remain observable as ERROR (Q16/Q11); this is correct behavior |
| TRACEQ2-O02 | OBSERVATION | T10 interleaving/refusal limitations; passing repetition is not exhaustive race proof |
| TRACEQ2-O03 | OBSERVATION | Windows/net8.0 test-only scope and unverified author independence; no owner acceptance |

TRACEQ2-F01 is a demonstrated **classification defect**, not a reproduced real network outage.
The acquisition catch accepts every SocketException and reports that the client was closed
before starting, without checking cancellation, disposal or SocketErrorCode. The outer handler
similarly accepts all SocketException/IOException as CLOSED. The observation IOException catch
labels them CLEAN_EOF. NoBufferSpaceAvailable and NetworkDown are not evidence of peer closure.
Exception type/message metadata survive in several traces, so this is not total erasure of all
diagnostics; nevertheless outcome-based fault classification treats those failures as ordinary
closure. In the framed path, inner socket diagnostics are further reduced to IOException and
CLEAN_EOF. The explicit Q2-03 requirement that unrelated unexpected faults remain ERROR is
therefore not met. Test-only scope does not waive that required qualification criterion.

Required producer action is to distinguish verified expected cancellation/disposal/peer-closure
conditions from unexpected socket failures at acquisition, negotiation and observation
boundaries, preserving ERROR for unexpected cases; then rerun the retained reviewer assertions.
For F02, cover the inner framed-read closure boundary consistently. This review changes none of
those implementations and does not prescribe an unrelated transport redesign.

Other limitations remain nonblocking implementation qualifications: disposal's five-second join
timeout can return without proving completion for an arbitrary noncooperative injected stream;
the native, cancellation-aware cases here explicitly assert handler completion. Concurrent
repeat DisposeAsync calls can return before the first disposal finishes, so the caller must
await the first invocation to establish its join. FirstTrace is not completed if no connection
ever produced a trace; its bounded waiter is the supported way to wait in that situation.
These are limits of the tested contract, not newly demonstrated native resource leaks or an
expansion into unrelated implementation scope. Provisional Bolt 5.8/PackStream 1.0 selection is
retained with the original B0-A unknowns and no milestone acceptance.

## 9. Formal disposition and owner recommendation

**B0-A-TRACE-Q2 REMEDIATION REQUIRED.**

The original TRACEQ1-F01 escape is independently closed, all requested baseline regressions
match, and authentic handshake evidence remains byte-identical. The stop condition for
SUPPORTED WITH QUALIFICATIONS is nevertheless not satisfied because TRACEQ2-F01 is a remaining
MAJOR defect in the explicitly required distinction between genuine faults and normal closure.
No BLOCKING defect was found. Owner acceptance should remain pending producer remediation
and targeted requalification of the retained failed assertions. No milestone is self-approved.

Deliverables are this report, `docs/reviews/probes/B0-A-TRACE-Q2/`, and
`docs/reviews/evidence/B0-A-TRACE-Q2/`. Historical artifacts and production code remain unchanged.
The final Q2 evidence manifest excludes only itself; a companion artifact manifest covers the
report and reviewer probe sources. Both are verified against disk and staged Git blob bytes
before the additive reviewer commit. Final commit/tree identities, clean status and worktree
count are recorded in the handoff. There are no branches, linked worktrees, history rewrites,
pushes, fixes, or B3/B4/B5 implementation in this assignment.
