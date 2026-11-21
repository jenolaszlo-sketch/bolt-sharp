# Q-B5B-09-OWNER-01 — Production Remediation Scope Decision

**PLAINTEXT-ONLY GRACEFUL-CLOSE REMEDIATION AUTHORIZED WITH QUALIFICATIONS**

Decision date: 2026-11-16 (Asia/Manila). This record authorizes the separately
commissioned Q-B5B-09-PLAIN-IMPL-01 within the
[plaintext-only production implementation specification](Q-B5B-09-PLAIN-IMPL-01-implementation-specification.md).
The owner commission itself adds decision documentation and evidence. Production
release and any qualification closure require separate acceptance.

| Track | Formal disposition |
| --- | --- |
| A — Plaintext graceful closure | **IMPLEMENTATION AUTHORIZED WITH QUALIFICATIONS.** Use the bounded post-refusal lifecycle, explicit limits, and producer and independent checks in the specification. |
| B — TLS graceful closure | **DEFERRED — DESIGN AND QUALIFICATION OUTSTANDING.** Preserve the current TLS behavior. A materially improved experiment or alternative transport design requires separate authorization. |
| Parent Q-B5B-09 | **OPEN.** This decision does not discharge release hardening or authorize closure after plaintext alone. |

## Repository and evidence integrity

The canonical repository at D:/projects/bolt-sharp was on main, clean, with one
worktree, zero stashes, and 0 behind / 9 ahead of local origin/main
79f7965a0e8e351baf2dd388218290804c31389a. Entry HEAD was
608eb3f0e07b059eb3bddd86de9235d422e1540c with tree
86ea9450d0fd5935af5a3ab851553934e39bfa20. The exact sole-parent chain is recorded
in the [owner audit](../reviews/evidence/Q-B5B-09-OWNER-01/verification.json).
All 12 preceding decision blobs match the TS1 owner revision. The owner audit
independently checked complete package inventories, SHA-256 and byte counts over
Git blobs, and additions-only scope for each input commit.

| Input | Verified | Decision use |
| --- | --- | --- |
| [INV-01](../reviews/Q-B5B-09-INV-01-investigation.md) | 11/11 | Explains the abortive-close delivery boundary and the rationale for send shutdown followed by bounded drain. |
| [INV-02](../reviews/Q-B5B-09-INV-02-investigation.md) | 13/13 | Integrated prototype, exact failure delivery, real plaintext backpressure, and regression evidence. |
| [INV-03](../reviews/Q-B5B-09-INV-03-investigation.md) | 12/12 | Resource and cancellation bounds, typed linger readback, and the distinction between held fixture I/O and real pending writes. |
| [TLS-GATE-01](../reviews/Q-B5B-09-TLS-GATE-01-report.md) | 12/12 | Establishes two WSL2 terminal cases and preserves the pending Windows terminal and close-notification questions. |
| [B5-B-TS1 owner closure](B5-B-TS1-OWNER-01-owner-closure.md) | 5/5 | Preserves the byte-exact standalone gate and the accepted pipelined terminality and multi-client assertions. |

The audit replayed the INV-02 prototype, both INV-03 refinements and the TLS
gate observation patch against pinned source in temporary storage. It reproduced
the recorded four-file prototype, five-file refined and gate file identities,
and the separately adapted WSL2 variant. Archived TRX counts and every TLS journal
start and completion were reconciled. No experiment was rerun.

## Acceptance and authorization matrix

| Consideration | Evidence accepted | Implementation consequence |
| --- | --- | --- |
| Exact FAILURE delivery | On Windows plaintext the candidate delivered the exact refusal and EOF in five queued-tail cases where baseline reset before delivery. The standalone oracle still pins the whole payload. A continuing-writing peer that delayed reading could still lose the response. | Implement best-effort improvement for eligible queued tails. Preserve the exact wire payload and standalone oracle. Avoid a peer-application receipt guarantee. [INV-02 comparison](../reviews/Q-B5B-09-INV-02-investigation.md#3-integrated-comparison) |
| Session terminality | The session entered Defunct before the terminal write, produced a single refusal, and the integrated fixture observed no parser re-entry or provider preparation for queued commands. | Keep a session-owned eligibility classification and a transport-only discard path. Require an integrated negative control that detects trailing processing independently of additional replies. [INV-02 lifecycle](../reviews/Q-B5B-09-INV-02-investigation.md#2-integrated-candidate) |
| Pending plaintext terminal writes | INV-02 observed the real Windows 169-byte terminal write pending. The total deadline and listener stop interrupted it in separate trials. | Qualify genuine plaintext terminal-write interruption in the implementation. Distinguish it from an earlier application write and distinguish listener stop from eventual expiry. [INV-02 backpressure](../reviews/Q-B5B-09-INV-02-investigation.md#real-backpressure) |
| Concurrency and cleanup | INV-03 completed 260 TCP/TLS laboratory connections across Windows and adapted WSL2, with two observed permits and zero retained captured handles. It detected unlinked cancellation, deadline renewal and a higher drain capacity. | Enforce bounded admission and actual socket/permit release. Treat laboratory parameters as qualification inputs. Representative production capacity evidence remains required. [INV-03 resource results](../reviews/Q-B5B-09-INV-03-investigation.md#resource-and-cancellation-campaign) |
| Compatibility and regressions | INV-02 candidate passed 1,227 existing core, 43 official-driver and 34 integrated Windows cases. INV-03 passed 29 focused existing Windows cases and three driver cases. | Run fresh production-code checks of transport, TS1, existing TLS and pinned driver behavior. Archive candidate results support the authorization. [INV-02 campaigns](../reviews/Q-B5B-09-INV-02-investigation.md#5-tests-platforms-and-controls) · [INV-03 regressions](../reviews/Q-B5B-09-INV-03-investigation.md#negative-controls-and-regressions) |
| Socket linger | The original boolean SO_LINGER call succeeded on Windows and produced SocketException/InvalidArgument, native EINVAL 22, at exact WSL2 listener startup. The typed disabled option read back disabled on listener and accepted sockets in both environments. | Prepare a separately traceable typed correction. Qualify equivalent disabled semantics and plaintext/TLS startup on Windows and WSL2, with native Linux required for applicable release. [INV-03 portability](../reviews/Q-B5B-09-INV-03-investigation.md#socket-linger-and-native-linux) |

## TLS gate and preservation

**TLS-GATE-01 — TLS GATE UNRESOLVED — OWNER DECISION REQUIRED.** The audited
campaign used 12 Windows and 10 adapted WSL2 attempts, at most three per cell
per platform. Windows all stopped in the preceding application write. Adapted
WSL2 demonstrated real pending TLS terminal FAILURE interruption by deadline and
listener cancellation, with the network task observed to return, handle release,
and balanced accounting. The tested WSL2 close alerts completed before a
persistent pending write was observed. The campaign demonstrated no material
TLS cancellation or cleanup defect. Its coverage is insufficient for an
authorization of the candidate TLS graceful-close design.
[TLS gate matrix and attempts](../reviews/Q-B5B-09-TLS-GATE-01-report.md#four-cell-results-and-all-attempts).

TLS availability, authentication, encryption, security configuration, close
behavior, stream/socket ownership, disposal and integration contracts retain
their accepted behavior. Connection configuration selects plaintext or TLS
explicitly. Only plaintext may enter the new terminal close path. No TLS
disablement, downgrade, change of security defaults, raw drain of TLS-owned
bytes, or experimental TLS shutdown path is authorized. The separately scoped
linger prerequisite preserves the intended disabled setting for both transports.
TLS mode exclusion requires both direct regression evidence and an integrated
negative control.

Carry forward Windows real terminal TLS write interruption, real pending
close_notify interruption, independent encrypted-byte-cap verification, native
Linux TLS execution, and integrated TLS resource and lifecycle controls.
The existing TLS delivery limitation remains in Q-B5B-09. No automatic TLS
follow-up campaign is scheduled.

## Qualification and release boundary

| Qualification | Disposition |
| --- | --- |
| B5-B-TS1 | **CLOSED.** |
| Q-B5B-03 | **CLOSED.** The [previous owner closure](B5-B-F01-R1-OWNER-01-owner-closure.md) remains authoritative. |
| Q-B5B-04 | **OPEN.** Independent from this remediation. |
| Q-B5B-05 | **OPEN.** Independent from this remediation. |
| Q-B5B-09 | **OPEN.** Plaintext implementation authorization and deferred TLS work are separate tracks. |
| Other inherited qualifications | Prior dispositions remain binding. |

Producer qualification must include a real pending plaintext write, the
absolute deadline, bounded byte use and admission, actual ownership and cleanup,
TLS exclusion and the seven integrated controls. A fresh independent reviewer
session must assess the exact production candidate, Windows and documented WSL2
startup, TLS and driver regressions, controls and evidence integrity. Native
Linux and representative production-capacity characterization remain explicit
release gates. The specification defines the full
[independent qualification and release matrix](Q-B5B-09-PLAIN-IMPL-01-implementation-specification.md#independent-qualification-and-release-gates).

The next commission is **Q-B5B-09-PLAIN-IMPL-01**. No implementation was carried
out in this owner decision. Only the decision, specification and reproducible
audit evidence were added. [Owner evidence package](../reviews/evidence/Q-B5B-09-OWNER-01/README.md).
