# Same-Physical-Connection Identity Matrix — Q-B5B-05-Q1

Commission §7 is the principal qualification gate: the entire recovery sequence must occur on one
physical connection. Reviewer-owned harness, independent of the producer probe.
Per-platform scenario evidence: `windows/`, `wsl2/` (this package). Each scenario records the
mid-sequence and post-GOODBYE verdicts; the exact windows R-01 extract:

Verbatim from `windows/r-01-recovery.txt` (final reviewer run; ports differ per run by nature,
the invariants are what the matrix asserts):

```text
verdict mid:  phase=mid; acceptedIds=[conn-0001]; msgIds=[conn-0001]; accepted=1 active=1
              completed=0 failed=0 messages=9; providers=1; proxyConnections=0;
              clientPort=2951 socketOpen=True; SAME_PHYSICAL_CONNECTION=True
verdict post: phase=post; acceptedIds=[conn-0001]; msgIds=[conn-0001]; accepted=1 active=0
              completed=1 failed=0 messages=10; providers=1; clientPort=2951
              socketOpen=False; SAME_PHYSICAL_CONNECTION=True
```

## 1. The identity lines (each independently observable)

| # | Line | Oracle | Replaces/kills |
|---|---|---|---|
| 1 | Server connection identity | exactly one accepted `conn-NNNN`; every `MessageProcessed` event of the sequence carries it | a second accept (reconnect or replacement) |
| 2 | Server connection counters | `accepted=1 active=1 completed=0 failed=0` mid; `completed=1 failed=0` post; `messagesProcessed` matches the message count | a transport fault, an extra connection, a fabricated recovery |
| 3 | Client socket identity | client local ephemeral port unchanged from authentication through recovered operation; socket never observed closed | a replacement socket (its port necessarily differs) |
| 4 | Per-connection provider identity | production invokes the provider factory exactly once per negotiated connection; one instance ⇔ one physical connection | a re-handshaked or re-authenticated connection |
| 5 | Protocol transcript ordering | reviewer-owned raw client transcript: every sent/received message, byte-exact, in arrival order, on the one socket | a reordering, an extra handshake or an extra authentication |
| 6 | Handshake + authentication count | exactly one `BoltHandshakeCompleted` per accepted connection and exactly one LOGON per HELLO | silent re-handshake / silent re-authentication |

Lines 1–4 are server-side facts independent of any client claim; lines 5–6 are client/wire facts
independent of any server claim. A driver retry, a pool reconnection or a session-level polite
"success" cannot satisfy all six simultaneously.

## 2. Results by scenario (both platforms; identical verdicts)

| Scenario | Cycles | Platform verdicts | Identity lines exercised |
|---|---|---|---|
| R-01 plaintext recovery (the commission 15-step flow, byte-exact) | 1 | `SAME_PHYSICAL_CONNECTION=True` mid + post | 1,2,3,4,5 + handshake/auth×1 |
| R-02 plaintext repeated cycles | 3 | `True` mid + post; 3 RESET server-side | + counter `messagesProcessed` carried through 3 cycles |
| R-03 pipelined RUN+PULL (exact Q-B5B-05 shape, one client write) | 1 | `True` mid + post | + pipelining on one socket |
| R-04 TLS loopback | 1 | `True` mid + post; `TlsHandshakeFailures=0`; server-side evidence only (a passive tap is blind inside TLS) | 1,2,4 + handshake/auth×1 |
| R-05 concurrent clients | 1 each | two connections, each recovered and completed independently, `failed=0` | 1,2,4 per connection |
| R-06 bounded repeated cycles | 10 | `True` mid + post; 10 RESET server-side | + repeated-recovery bound |
| NC-01 replacement connection | — | `False` (`accepted=2`, two ids, two provider instances, ports `2964 ≠ 2965`, evidence: `windows/nc01-replacement-connection.txt`) | oracle REJECTS a new connection |
| D-02 official driver, post-failure recovery | — | 2 physical connections (see driver assessment) | driver path observed through the reviewer proxy |
| D-04 official driver, pooled reuse | 3+3 sessions | 1 physical connection, 3 (+3) same-connection RESET→SUCCESS | driver path observed through the reviewer proxy |

## 3. What the matrix rules out

Verified absent in every accepted (SameConnectionVerdict=True) scenario:

- replacement socket (line 3: port unchanged, socket open mid-sequence);
- additional handshake (lines 1/6: one `BoltHandshakeCompleted` per accepted connection; raw
  transcript carries exactly one HELLO);
- reauthentication (line 6: one LOGON per HELLO; server trace shows exactly one `sig=0x6A`);
- silent retry on another connection (line 1: exactly one accepted id carries every
  `MessageProcessed` event);
- new provider instance (line 4: one factory invocation per negotiated connection).

Conversely, the oracle demonstrably **rejects** each false-positive shape (NC-01 replacement,
NC-02 missing RESET, NC-03 non-SUCCESS recovery response, NC-04 terminal failure substitution) —
see `06-negative-controls.md`.

## 4. Insufficient evidence, as required

None of the following was used as identity evidence: driver-instance reuse, logical driver session
reuse, query success, connection-pool membership, or driver retry success. Driver-session success
in D-02/D-05 in fact occurs on a *different* physical connection, which lines 1/3/4 detect — that
is exactly why those lines are required rather than query success.