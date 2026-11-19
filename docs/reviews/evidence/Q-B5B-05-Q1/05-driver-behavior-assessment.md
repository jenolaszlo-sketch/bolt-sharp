# Official-Driver Behavior Assessment — Q-B5B-05-Q1 (Independent)

Independent reproduction of D-01..D-04 (plus a reviewer-added D-05) with the pinned
Neo4j.Driver 6.4.0, driven against the production BoltSharp listener through a **reviewer-owned
recording proxy** (byte-for-byte relay, per-connection identity, message-level deframing, LOGON
redacted). Observations only; no public driver RESET API is invented or assumed. Cross-platform:
identically reproduced on Windows and Ubuntu WSL2 (`windows/`, `wsl2/` in this package).

Driver establishment observed on the wire (all driver scenarios): C2S
`6060B017 000001FF 00080805 00020404 00000003`; S2C manifest offer `000001FF 01 00000805 00`;
C2S selection `0000080500`; server reports `BoltHandshakeCompleted v5.8 mode=ManifestV1`.

## D-01 — the pipelined RUN/PULL refusal (the Q-B5B-05 shape)

- Exception: `Neo4j.Driver.ProtocolException`, `code=Neo.ClientError.Request.Invalid`,
  `gqlStatus=50N42`, `gqlClassification=UNKNOWN`, `IsRetriable=False`, message "An unknown error
  occurred".
- Wire (connection #1): `HELLO → SUCCESS(server, connection_id, hints)`, `LOGON → SUCCESS`,
  `RUN → FAILURE(gql_status 50N42, neo4j_code Neo.ClientError.Request.Invalid)`,
  `PULL → IGNORED B0 7E` (byte-exact).
- Driver then closes the physical connection without GOODBYE (server: `accepted=1 completed=1
  failed=0 messagesProcessed=4`; trace: RUN→Failed, PULL→Failed (IGNORED), completed).

## D-02 — recovery disposition after the recoverable failure

Same driver instance, second session, valid query:

- `physical connections observed by the proxy: 2`; `recovery on the SAME physical connection:
  False`.
- Connection #1 ends at `IGNORED B0 7E`: no RESET, no GOODBYE — the driver destroys it.
- Connection #2 performs a **new handshake and a new authentication** (`HELLO`, `LOGON`), then
  `RUN → SUCCESS(fields)`, `PULL → RECORD 42`, `SUCCESS`, then release `RESET → SUCCESS` and
  `GOODBYE`.
- Server counters: `accepted=2 completed=2 failed=0`; per-connection providers:
  `#1 prepare=1 success=0 refused=1`, `#2 prepare=1 success=1 refused=0`.
- Conclusion: the driver **opens a new connection**; it does not send RESET on the original and
  does not reuse the original connection.

## D-03 — alternative failure classification + retry configuration

Provider fault on the first connection (recoverable `50N00 Neo.DatabaseError.General.UnknownError`),
driver configured with `WithMaxTransactionRetryTime(5s)`:

- Exception: `Neo4j.Driver.DatabaseException`, `code=Neo.DatabaseError.General.UnknownError`,
  `gqlStatus=50N00`, `gqlClassification=UNKNOWN`, `IsRetriable=False`.
- Wire: `HELLO/LOGON → SUCCESS`, `RUN → FAILURE(50N00)`, `PULL → IGNORED B0 7E`, then
  `GOODBYE` on the **same** physical connection — no RESET after the failure, no reconnect, no
  automatic retry.
- Conclusion: configuring a retry time does not make the driver treat the failure as retriable;
  eligibility is decided by the error classification, not by the policy.

## D-04 — normal pooled-connection reuse (distinct from post-failure recovery)

Three sequential sessions of one driver instance:

- Variant A (results fully consumed): **one** physical connection for all three sessions;
  `HELLO`, `LOGON`, then three `RUN n=1000 / PULL n=1000 / SUCCESS / RECORD 42 / SUCCESS` cycles,
  each session followed by `RESET B0 0F → SUCCESS B1 70 A0` on the same connection (3 RESETs);
  server-side 3× `sig=0x0F responses=1 state=Ready`; one provider instance.
- Variant B (cursors opened and abandoned — dirty sessions): the same one physical connection;
  6 further RESETs server-side, each answered `SUCCESS`; `messagesProcessed=20`.
- Conclusion: the driver **does** perform same-connection RESET→SUCCESS as part of its pooled
  connection protocol; that capability is not invoked after a recoverable FAILURE.

## D-05 — second run on the same (failed) session (reviewer addition)

- First run: `ProtocolException` as in D-01.
- Second run on the **same session object** returns the value `42` — but on a **new physical
  connection** with a new handshake and a new authentication. Connection #1 was abandoned at
  `IGNORED B0 7E` with no RESET.
- Conclusion: no client-side usage pattern found in this test drives the official driver to
  RESET-recover the failed connection; "recovery" transparently means a new physical connection.

## Classification summary (observed, both platforms)

| Failure | Approaching message | Driver exception | GQL status | Retriable | Driver action |
|---|---|---|---|---|---|
| `50N42 Neo.ClientError.Request.Invalid` | auto-commit RUN (+ pipelined PULL) | `ProtocolException` | `50N42` | `False` | destroys connection; recovery on a new physical connection |
| `50N00 Neo.DatabaseError.General.UnknownError` | auto-commit RUN (+ pipelined PULL) | `DatabaseException` | `50N00` | `False` | no RESET; GOODBYE on the same connection (with `WithMaxTransactionRetryTime(5s)` configured) |

## Relationship to the producer's D-observations

The independent reproduction matches the producer package on every material point: D-01
classification and wire bytes; D-02 new-connection disposition; D-03 classification and GOODBYE;
D-04 pooled same-connection RESET→SUCCESS. Two producer-documentation precision issues are
recorded as findings (see `10-findings-register.md`, F06/F07): the producer's assessment text
over-generalizes acquisition RESETs, and its D-03 artifact header wording contradicts its own
recorded classification.