# Q-B5B-09-PLAIN-R1 — Scoped terminal deadline remediation

**IMPLEMENTED — READY FOR INDEPENDENT REQUALIFICATION** · 2026-11-17, Asia/Manila

R1 ends the write-specific cancellation scope when terminal write and flush succeed, before reporting MessageProcessed. The overall 250 ms deadline remains active during publication and cleanup. The seven inherited controls still detect their intended faults, and reintroducing F01 makes the new regression fail on Windows and Ubuntu WSL2. These are producer results. The original [Q1 disposition](Q-B5B-09-PLAIN-Q1-qualification.md) remains authoritative pending independent requalification.

## Entry and binding evidence

The canonical repository was `D:/projects/bolt-sharp` on clean `main`, 14 ahead and zero behind local `origin/main`, with one worktree and no stashes. Entry HEAD was `9dd2fe1b9b6f8ab2880e3fe50b8ef9ac2d43ae9c`, tree `f0f1d38dbe648ee36c07a490f6217b47a179dd16`. The direct parent chain is Q1 → producer evidence `5d27b39c32a2d1e56a3d8ef7ce116a36445427cf` → plaintext implementation `20fbc461c0257620db28fb78a463218b242455bc` → typed linger `dac52d55ade0a9ae703b801c8a4066e8aef2bd58` → owner baseline `fac10f6c5ece37dbc3f38c9c831c668d49a3d290`. The owner tree remained `fceee363ebcc72c99263d2bfe5e210a9c49cea4d`. The Q1 verifier rehashed eight blobs and 137 members. The producer verifier rehashed eight blobs and 59 members and replayed the prior owner evidence. [Owner decision](../decisions/Q-B5B-09-OWNER-01-production-remediation-scope-decision.md), [implementation specification](../decisions/Q-B5B-09-PLAIN-IMPL-01-implementation-specification.md), [Q1 findings](evidence/Q-B5B-09-PLAIN-Q1/findings.md).

## Original failure and correction

A test-only probe ran against the pristine production source at `20fbc461`. Both peers read the exact terminal FAILURE after write and flush. With WriteTimeout 80 ms and a 140 ms inline observer, Windows reported `write-deadline` at 149.58 ms and WSL2 at 145.63 ms. Both had three MessageProcessed events, no drain admission and one failed connection. The test assertions failed as intended. [Baseline logs and reproduction](evidence/Q-B5B-09-PLAIN-R1/README.md).

In the original code, the writer delegate included diagnostic publication and the shorter timer could abort the socket until the delegate returned. R1 passes two callbacks to the plaintext helper. The first frames, writes and flushes exactly one terminal response under a scoped cancellation token. The second publishes MessageProcessed after the scope and its registrations have settled. The shorter timer cancels the scoped I/O token and cannot abort a socket during subsequent observer execution. A first-signal latch identifies the cause of write cancellation when timeout and owner signals race. Successful write and flush mark the post-write phase; a racing short timer may cancel the now-retired token, with no authority over later phases. Pending writes still observe cancellation and remain directly awaited.

The overall timer, listener stop and connection-owner signal still interrupt the owner and close the socket during pending I/O or synchronous diagnostic processing. The owner checks monotonic remaining time again before attempting admission. Expiry after a successful flush is a completed post-write closure. The admitted drain gets the smaller of 75 ms and remaining total time. The established zero-wait permit policy, disabled-linger setter, 8 KiB read cap, 2 KiB scratch, discard-only reads and disposal-before-permit-release ordering remain in force. The reader retires on the terminal branch, and the existing session/codec produce the single exact refusal and Defunct state.

A synchronous observer can hold the connection task and its admission slot until the callback returns, including after the overall timer closes the underlying socket. This limitation follows from the existing synchronous observer contract. The absolute deadline bounds cancellation initiation and transport ownership subject to runtime scheduling and socket completion; it cannot preempt arbitrary managed observer or provider code. Holding the observer past expiry in the regression showed the handle already closed while the observer still held the task. No public observer contract changed.

## Findings and scope

| Q1 finding | Proposed remediation and coverage | Remaining boundary |
| --- | --- | --- |
| F01 · R05/R10/R11 | The write timer and cause are scoped to successful write/flush or actual interrupted write. R1-01, R1-02 and R1-07 check exact peer receipt, Defunct publication and completion/failure counters. R1-03 through R1-06 check real pending I/O. NC-08 restores the original ownership error and trips the delay oracle on both platforms. | Overall deadlines cannot force synchronous user code to return. |
| F02 · R11 | The helper now publishes correlated `write-pending` with phase and actual monotonic elapsed time, distinct `write-failed`, `drain-admitted` and `drain-end` observations, and the enriched end reasons already used by `disposed`. The failure and drain-end timestamps precede ownership cleanup, and disposal confirmation is timestamped before any final diagnostic subscribers run. Pending-write and successful-drain regressions assert the event metadata. | Disposal confirmation describes the helper cleanup checkpoint. For cancellation during synchronous observer execution, the OS handle may already have closed earlier. Diagnostic payloads exclude drained bytes and peer data. |
| F03 · R02/R12 | Removed nullable timeout and plaintext pending-write instrumentation from the shared writer. TLS and other ordinary paths use their original linked timeout, write and flush sequencing. The shared change is extraction of the existing pure response framing into a private method used by both writers. R1-10 and the incorrect-mode control establish TLS isolation. Restored the original lifecycle comment and retained the typed setter byte sequence. | TLS graceful-close remediation and its outstanding qualification remain deferred. |

Three production files and the existing plaintext terminal test file comprise the implementation delta. Public APIs, dependencies, options, owner decisions, existing TS1 test files and historical evidence remain protected. TLS continues to negotiate and exchange Bolt through SslStream and to use the established shutdown and disposal behavior. The typed-linger correction remains independently traceable; its setter and portable startup tests still pass.

## Regression matrix

| Case | Production-path oracle |
| --- | --- |
| R1-01 · 80 ms write and 140 ms observer | Exact FAILURE and Defunct, successful drain/EOF, one completed connection, zero failures and reusable permit. |
| R1-02 · Observer consumes the absolute budget | Socket closes while observer remains held, `post-write / total-deadline`, zero drain admissions, write-failure events or failed connections. |
| R1-03 · 150 ms write-specific timeout | A genuine pending write after the earlier PULL gives `write / write-deadline` before total expiry, one failure, closed handle and no drain. |
| R1-04 · Absolute timeout while writing | Same real pending-write setup with a longer write setting gives `write / total-deadline` and no drain. |
| R1-05 and R1-06 · Listener and owner interruption during write | Each cancels a distinct real pending terminal write and releases the handle before deadline expiry. |
| R1-07 · Listener and owner cancellation after flush | The publication gate establishes successful write/flush and holds the observer. Each independent cancellation closes the socket and completes the connection under its actual cause. |
| R1-08 · Observer consumes about 200 ms, then stalled drain | Drain enters with less than its normal 75 ms, exits on the original total deadline and returns its permit. |
| R1-09 · Throwing observer | Exact FAILURE, successful graceful drain and balanced counters after the contained exception. |
| R1-10 · TLS neutrality | Existing TLS and driver suites, eligible TLS refusal with zero plaintext events, typed-linger coverage and controlled incorrect-mode detection. |

Windows and WSL2 each passed 1,257 core tests, 43 official-driver tests and three focused runs of 57 tests, with zero skips. The preceding qualified core count was 1,250, plus seven new theory cases in R1. The baseline reproduction ran one deliberately discriminating case per platform. Production, solution and driver builds and scoped format passed. The only analyzer warnings were the two existing xUnit2012 warnings. [Campaign records and controls](evidence/Q-B5B-09-PLAIN-R1/README.md).

In the final full-core run, the original 140 ms observer case drained and completed after 153.32 ms on Windows and 142.81 ms on WSL2. A genuinely pending write exceeded the 150 ms short timer at 157.28 ms and 151.50 ms, respectively. Pending owner cancellation settled at 32.61 ms and 24.20 ms, and listener stop at 38.30 ms and 23.17 ms. The post-flush total-expiry case completed at 262.16 ms and 255.12 ms with no failed connection. These elapsed times run from lifecycle entry to the disposal checkpoint and include scheduler and I/O settling time.

B5-B-TS1 and Q-B5B-03 remain CLOSED. Q-B5B-04, Q-B5B-05 and Q-B5B-09 remain OPEN. TLS graceful-close remediation remains DEFERRED. Native Linux and representative production capacity remain OUTSTANDING, and all other inherited qualifications remain in force. The next commission is **Q-B5B-09-PLAIN-Q2 — Independent R1 Requalification** in a fresh reviewer session that performed neither implementation nor producer verification.
