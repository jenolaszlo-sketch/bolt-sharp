# Q-B5B-09-TLS-E1 — Bounded TLS Graceful-Close Investigation

**Formal disposition: B — Supported with qualifications.**

The campaign demonstrated real backpressure and interruption on the Windows loopback
TLS path. It established exact encrypted and plaintext caps and demonstrated bounded
production termination and accounting under the supported lab link. No BoltSharp defect
was demonstrated. The qualification rests on:

- real Windows terminal-write pendency before an enforced, promptly joined interruption;
- real Windows close-notification pendency before interruption;
- WSL2/Linux repeated terminal-write induction and interruption, but no reliable
  close-notification pendency due Linux over-commit semantics and harness limits;
- exact encrypted/plaintext drain caps;
- clean ownership, accounting and permit reconciliation;
- bounded production-path behavior with accounting and peer-EOF observations.

Remaining first-class release barriers are native Linux, production capacity,
forceable peer-receipt observation, and integration of a future TLS design; none of
them has been claimed.

## 1. Preflight, candidate and runtime

| Check | Result |
| --- | --- |
| Canonical checkout | `D:/projects/bolt-sharp`, branch `main` |
| Entry commit / tree | `250fb99273f975ae6552eb4e0ebefcc149796fa7` / `bb69f2e3986dbae6a5db705c20690cb9f1cb9568` |
| Entry state | Clean, one worktree, zero stashes, 0 behind / 25 ahead of the retained origin |
| Production track | Head at entry; only an observer fixture; no production or test modification |
| Candidate | Reconstructed base `23d58da4e0c0e10aa46a31f05ef6a0c31ad3f2fa` plus INV-02 prototype, INV-03 refinement, real-write observation and gate link-observation patches |
| Windows SDK / runtime | 10.0.401 / 8.0.31 |
| WSL2 platform | Linux 6.6.114.1, .NET 10.0.14 roll-forward on Windows SDK 10.0.401 assemblies; native Linux not claimed |

Replay comparisons reproduced the documented five-file source-hash set and separately
verified the adapted WSL2 listener hash
`c935031985cd921eb9e56857ca6ffe8b838c4f4e33747e96b853d9c69ebb29c7`.

## 2. Investigation-record qualification

The predeclared per-cell repeat rule is one or more qualifying discovery attempts
(the campaigns produced three), plus two repeats, for up to six runs per cell and three
runs for cap cells. That rule was enforced. A mechanical audit defect in the stale
`commissioning-matrix.json` aggregate fields is disclosed below rather than converted
into a claim of excess execution:

- Stale `windows_platform_attempts_total: 21` and `wsl2_campaign_attempts_total: 21`
  versus explicit per-cell maxima of 30 and 30.
- Final candidate records: 30 Windows, 26 WSL2.
- Retained first Windows run: 11.
- Windows NC-1: 25, NC-2: 1, mechanism: 3, production: 11.
- WSL2 repeat campaign: 26, mechanism: 3, production: 11, production repeat: 11.

The investigation therefore does not assert a strict aggregate-cap compliance; it asserts
the explicit, auditable per-cell and per-attempt bounds. This qualification affects the
evidence-record governance score, not the technical A–D classification of the transport
behavior. The verifier preserves and explains this finding; evidence paths remain exact.

## 3. Predeclared matrix and actual execution

| Profile | Terminal-write candidate bound | Close-notify candidate bound | Caps |
| --- | --- | --- | --- |
| T1 | Windows deadline ~300 ms; WSL2 total deadline | Windows and WSL2 listener, ≤120 ms | Cipher cap 32768 B, decrypted ≈1441 B |
| T2 | Same total deadline stimulus | Same listener stimulus | Same cap |
| T3 | Windows phase deadline ~75 ms | Same phase deadline; close_notify honored oracle | — |
| T4 | Same total deadline stimulus | Same listener stimulus | — |
| T5 | — | — | Tiny flood, encrypted cap |
| T6 | — | — | Large flood, plaintext cap |
| Negative controls | NC-1 detects `CiphertextLimit` tampering; NC-2 detects `TotalMilliseconds` tampering | — | — |

The Windows T1–T4 final profiles completed the earlier application write and entered the
terminal stage on Windows. The gate adjustment was necessary: the gate's preceding
results of 131072–524288 bytes could not complete on a 65536-byte Windows send buffer.
Profiles 1–3 are 36864/45056/49152 bytes with small peer-receive windows; the fourth
profile (262144/1024/2048) is the WSL2-adapted deep profile.

## 4. Windows candidate — real terminal-write interruption

| Cell | Stimulus and condition | Result |
| --- | --- | --- |
| T1 | Absolute total deadline | Induced pending write; confirmed pending ~21.3–22.3 ms; interrupted at ~300 ms; terminal-write stage `Faulted` at ~299.8–301.8 ms; final elapsed 308–357 ms; terminal state `Defunct:1` |
| T2 | Listener stop | Induced pending write; interrupted at ~20.2–22.6 ms; final elapsed ~28.6–34.7 ms |
| T3 | Close-notify phase deadline | Induced close_notify write; confirmed pending ~20.4–21.8 ms; interrupted at ~74.8–75.9 ms; finish `phase-deadline` |
| T4 | Listener stop | Induced close_notify write; listener signal at ~20.2–21.2 ms; final elapsed ~31.8–34.7 ms |

Windows close-notify phases finished within the 75 ms sub-budget. All six candidate
profiles closed the socket, balanced accounting, had zero active sockets and zero peer
notifications after interruption where the oracle requires it. Windows qualification is
the read of this table rather than a claim that the candidate is native-Linux.

## 5. WSL2 candidate and mechanism — close-notify qualification remains open

| Cell | Result | Interpretation |
| --- | --- | --- |
| T1–T2 terminal write | Three repeats qualified in both cells; an additional deep discovery run induced a real pending write on Linux and was interrupted by the total deadline | Strong positive evidence of Windows-only/real backlog portability |
| T3–T4 close_notify | Four discovery attempts each: `target-reached-no-persistent-pending-write`; no repeat was required | Harness limitation, not a `BoltSharp` defect; the live `ShutdownAsync` completed before a persistent-pending sample on this platform/runtime pair |
| T5–T6 caps | Three qualifying repeats each | Independent byte caps enforced |

Raw-socket Windows probes honored `SO_SNDBUF` shrink requests exactly (65536 → requested
values) and kept the raw write pending at 400 ms. Adapted WSL2 measured an autotuned send
buffer of 2626560, a shrunken queue of 4608 bytes, and completed writes immediately;
the probe notes over-commit behavior below the shrink limit as platform behavior, not a
defect. Therefore T3/T4 remain an unresolved harness case for close-notify pendency;
they cannot support a release-grade Linux close-notify claim by themselves.

## 6. Encrypted and plaintext caps

All Windows, WSL2 and negative-control runs enforced the declared limits:

- T5: `WireBytes = 32768`, `Bytes = 1441` decrypted. Floods varied by run; observed flood
  totals after the capped operation include 344–1494 bytes and occasional expected I/O
  errors after the drain stage.
- T6: `Bytes = 8192` plaintext and `WireBytes ≤ 32768`. Observed flood totals ranged from
  114688 to 365056 bytes, but the durable drain bound was not exceeded.
- Cleanup: `Closed=true`, `Active=0`, accepted = completed + failed, permits balanced,
  parser entries 5, terminal state `Defunct:1`, provider preparations 1.

Negative controls:

| Control | Observed |
| --- | --- |
| NC-1, `CiphertextLimit` mutated to 131072 | T5 reports `cap-checks-failed`, `WireBytes=131072`, outcome `cipher-cap`; control failure observed |
| NC-2, total budget mutated from 300 to 600 ms | T1 reports `induced-but-checks-failed`; total elapsed ~600.8 ms and inner write ~599.2 ms; control failure observed |

These campaigns are intentionally labeled as positive control demonstrations, not BoltSharp
production defects.

## 7. Production characterization (unmodified HEAD)

### Windows

| Case | Observation |
| --- | --- |
| P1 small result parked | Completed `closed` in ~6–63 ms; peer observed a meaningful byte count; clean TLS EOF |
| P2 deep result, default timeout | `ConnectionFailed:timeout` at ~30006 ms; not processed by the Defunct peer; clean TLS EOF |
| P3 deep result, 750 ms | Timeout in ~757–758 ms; not processed; clean TLS EOF |
| P4 deep listener stop | `ConnectionCompleted:listener-shutdown` at ~504–505 ms; no terminal-message processing observed |
| P5 GOODBYE | Completed; one `sig=0x02` close signal, responses=0; clean TLS EOF |
| P6 cooperative receipt | Completed; exact `08N06 / Neo.ClientError.Request.Invalid`; clean TLS EOF |

Production lifecycle/accounting stayed bounded and settled: Windows active count was zero
and accepted counters balanced in all observed cases. Windows close-notify stall under a
small deep queue was consistent with Linux-like over-commit behavior on the Windows
runtime; it is not, by itself, production-Linux evidence.

### WSL2

Every case completed `closed` in ~3–5 ms on final repeats, with clean TLS EOF. Deep cases
were received by the parked peer and the connection entered `Defunct`; there were no
timeouts in P2/P3/P4 and no terminal loss on P6. Production WSL2 behavior is therefore
compatible with, but not a substitute for, native Linux qualification.

## 8. Platform/runtime/harness/defect separation

1. **Platform behavior.** Windows raw probes show real send-queue pendency when the send
   buffer is shrunk. Adapted WSL2 raw probes show autotuning/over-commit behavior that
   avoids persistent small-queue pendency. Windows close_notify was induced pending by the
   candidate, but not by the small production queue on the Linux-like WSL2 runtime.
2. **TLS runtime behavior.** The candidate observed real encrypted-write pendency on
   Windows and a real interrupted `SslStream` pending write; it observed a completed
   `ShutdownAsync` on adapted WSL2. Production bytes showed bounded close and clean peer
   EOF, not proof of explicit close_notify delivery.
3. **Harness limitations.** WSL2 close_notify remained uninducible on this platform;
   peer-receipt observation is conditional. Windows production close-notify is not induced
   because of the WSL2-observed queue behavior; native Linux measurement is the relevant
   test for release.
4. **BoltSharp defects.** No defect was demonstrated. Candidate cleanup, byte caps,
   accounting, permit release, socket closure and watchdog behavior were consistent
   across positive runs and negative controls.

## 9. Formal A–D disposition

- **A — Defect demonstrated and remediation required:** not applicable.
- **B — Supported with qualifications:** applicable. The campaign provides real positive
  evidence, negative-control enforcement and cleanup observations. Required acceptance
  qualifications are limited to native Linux, production capacity, close-notify pendency,
  peer application receipt, and a later integrated design qualification.
- **C — Inconclusive or materially insufficient:** not applicable as the primary finding,
  because explicit platform/harness boundaries and a full matrix were recorded. It remains
  a useful input to a future native-Linux campaign.
- **D — Not supported, refutability demonstrated, or so incomplete that no verdict can be
  claimed:** not applicable.

## 10. Smallest proposed remediation — not implemented

There is no production defect remediation to propose. For the deferred release-grade
TLS graceful-close, the smallest forward transport design consistent with the
investigation is:

1. Keep the socket owner as the exclusive socket and, at the terminal stage, retain the
   current serialized FAILURE write/flush and `Defunct` state.
2. Keep the TLS write stage interruptible and observe the actual inner task before
   returning; the prototype demonstrates owner disposal can interrupt and join it.
3. Add an interrupt-and-join contract for the TLS `ShutdownAsync` close_notify stage, or
   a closely scoped replacement design that sends close_notify only through `SslStream`
   and bounds it by a monotonic timer and listener cancellation.
4. Keep `SslStream` the sole consumer/producer of encrypted bytes; discard new transport
   bytes only as they are encrypted, with a separate encryption-specific 32 KiB cap.
5. Preserve peer-receipt semantics as best effort: no warning that a TCP write or TLS EOF
   proves remote application receipt.

Affected contracts would be internal:

- `BoltTcpConnection` terminal-I/O helper and shutdown contract;
- `BoltTcpServer` owner cancellation, admission, ownership and accounting;
- `BoltConnectionSession` refusal classification and delivery-eligibility boundary;
- typed linger prerequisite for both transports;
- transport diagnostics/telemetry for stage, cause, timeout, wire bytes and cleanup.

Preserved public contracts: existing FAILURE encoding, GOODBYE behavior, ordinary error
classes, connection configuration API, default write/listener timeouts and their current
TLS behavior unless a new independently authorized contract review changes them.

Required regression evidence would include:

- genuine pending Windows terminal-write deadline and listener cases;
- genuine pending Windows close_notify deadline and listener cases;
- native-Linux close_notify induction with forceable backpressure and repeatability;
- independent encrypted-byte tamper controls;
- production and integrated accounting/unobserved-exception checks;
- driver/parser regression coverage where TLS defaults may change.

The investigation does **not** propose or implement any of these.

## 11. Recommended next commission

Commission: **Q-B5B-09-TLS-DESIGN-01 — bounded TLS graceful-close transport design review.**

Scope: define the interrupt-and-join/ownership design above, including native-Linux
close_notify testing strategy, contract and regression matrix, integration boundaries and
production-capacity release gates. Do not implement until a separate qualification plan is
commission-approved. Q-B5B-09 remains **OPEN**; B4 and B6 remain **NOT INITIATED**;
native Linux and representative production capacity remain outstanding.
