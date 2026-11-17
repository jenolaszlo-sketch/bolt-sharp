# Q-B5B-09-PLAIN-Q1 findings register

## Q-B5B-09-PLAIN-Q1-F01 — Write deadline remains armed after completed write and flush

**Severity** Major, qualification blocker · **Requirements** R05, R10, R11 · **Reproducibility** Windows and WSL2, two captures per platform.

The terminal write delegate calls public `MessageProcessed` synchronously after the writer returns from flush. `BoltPlaintextTerminalClose.RunAsync` leaves the shorter write timer armed until that entire delegate returns. See `BoltTcpConnection.cs:239`–`246` and `BoltPlaintextTerminalClose.cs:67`–`72`. The diagnostic observer can therefore consume the shorter WriteTimeout after the write completed. The callback interrupts/closes the socket and sets the interruption to `write-deadline`. The still-current phase is `write`; the helper returns a failed timeout and bypasses send shutdown and drain despite the actual completed write.

The reviewer probe set WriteTimeout to 80 ms and delayed the inline public observer by 140 ms only for BEGIN's MessageProcessed. The peer half-closed its input and read the exact terminal FAILURE. Both platforms emitted three MessageProcessed events, classified `phase=write`, `reason=write-deadline`, and counted one ConnectionFailed. Final elapsed values were 149.62 ms on Windows and 144.60 ms in WSL2, below the 250 ms lifecycle budget. The observer runs only after write/flush by source order and the captured peer independently received the exact response. This failure exercises an existing public option and diagnostics hook, with no injected network failure. The first attempt reproduced it as well.

Stop the phase timeout at actual write/flush completion, preserve the overall absolute deadline, and keep disposal/outcome classification aligned with the pending operation or phase that was interrupted. Add a regression with the short WriteTimeout and a post-flush observer. Preserve the local MessageProcessed meaning and re-run pending-write, owner and listener cancellation and capacity checks. [Campaign files and probe](README.md).

## Q-B5B-09-PLAIN-Q1-F02 — Incomplete terminal diagnostic metadata and stage boundaries

**Severity** Minor · **Requirement** R11 · **Reproducibility** Source inspection and emitted events on both platforms.

The actual pending-write signal invokes the reporter with just connection ID and event name, leaving phase null and elapsed time zero. The success path emits write start/completion, shutdown and drain start. Failure, admission and drain end are inferred from the eventual combined `disposed` record, so observers cannot separately time admission or drain completion and write failure against actual disposal. Discarded payload bytes are excluded from the diagnostics. The pending-write tests validate actual task behavior, but the raw pending event itself cannot supply its timing.

Use consistent elapsed time and phase for the pending-write signal and record bounded stage transitions and outcome events required by the diagnostic contract, including explicit drain admission/end and failed write timing. Verify metadata over capacity, timeout, reset and listener shutdown without reporting payload or peer details.

## Q-B5B-09-PLAIN-Q1-F03 — Incidental edits beyond the authorized transport scope

**Severity** Minor · **Requirements** R02, R12 · **Reproducibility** Exact typed-linger-to-implementation diff.

The shared writer implementation reachable from TLS was altered for the plaintext branch (nullable timeout, optional pending-write observation and an added completion probe). The TLS call supplies the existing timeout and no observer, and exercised TLS outcomes matched the prior contract. The plaintext commit also rewrote the em dash in the unrelated public XML lifecycle comment to mojibake. The bound commission reserves shared transport changes for the separate linger correction. No change to public signatures, TLS authentication, configuration, raw-read exclusion or close behavior was found.

Limit the implementation delta to the authorized paths and behavior, keep TLS on the established writer behavior without the plaintext-only instrumentation, and restore the unrelated comment byte sequence.

## Formal disposition

**REMEDIATION REQUIRED** for the plaintext implementation due to F01. The separate typed-linger change passed the Windows and WSL2 implementation checks, with native Linux still a release gate. All inherited dispositions remain in force. The report records independent qualification, with owner acceptance reserved to the owner.
