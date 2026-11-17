# Independent requirement matrix

Derived from the binding owner decision and implementation specification before disposition. The IDs below identify obligations used in the finding and control records.

| ID | Owner obligation | Reviewer evidence | Result |
| --- | --- | --- | --- |
| R01 | Fresh reviewer, exact commits/trees and chain | Chat identity and canonical preflight | Verified |
| R02 | Separately traceable typed linger, narrow file and behavioral scope, unchanged public and dependency contracts | Three complete deltas, startup probes, prior tree comparison | Linger verified. Incidental source scope deviations F03 |
| R03 | Session-owned authenticated eligibility in Ready, Streaming and Failed. Established retirement and Defunct before exactly one byte-exact terminal FAILURE | Session and codec trace, standalone TS1 wire oracle, raw peer frame, full regressions | Verified |
| R04 | Reader retires, no parser/provider re-entry, no trailing command or extra response | Early return in loop, discard-only reads, TS1 pipeline oracle and NC-01 | Verified |
| R05 | One absolute monotonic deadline starts after classification before encoding. Await write and flush under the shorter write limit. Drain never renews total budget | Timer and Stopwatch trace, real pending writes, write-to-drain test and NC-04 | Total-budget behavior verified. Write-phase endpoint violates F01 |
| R06 | Linked listener and owner cancellation interrupt and join actual pending I/O well before deadline | Pressure records on both platforms and unlinked listener NC-03 | Verified for pending I/O, subject to F01 at the post-write observer boundary |
| R07 | Try immediate permit after local write/flush. Capacity fallback preserves refusal and terminality | Zero-wait source, MaxConnections 1 and saturation tests | Verified |
| R08 | Send half-close, 75 ms drain within total, at most 8 KiB in 2 KiB scratch, opaque bytes | Source slices and allocation, byte-boundary probes, reset/stall/cap tests and NC-05 | Verified |
| R09 | Bounded permits inside connection admission, healthy-client headroom, cleanup before release | Limits 1–4, competing terminals, healthy client and post-admission cancel/fault recovery, NC-02 and NC-06 | Verified in the laboratory |
| R10 | Stop signals owners, captures concurrent accepts, balances exactly one terminal transition after cleanup | Stop/accept race, active drain interruption, handle and counter checks, single-use restart rejection | Ordinary paths verified. Wrong completion classification under F01 |
| R11 | Bounded correlated observations and accurate cause, time and bytes without payloads | Internal diagnostic source and emitted events, independently reproduced F01 | F01 and F02 |
| R12 | TLS never uses new helper, permit or raw drain. Preserve TLS auth, write, close and configuration | Stream selection, TLS integration and driver suites, NC-07 | Isolation verified in tested scope. Shared code edits identified in F03 |
| R13 | Disabled linger on listener and accepted socket in both modes on both platforms. Separate startup portability from delivery | Read-only option observations and clean source startup; original WSL2 EINVAL 22 | Verified for Windows and WSL2 |
| R14 | Meaningful Windows queued-tail improvement, paired baseline and adverse runs, retain application receipt boundary | Exact raw frames and EOF/reset results for three revisions, separate WSL2 comparison | Verified for tested cooperative scenarios |
| R15 | Detect and restore all seven integrated negative controls | Independent edits in local copy with clean controls before and after, patch and TRX evidence | Seven intended detections |
| R16 | Fresh platform suites, builds, format and existing warning comparison | Two platform campaigns, three focused repeats each, Debug build logs, owner baseline warning inventory | Verified on Windows and WSL2 |
| R17 | Historical and producer evidence integrity, retained owner and accepted TS1 dispositions | Git-blob and ZIP rehash, historical/owner replay, protected tree and file scopes | Verified |
| R18 | Native Linux, representative capacity and outstanding TLS requirements reserved for release | Local environment inventory, binding owner gates and TLS-GATE-01 | Explicitly outstanding |

[Owner decision](../../../decisions/Q-B5B-09-OWNER-01-production-remediation-scope-decision.md) · [Implementation specification](../../../decisions/Q-B5B-09-PLAIN-IMPL-01-implementation-specification.md).
