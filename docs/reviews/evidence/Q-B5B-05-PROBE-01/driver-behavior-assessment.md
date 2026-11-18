# Official-Driver Behavior Assessment — Q-B5B-05-PROBE-01

Producer: [`Q-B5B-05-PROBE-01-producer-report.md`](../../../reviews/Q-B5B-05-PROBE-01-producer-report.md)

Every statement below is an observation, not an inference. The genuine, pinned Neo4j.Driver
6.4.0 is driven against the real BoltSharp listener through the producer-owned byte-exact
recording proxy, so both the wire sequence and the physical connection identity are visible.
No public driver RESET API is invented or assumed.

## 1. How the official driver actually initiates RESET

The driver emits the zero-arity `RESET` message on its own. The observed encoding is the
byte-exact `B0 0F` (`C2S RESET B00F`), answered by the server with `SUCCESS B1 70 A0`.

Two distinct, separately observable driver uses of RESET exist:

1. **Acquisition-time RESET.** On every physical connection the driver opens, immediately
   after HELLO/LOGON it sends **two** RESETs before any work message
   (`C2S RESET | C2S RESET | S2C SUCCESS | S2C SUCCESS`), and again at the close of a session
   on a pooled connection. Observed in D-01, D-02, D-03 and D-04.
2. **Pooled-connection reuse RESET.** Between sequential sessions of one driver instance the
   driver sends `RESET` and then the work message on the **same physical connection**, with no
   interlacing handshake or authentication. Observed in D-04: three sequential sessions served
   by **one** physical connection, each session preceded by `RESET → SUCCESS`.

So the driver *does* perform same-connection RESET recovery — as part of its normal
pooled-connection protocol.

## 2. What the driver does after a recoverable FAILURE

**It does not RESET the failed connection. It closes the physical connection instead.**

Observed in D-02 (the exact Q-B5B-05 shape):

- Physical connection #1: `HELLO → SUCCESS`, `LOGON → SUCCESS`, `RESET, RESET → SUCCESS, SUCCESS`,
  `RUN` (unsupported) `→ FAILURE 50N42`, `PULL → IGNORED B0 7E`. Then the driver stops using it.
- The subsequent operation runs on **physical connection #2**, which performs a **new
  handshake** (`HELLO → SUCCESS`) and a **new authentication** (`LOGON → SUCCESS`) before its
  own `RUN → SUCCESS`/`PULL → RECORD + SUCCESS`, and finally `RESET → SUCCESS` and `GOODBYE`.
- Server counters: `accepted=2 completed=2 failed=0 active=0 messagesProcessed=12`.
- Per-connection provider counters: `#1 prepare=1 success=0` (the refusal only) and
  `#2 prepare=1 success=1` (the recovery query only) — **two physical connections**.
- `RESET` observed server-side: 3 in total, none of them a recovery RESET after the failure.

The same pattern holds for a **retriable-class** database error (D-03): arming the provider's
`Prepare` to throw produces the recoverable `50N00 Neo.DatabaseError.General.UnknownError`
FAILURE followed by `IGNORED B0 7E`. The driver classifies it as
`DatabaseException code=Neo.DatabaseError.General.UnknownError gqlStatus=50N00
gqlClassification=UNKNOWN retriable=False`, sends **no** subsequent RESET, and goes straight
to `GOODBYE` on the same physical connection. Even with
`WithMaxTransactionRetryTime(TimeSpan.FromSeconds(5))` configured, no automatic retry and no
RESET recovery is performed in this scenario.

## 3. Driver classification summary (observed, verbatim)

| Failure | Approaching message | Driver exception | `IsRetriable` | Driver action after the failure |
|---|---|---|---|---|
| `50N42 Neo.ClientError.Request.Invalid` | auto-commit `RUN` (+ pipelined `PULL`) | `ProtocolException code=Neo.ClientError.Request.Invalid gqlStatus=50N42 gqlClassification=UNKNOWN retriable=False` | `False` | destroys the connection; recovery on a fresh physical connection with a new handshake and new authentication |
| `50N00 Neo.DatabaseError.General.UnknownError` (provider `Prepare` throws) | auto-commit `RUN` (+ pipelined `PULL`) | `DatabaseException code=Neo.DatabaseError.General.UnknownError gqlStatus=50N00 gqlClassification=UNKNOWN retriable=False` | `False` | no RESET; `GOODBYE` on the same connection |

## 4. Evidence classification (commission §7 requirement)

| Class | Artifacts | What it can and cannot establish |
|---|---|---|
| **Official-driver recovery evidence** | D-01, D-02, D-03, D-04 | Establishes the genuine driver's classification, the actual wire sequence and the physical connection disposition. It cannot establish server-side RESET handling beyond what the driver chooses to exercise. |
| **Raw protocol recovery evidence** | P-01, P-02, P-03 | Establishes that the wire protocol permits the full sequence on one connection. **It is never presented as official-driver qualification.** |
| **Server-side state-machine evidence** | All P-01..P-05 and NC-01..NC-04 | Establishes the connection ids, the counters and the session state transitions reported by the production transport, including over TLS where a passive tap is blind. |

## 5. Conclusion of this assessment

Same-connection RESET recovery exists in the official Neo4j.Driver 6.4.0 as part of its
pooled-connection protocol, and the BoltSharp server fully supports it. What the driver does
**not** do is RESET a connection that has just received a recoverable FAILURE: it closes the
physical connection instead, and the subsequent operation runs on a new physical connection
with a new handshake and a new authentication. That is the operational content of the retained
`Q-B5B-05` qualification, and it is a **driver behavior limitation combined with the bounded
error classification** — not a production session-state defect, which P-01..P-05 disprove.