# Protocol Recovery Traces — Q-B5B-05-Q1

Commission §6 expects the exact protocol messages and state transitions, with byte-exact markers
where the contract requires them. All traces in this package were produced by the reviewer-owned
harness (raw client, no BoltSharp codec involvement) and, for the driver scenarios, by the
reviewer-owned recording proxy. Both platforms: `windows/`, `wsl2/`.

## 1. Required markers, as independently verified

| Marker | Bytes | Verified in |
|---|---|---|
| Recoverable FAILURE (`50N42`) | `B1 7F` + `gql_status` field `"50N42"` + `neo4j_code Neo.ClientError.Request.Invalid` | every R-01/02/03/05/06 cycle; D-01/D-02 wire |
| IGNORED | exactly `B0 7E` | every R-01/02/03/05/06 cycle; D-01/D-02/D-03 wire |
| RESET | exactly `B0 0F` | every cycle; D-04 pooling wire |
| RESET SUCCESS | exactly `B1 70 A0` | every cycle; D-04 pooling wire |
| GOODBYE | exactly `B0 02` | R-01; driver closes |
| Terminal refusal (`08N06`) | `B1 7F` + `"08N06"` | NC-04 |
| Terminal retirement failure (`50N00`) | `B1 7F` + `"50N00"` | NC-03; D-03 wire |

Byte comparisons are exact-equality assertions in the harness, not substring searches
(`RawBoltClient` records the exact payload of every message in both directions).

## 2. Canonical raw sequence — R-03 (the exact Q-B5B-05 shape, one client write)

From `windows/r-03-recovery.txt` (RUN and PULL pipelined in a single TCP write):

```text
server: accepted=1 active=0 completed=1 failed=0 messagesProcessed=10
connection ids: accepted=[conn-0001] messageProcessed=[conn-0001]
RESET messages observed server-side: 1
client local port: <one port for the whole sequence>
provider instances: 1 (prepare=3 success=2 refused=1)

wire (payload hex, abbreviated direction):
  C2S HELLO …user_agent…bolt_agent…                → S2C SUCCESS B170A3…server…connection_id…
  C2S LOGON …basic credentials…                    → S2C SUCCESS B170A0
  C2S RUN "RETURN 42 AS answer"                    → S2C SUCCESS B170A1…fields…
  C2S PULL {n=-1}                                  → S2C RECORD B171912A   (integer 42)
                                                      S2C SUCCESS B170A0
  C2S RUN "MATCH (n) DETACH DELETE n" + C2S PULL   → S2C FAILURE B17FA4…gql_status "50N42"…
                                                      S2C IGNORED B07E
  C2S RESET                                        → S2C SUCCESS B170A0
  C2S RUN "RETURN 42 AS answer"                    → S2C SUCCESS (fields)
  C2S PULL {n=-1}                                  → S2C RECORD B171912A , S2C SUCCESS B170A0
  C2S GOODBYE                                      → (no response; connection completes)

verdict mid : accepted=1 active=1 completed=0 failed=0 providers=1 SAME_PHYSICAL_CONNECTION=True
verdict post: accepted=1 active=0 completed=1 failed=0 providers=1 SAME_PHYSICAL_CONNECTION=True
```

The full byte-exact transcripts for every scenario are stored in `windows/` and `wsl2/`
(`client transcript:` line in each `r-*.txt`; `proxy transcripts:` per connection in `d0*.txt`).
The repeated-cycle runs (R-02 ×3, R-06 ×10) record 3 and 10 server-side `sig=0x0F responses=1
state=Ready` events respectively, on one connection id, with one provider instance.

## 3. Server-side state-transition evidence (per scenario)

Each `r-*.txt` carries the complete `MessageProcessed` trace with post-transition states, e.g.
R-01: `HELLO→Authentication, LOGON→Ready, RUN→Streaming, PULL→Ready, RUN→Failed, PULL→Failed
(IGNORED), RESET→Ready, RUN→Streaming, PULL→Ready, GOODBYE→Defunct` — exactly the transitions
required by the session contract, with the IGNORED and RESET responses matching the byte-exact
markers above.

## 4. Handshake bytes (observed, for completeness)

- Raw client: magic `60 60 B0 17`, proposal `00 00 08 05` (+ zero fill) → server selects
  `00 00 08 05` (server trace: `v5.8 mode=Legacy`).
- Official driver: magic `60 60 B0 17`, proposals `000001FF 00080805 00020404 00000003`; server
  manifest offer `000001FF 01 00000805 00`; driver selection `0000080500` (server trace: `v5.8
  mode=ManifestV1`).

LOGON payloads are never stored in this package: the raw client records LOGON requests as
`REDACTED(LOGON)` and the proxy redacts them before recording. Greppable verification over the
committed package finds no credential material (only prose describing the message type).