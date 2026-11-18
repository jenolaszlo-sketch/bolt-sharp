# Requirements Matrix — Q-B5B-05-PROBE-01

Producer: [`Q-B5B-05-PROBE-01-producer-report.md`](../../../reviews/Q-B5B-05-PROBE-01-producer-report.md)

Every requirement below is derived from the binding Q-B5B-05 qualification text and the
applicable owner contracts. "Satisfied" means the producer probe observed it and its own
oracle detected the intended behavior; it is **not** an independent qualification.

## A. Protocol recovery sequence (commission §5)

| Step | Requirement | Artifact | Result |
|---|---|---|---|
| 1 | Establish and authenticate a Bolt connection | P-01/P-02/P-03/P-04/P-05, D-01..D-04 | Observed (legacy 5.8 handshake + Manifest v1 in the driver path; HELLO SUCCESS with `server`/`connection_id`/`hints`; LOGON SUCCESS to READY) |
| 2 | Execute a valid initial operation | P-01 (`RETURN 42 AS answer` → fields → RECORD `R2 42 1A` → SUCCESS) | Observed |
| 3 | Trigger a recoverable FAILURE | P-01/P-02/P-03/P-05: provider refusal → `50N42 Neo.ClientError.Request.Invalid`; connection left open | Observed |
| 4 | Confirm the applicable failed session state | Server `MessageProcessed` detail `state=Failed`; pipelined work answered with the byte-exact zero-field `IGNORED B0 7E` | Observed |
| 5 | Send or observe an official-driver-generated RESET | Raw probes: the probe sends `RESET B0 0F`. Driver probes: the driver emits `RESET B0 0F` on its own (D-01, D-02, D-03, D-04) | Observed both ways |
| 6 | Confirm the expected successful RESET response | `SUCCESS B1 70 A0` (empty metadata) | Observed |
| 7 | Confirm the applicable recovered session state | Server detail `state=Ready` after RESET | Observed |
| 8 | Execute a subsequent valid operation | `RETURN 42 AS answer` → `SUCCESS {fields=[answer]}` | Observed |
| 9 | Confirm successful results | `RECORD B1 71 91 2A` (integer 42) then `SUCCESS B1 70 A0`; driver cursor value `42` | Observed |
| 10 | Confirm orderly connection lifecycle completion | `GOODBYE B0 02`, no further response, server `ConnectionsCompleted=1`, `ConnectionsFailed=0`, `ConnectionsActive=0` | Observed |

## B. Same-physical-connection proof (commission §6 — the principal acceptance gate)

| # | Requirement | Oracle | Result |
|---|---|---|---|
| 1 | Demonstrate failure, RESET and subsequent successful operation on the same physical connection | `SameConnectionVerdict.SamePhysicalConnection` | **Satisfied** in P-01, P-02, P-03, P-04 |
| 2 | Do not rely solely on driver-instance reuse, logical driver sessions, query success, connection-pool membership or driver retry success | None of these is used as an oracle. Pooling is observed as a *driver behavior* case (D-04), never as connection identity | Satisfied |
| 3 | Use independent, correlated evidence | Five independent lines, below | Satisfied |
| 4 | Establish that there was no intervening reconnection, replacement connection, additional handshake, additional authentication, or a retry that masks failed RESET recovery | Counters + connection ids + client port + provider identity + transcript ordering | Satisfied |

### The five independent evidence lines

| Line | Evidence | What it proves | What it rules out |
|---|---|---|---|
| 1. Server connection identity | The listener mints one monotonic `conn-NNNN` per accepted socket and records it on every diagnostics event. Exactly one accepted id, and every `MessageProcessed` event of the sequence carries it | All protocol events of the sequence went through one accepted socket | A second accept (reconnection or replacement) |
| 2. Server connection counters | `ConnectionsAccepted=1`, `ConnectionsActive=1` during the sequence, `ConnectionsFailed=0`, `ConnectionsCompleted=1` after GOODBYE, `MessagesProcessed` matching the message count | One connection, never failed, ended orderly | A transport fault or an extra connection |
| 3. Client socket identity | The client's local ephemeral port is unchanged and, before the orderly GOODBYE, the socket was never observed closed | The same client socket carried the whole sequence | A replacement socket (whose port necessarily differs) |
| 4. Per-connection provider identity | Production creates exactly one provider instance per negotiated connection, so exactly one instance ↔ exactly one physical connection | One negotiated connection | A re-handshaked or re-authenticated connection |
| 5. Proxy transcript ordering | The producer-owned byte-exact proxy assigned one identity and recorded, in order, HELLO, LOGON, RESET(s), RUN, PULL, FAILURE, IGNORED, RESET, RUN, PULL, GOODBYE — all on the one connection | The observed byte sequence, in arrival order, on one physical socket | A reordering across connections or an extra handshake |

A replacement connection, an additional handshake, an additional authentication, or a driver
retry over a fresh socket each breaks at least one line.

## C. Official-driver integration (commission §7)

| # | Requirement | Result |
|---|---|---|
| 1 | Use the existing Neo4j.Driver 6.4.0 harness | Satisfied — the pinned package is referenced by the probe project; the driver is constructed through its documented public API only |
| 2 | Determine how the driver actually initiates RESET; do not invent or assume a public driver RESET API | Satisfied by observation: the driver emits `RESET B0 0F` on its own when acquiring a pooled, previously-used connection (D-01, D-02, D-03, D-04) |
| 3 | Where the driver automatically performs recovery, observe and verify the actual wire or server-side protocol sequence | Satisfied — the proxy transcript records the exact byte sequence and the server-side `sig=0x0F` count |
| 4 | Where direct protocol control is necessary, use the existing raw Bolt probe as supplemental evidence | Satisfied — P-01..P-05 are the raw-protocol supplemental evidence and are explicitly **not** presented as official-driver qualification |
| 5 | Distinguish official-driver evidence, raw protocol evidence and server-side state-machine evidence | Satisfied — see the assessment document; each artifact is labeled |

## D. Experimental matrix (commission §8)

| Requirement | Coverage |
|---|---|
| Plaintext loopback | P-01, P-02, P-03, P-05, NC-01, NC-02, NC-04 |
| Existing TLS loopback arrangement | P-04 |
| Recoverable failure followed by successful RESET | P-01, P-02, P-03, P-04 |
| Successful operation after RESET | P-01, P-02, P-03, P-04 |
| Repeated recoverable failure and RESET on the same connection | P-02 (three cycles) |
| Connection pooling interactions | D-04 (three sequential sessions), D-02 (post-failure acquisition) |
| An unaffected concurrent client | P-05 |
| Record exact response contents, session transitions and connection identities | Evidence files record the response hex, the server-side state transitions and the connection ids |
| No expansion into general transaction, driver or transport qualification | Satisfied — BEGIN appears only in NC-04 as the *inadmissible terminal failure* case; no transaction or routing behavior is qualified |

## E. Negative controls (commission §9)

| Control | Intended defect | Detecting oracle | Result |
|---|---|---|---|
| NC-01 | A subsequent successful operation on a different physical connection | `ConnectionsAccepted == 2`, two distinct connection ids, two provider instances, two distinct client local ports | **Detected** |
| NC-02 | Omitting RESET after the recoverable failure | The subsequent RUN is answered with the byte-exact `IGNORED B0 7E` and the session stays failed; recovery only follows the RESET itself | **Detected** |
| NC-03 | An incorrect recovery response or state (terminal 50N00 instead of the expected `B1 70 A0`) | The RESET response is `FAILURE` with `50N00` and never `B170A0`; no subsequent message is processed; `DisposeFailures == 1` | **Detected** |
| NC-04 | A terminal failure substituted for a recoverable one | `08N06` FAILURE, then RESET produces **no response at all** (the session is Defunct and processes nothing) and no subsequent operation runs | **Detected** |

Mutations only in isolated temporary probe-owned code: NC-03's mutation lives in a
probe-owned provider inside the probe test process. No canonical production behavior is
modified.

## F. Platform execution (commission §10)

| Platform | Configuration |
|---|---|
| Windows | Microsoft Windows NT 10.0.26200.0, .NET SDK 10.0.401, `Microsoft.NETCore.App 8.0.31` |
| Ubuntu WSL2 | `6.6.114.1-microsoft-standard-WSL2`, Ubuntu 26.04.1 LTS, .NET SDK 10.0.114 with `DOTNET_ROLL_FORWARD=Major` (only .NET 10 is installed on that image) |

Native Linux qualification remains an inherited release gate and is not claimed.

## G. Regression verification (commission §11)

| Suite | Windows | WSL2 |
|---|---|---|
| Producer probe suite (13 probes) | 13 passed, 0 failed, 0 skipped | 13 passed, 0 failed, 0 skipped |
| Core solution (`BoltSharp.slnx`: 1,257 tests) | 1,257 passed, 0 failed, 0 skipped | 1,257 passed, 0 failed, 0 skipped |
| Official-driver interoperability (43 tests) | 43 passed, 0 failed, 0 skipped | 43 passed, 0 failed, 0 skipped |

The core suite contains the session-recovery, RESET-handling, multi-client accounting and the
plaintext and TLS transport cases, so the relevant surfaces are covered by one full run per
platform without repeating unrelated graceful-close investigations.