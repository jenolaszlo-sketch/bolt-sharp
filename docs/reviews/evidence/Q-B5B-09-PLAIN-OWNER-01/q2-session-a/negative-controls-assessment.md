# Negative Controls — Q-B5B-09-PLAIN-Q2

## Method

Executed in an isolated disposable R1 worktree (`C:\Windows\Temp\opencode\q2w\r1`,
detached at `6a40c4b84993dec77deb165cef31e419df05be2e`) on Windows Debug. The runner
(`negative-controls/run_controls.py`) applies each mutation by exact string replacement,
runs the canonical detecting filter with an xUnit TRX logger, then restores the original
bytes with `git checkout -- src/` and asserts a clean tree. No canonical production or
test file is edited.

Mutation shapes and detecting oracles are inherited from Q1's integrated controls, adapted
to R1's structured code (R1 separates the write delegate, the observer callback and the
permit release, so NC-01, NC-02, NC-07 and NC-08 required multi-hunk mutations).

## Results

| Control | Requirement | Mutation | Detecting oracle | Outcome |
|---|---|---|---|---|
| **NC-01** | R04 trailing execution | Re-enable `Ready` after marking the refusal; inside the terminal write delegate read a queued frame with the retired reader and hand it to the session | `ExactFailureAndNoTrailingExecution` — malformed, partial, large, delayed, small failed (5 of 6) | **DETECTED** |
| **NC-02** | R09/R10 socket retention | Suppress the helper's `Client.Dispose()` and remove the connection's client ownership | `ListenerStopJoinsActualHandleAndReturnsPermit` — actual handle closure assertion failed | **DETECTED** |
| **NC-03** | R06 linked cancellation | Remove the listener-token registration in the terminal owner | `ListenerStopJoinsActualHandleAndReturnsPermit` — expected `listener-shutdown`, actual `drain-deadline` | **DETECTED** |
| **NC-04** | R05 deadline renewal | Reset the total timer at drain entry and use a fresh drain elapsed origin | `OneDeadlineCoversWriteAndDrain` | **DETECTED** |
| **NC-05** | R08 byte cap | 8 KiB → 12 KiB drain byte cap | `ExactFailureAndNoTrailingExecution(scenario: "large")` | **DETECTED** |
| **NC-06** | R09 concurrency | Two drain permits per listener (clamped to `MaxConnections - 1`) | `DrainAdmissionAndRecovery(connectionLimit: 4, simultaneous: 3, admitted: 1)` | **DETECTED** |
| **NC-07** | R12 TLS isolation | Construct the plaintext helper for a TLS listener with a zero-permit policy, pass it into the authenticated connection, and give it the raw `NetworkStream` | `TlsRefusalNeverEntersPlaintextHelper` | **DETECTED** |
| **NC-08** | F01 ownership | Reintroduce the full original defect: write uses the owner token, the write timer interrupts the owner, and the observer is published inside the write phase | 17 of 28 `PlaintextTerminalCloseTests` failed; `SuccessfulWriteEndsWriteTimeoutBeforeObserver(scenario: "delay")` expected `peer-eof`, actual **`write-deadline`**; the independent F01 probe reported `phase=post-write` instead of `drain` | **DETECTED** |
| **NC-09** (added) | Resource-lifetime oracle effectiveness | Never release an acquired drain permit | 5 tests failed including `DrainAdmissionAndRecovery` | **DETECTED** |

All nine controls ended with a restored and verified clean tree.

## Interpretation

- **NC-01…NC-07** inherited from Q1 remain effective against R1. The F01 correction did
  not weaken any of the requirements they guard.
- **NC-08** establishes that the new F01 oracle detects reintroduction of the original
  write-timeout ownership defect. The earlier partial mutation (write token only) passed
  because R1's structural fix — observer publication *after* `write-complete` — prevents
  the write deadline from ever racing the observer. The complete reintroduction (which also
  moves the observer into the write phase) reproduces the original F01 signature exactly.
- **NC-09** was added to establish the effectiveness of the permit-resource oracle used in
  the observer-retention measurements: a permit leak is detected immediately by
  `DrainAdmissionAndRecovery` and by the admitted-drain scenarios, so the measured permit
  counts in the OBS experiments are trustworthy (a leak could not have gone unnoticed).

## Reproduction

```powershell
# Requires an isolated worktree at 6a40c4b (any disposable path).
python <evidence>/negative-controls/run_controls.py
```

The runner prints `DETECTED`/`NOT DETECTED`, the exact failing tests, and the final
`final tree status: clean`.