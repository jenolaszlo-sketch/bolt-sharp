# Q-B5B-09-TLS-DESIGN-01 — Independent TLS Graceful-Close Design Review

**Formal disposition: B — Supported with qualifications. Retain the existing production TLS design.**

This review is independent and source/evidence-based; it is not a qualified representation of any prior observation that has not itself survived this review. No production implementation was made.

## 1. Preflight

- Repo: `D:/projects/bolt-sharp`
- Branch: `main`
- Expected post-E1 commit: `7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f`
- Expected tree: `2fdf1c3b96586465eb7547d23b6b1792240692ba`
- Parent of expected commit: `250fb99273f975ae6552eb4e0ebefcc149796fa7`
- Current tree verified exactly at commit and tree above.

## 2. Evidence verification

Running `docs/reviews/evidence/Q-B5B-09-TLS-E1/verify-evidence.py` passed:

```text
PASS 36/36 blob hashes, exact parent, additions-only inventory, bounded journals,
matrix reconciliation, mechanism controls, and production characterization
Commit 7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f
Tree   2fdf1c3b96586465eb7547d23b6b1792240692ba
```

The package’s post-execution record states that the aggregate `21` attempt fields in `commissioning-matrix.json` are stale; the operative predeclared bounds were per cell and per attempt, and every executed campaign stayed within those explicit bounds and watchdogs.

## 3. Independent E1 assessment

| E1 conclusion | Review | Reason |
| --- | --- | --- |
| Windows terminal-write interruption | Confirmed | `windows-campaign.jsonl`: T1/T2 have `Outcome="induced-and-interrupted"`, `Confirmed=true`, `PendingMs` 20.9–22.6 ms, `SenderWritableAtSample=false`, `InnerWrites[0].Stage="terminal-write"` / `Status="Faulted"`, `TerminalResult="Defunct:1"`, `Closed=true`, `Active=0`. |
| Windows close-notify interruption | Confirmed | T3/T4 have `Outcome="induced-and-interrupted"`, `Confirmed=true`, `InnerWrites[0].Stage="close-notify"` / `Status="Faulted"`, and bounded completion by listener or deadline. This was the E1 candidate’s observed `ShutdownAsync` path. |
| Encrypted cap | Confirmed | T5: `WireBytes=32768`, `Bytes=1441`; cleanup balanced and `Closed=true`. |
| Plaintext cap | Confirmed | T6: `Bytes=8192`, `WireBytes≤32768`; observed floods stayed documented as peer input and the durable drain stayed within cap. |
| Cleanup/accounting | Confirmed in candidate campaign | `Active=0`, `Closed=true`, permits balanced, `Defunct:1`, parser=5, provider=1. |
| WSL2 close-notify limitation | Confirmed as a limitation | T3/T4 reached `Outcome="target-reached-no-persistent-pending-write"`; no persistent live shutdown on the Linux-like runtime pair. This is not native Linux evidence. |
| Budget reconciliation | Accepted as record qualification | Effective bounds were per cell/attempt; executed totals are enumerated in `commissioning-budget-reconciliation.json`. No production consequence. |

Negative controls were consistent with their labels: NC-1 detected the encrypted-cap tamper (`cap-checks-failed`, wire >32768), and NC-2 detected the deadline tamper (`induced-but-checks-failed`, total >500 ms).

## 4. Production design review

**Production HEAD has no production close-notify path.** Production terminal writes are made through the normal response path; there is no observed production `ShutdownAsync`/close_notify invocation. E1’s “clean TLS EOF” is consistent with a TCP/TLS FIN without a production close_notify.

**Guarantee levels, intentionally not stronger**

| Level | Meaning | Result here |
| --- | --- | --- |
| Local write completion | `WriteAsync`/`FlushAsync` return after TLS encryption/kernel acceptance | Observed in journals where pending, or completed in smaller production cases |
| TLS close-notify transmission | Close alert delivered to the TLS layer | Not established for production HEAD; E1 candidate only |
| Peer receipt | Peer observes EOF/reads the message | journals report peer unread counts, but not application proof |
| Verified peer receipt | Peer application confirms/reads the data | Not demonstrated anywhere |

## 5. Ownership, cancellation, join, disposal, and concurrency

- Single-owner flow: `BoltTcpConnection` models one sequential owner task and writes are sequential.
- Existing interrupt-and-join precedent: `BoltPlaintextTerminalClose` uses owner interruption, cancellation of the active write, and an awaited join of the real operation before returning.
- Production termination is bounded: TLS/read/write timeout paths use linked cancellation and `Timeout`; server `StopAsync` waits with a configured shutdown timeout.
- Disposal ordering is no stronger than the code contract: connection and session disposal are contained, then the connection wrapper disposes the TCP socket, which is the owner boundary. This source review does **not** prove a close-notify alert was sent.

## 6. Formal disposition and retained evidence

- A: Existing design supported; qualification gaps only.
- **B: Supported with qualifications; no production changes required.**
- C: Production remediation required.
- D: Further investigation required.

The prior E1 disposition of B is unchanged. Production is retained because:

1. E1 found no BoltSharp production defect;
2. Windows behavior is positive evidence on Windows, not native Linux;
3. WSL2 does not establish native Linux close_notify behavior;
4. peer application receipt and production capacity remain unsupported, and
5. current production already remains the accepted behavior under the B scope decision.

## 7. Native Linux qualification strategy (design)

Use a real native Linux host, not WSL2. Repeat the E1 lab with native induced close_notify pendency:

- Terminal-write induction: small peer window plus deep send-queue pressure on native Linux; shrink `SO_SNDBUF`; deadline and listener-interruption repeatability.
- Close_notify induction: small peer `SO_RCVBUF` and a deliberately non-reading peer to create a live close-notify backpressure, or use a controlled TLS proxy/tcpdump/tshark at the peer boundary; require a live `ShutdownAsync` to block on the close_notify record; verify TLS protocol identity in the journal.
- Verified receipt: peer application reads the terminal message and confirms receipt; journal peer-receipt evidence separately from transport completion.
- Representative capacity: E1’s deep profiles (262144 B queue/peer windows 1024–2048; shallower 36864, 45056, 49152 B profiles), plus production-style deep-default-timeout cases.

## 8. Minimum remaining regression matrix

- Windows: T1–T4 terminal/close deadline and listener cases; T5/T6 cap cases; NC-1/NC-2.
- Native Linux: close_notify induction and interruption; terminal-write interruption.
- Release gates: accounting/active=0/permit release; clean close cleanup; cooperative peer receipt; independent encrypted/plaintext caps; existing TLS, driver, parser, and regression suites.
- Not claimed without new evidence: native Linux qualification, production-capacity qualification, verified peer-app receipt.

## 9. Recommended smallest next commission

**Native-Linux close_notify qualification campaign** — observational and bounded. No production implementation until a separate design/implementation commission is approved.

A later transferable design/implementation commission should include: interrupt-and-join semantics for the TLS terminal write and close_notify stage, exclusive socket-owner coordination, `SslStream`-sole encrypted-byte ownership, independent byte caps, and a typed-linger prerequisite. Do not merge this review into that commission.

## 10. Repository state after commit

- Branch: `main`
- Commit: `7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f`
- Tree: `2fdf1c3b96586465eb7547d23b6b1792240692ba`
- Status: clean, one worktree, zero stashes
- No push.
