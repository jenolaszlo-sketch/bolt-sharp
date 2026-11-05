# B2-C-R1 — Targeted harness and driver scaffold remediation

**Status:** COMPLETE — targeted remediation delivered; B2-C-Q2 requalification PENDING
**Date:** 2026-11-05 (Asia/Manila)
**Task:** B2-C-R1 (remediate B2-C-Q1 findings **Q1-F01** and **Q1-F02**)
**Canonical branch:** `main` (one worktree)
**Reviewed implementation:** `5295137bd688d38a2c1e3c67e6cc2daa51c050fb`
**Qualification commit:** `2710bc73741ecd1f144cf4d60d7e2387d8f125f9` / tree `1c5dcc38a1138f662fb0b00fbbf5708b4d171f25`

This is an additive remediation record. It does not modify the B2-C-Q1 report, B2-C-Q1 reviewer
probes, B2-C-Q1 evidence, or any accepted B2-A/B2-B code or evidence. It makes no
`Neo4j.Driver` compatibility claim and does not enable D01–D10.

---

## 1. Baseline identity

| Object | Exact identity |
| --- | --- |
| Baseline (qualification) commit | `2710bc73741ecd1f144cf4d60d7e2387d8f125f9` |
| Baseline tree | `1c5dcc38a1138f662fb0b00fbbf5708b4d171f25` |
| Reviewed implementation | `5295137bd688d38a2c1e3c67e6cc2daa51c050fb` / tree `5351dacd16475c75a2b44054a033ba34bd610e51` |

Entry `main` was clean with one worktree and was 2 commits ahead of `origin/main` (`8f8b7e4`).

## 2. Reproduction of the findings before remediation

**Q1-F01 (MINOR).** With `BOLTSHARP_DRIVER_URI=neo4j://127.0.0.1:7687`, the scaffold accepted the
routing URI instead of rejecting it:

```text
dotnet test tests/BoltSharp.DriverIntegrationTests/BoltSharp.DriverIntegrationTests.csproj \
    --filter FullyQualifiedName~Scaffold_Default_Endpoint_Is_Direct_Loopback
Assert.False() Failure: Expected False, Actual True   (UsesRoutingScheme == true)
Failed! - Failed: 1, Passed: 0
```

`DriverTestSettings` documented that routing schemes "are rejected", but no code enforced it and
`GraphDatabase.Driver` was constructed directly from `settings.Uri`.

**Q1-F02 (MINOR).** The reviewer probe
`C6_Dispose_alone_does_not_wake_a_blocked_read_documented` observed:

```text
wokeOnDispose=False
```

A blocked `ReadAsync` remained pending after `HarnessStream.Dispose()` and was released only by
cancellation.

Both findings were reproduced at the baseline before any change.

## 3. Fix — Q1-F01: enforced direct-URI validation

`tests/BoltSharp.DriverIntegrationTests/DriverTestSettings.cs`:

- New `internal static Uri ValidateUri(string value)`:
  - Accepts only the direct schemes `bolt`, `bolt+s`, `bolt+ssc`.
  - Rejects `neo4j`, `neo4j+s`, `neo4j+ssc` and any other/unsupported scheme.
  - Rejects malformed/relative URIs, an empty host, embedded credentials (`userinfo`), a path,
    a query string and a fragment. A routing URI is never transformed into a direct URI.
- The constructor now runs every configured value through `ValidateUri`, so validation happens at
  the configuration boundary and applies to all configuration sources.
- New `internal IDriver CreateDriver(IAuthToken? authToken = null)` is the single driver-construction
  path; because it consumes only validated settings, every construction path is constrained.
- Environment-driven configuration and the loopback default (`bolt://127.0.0.1:7687`) are unchanged.

`tests/BoltSharp.DriverIntegrationTests/DriverInteropScaffoldTests.cs`:

- `NewDriver` delegates to `DriverTestSettings.CreateDriver()`.
- `Scaffold_Rejects_Routing_Scheme_For_A_Direct_Server` now asserts actual rejection of the three
  routing schemes (previously it only checked the default and a literal `Uri`).
- New `Scaffold_Constructor_Rejects_Routing_Uri_From_Environment` asserts the constructor rejects a
  routing value from the environment and restores the variable.
- D10 uses `badSettings.CreateDriver(...)` for the deliberately-wrong-credential path.

D01–D10 remain gated (skipped); no connectivity is attempted.

## 4. Fix — Q1-F02: disposal wakes pending operations

`tests/BoltSharp.Tests/Harness/HarnessStream.cs`:

- `HarnessChannel` gains a `_disposed` flag, `IsDisposed`, and a `Dispose()` that sets the flag and
  completes the pending-read signal, so a blocked reader wakes immediately.
- `HarnessChannel.Read`/`ReadAsync` throw `ObjectDisposedException` once disposed (before returning
  any buffered data), and `Write` throws `ObjectDisposedException` once disposed.
- `HarnessStream` guards `Read`/`ReadAsync`/`Write` with a disposal check and overrides `Dispose` to
  mark itself disposed and dispose its **read** channel (idempotently). Repeated disposal is safe.
- Bounded-buffer signalling, half-close (`CompleteWrites`) and caller stream ownership for
  non-disposed streams are unchanged.

Disposal now yields `ObjectDisposedException` (a disposal result), never a clean EOF or a
successful read of stale buffered data.

## 5. New regression cases

**F02 — `tests/BoltSharp.Tests/Harness/HarnessDisposalTests.cs` (9 cases):**

| # | Case |
| --- | --- |
| 1 | Dispose before read → `ObjectDisposedException` |
| 2 | Dispose during a blocked read → released promptly with `ObjectDisposedException` |
| 3 | Dispose after buffering data → reflects disposal (not EOF, not stale data) |
| 4 | Cancellation versus disposal → each observed deterministically |
| 5 | Concurrent writer activity during disposal → no deadlock, terminates |
| 6 | Repeated disposal → safe and idempotent |
| 7 | Dispose-during-read repeated 50× → stable, no intermittent hang |
| 8 | Half-close preserved for a non-disposed stream (clean EOF) |
| 9 | Bounded-buffer signalling preserved |

**F01 — `tests/BoltSharp.DriverIntegrationTests/DriverUriValidationTests.cs` (19 cases):**

- Accepts 5 direct URIs (`bolt`, `bolt+s`, `bolt+ssc`, host with/without port, trailing slash).
- Rejects 14 routing/unsupported/malformed URIs (all three `neo4j` schemes, `http`/`https`/`ws`/
  `wss`, missing host, path, query, fragment, embedded credentials, non-URI, empty).

These tests validate URI policy only; they do not attempt connectivity.

## 6. Finding-to-fix-to-test traceability

| Finding | Severity | Fix site | Regression tests |
| --- | --- | --- | --- |
| Q1-F01 — routing URI documented as rejected but not enforced | MINOR | `DriverTestSettings.ValidateUri` + constructor + `CreateDriver`; scaffold `NewDriver` | `DriverUriValidationTests` (19); `Scaffold_Rejects_Routing_Scheme_For_A_Direct_Server`; `Scaffold_Constructor_Rejects_Routing_Uri_From_Environment` |
| Q1-F02 — `Dispose` does not wake a blocked read | MINOR | `HarnessChannel.Dispose`/`ReadAsync`/`Write`; `HarnessStream.Dispose` | `HarnessDisposalTests` (9) |

## 7. Test commands and outcomes

Environment: Windows `10.0.26200`, `win-x64`; .NET SDK `10.0.401`; target `net8.0`; .NET 8 runtime
`8.0.31`. Commands are in `docs/reviews/evidence/B2-C-R1/` (`Run-Evidence.ps1`, `commands.json`,
`summary.json`).

| Executed set | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Full solution (`BoltSharp.slnx`) | 826 | 0 | 0 |
| — H01–H07 + disposal harness (`~Harness`) | 40 | 0 | 0 |
| — F02 disposal subset (`HarnessDisposalTests`) | 9 | 0 | 0 |
| Driver scaffold | 24 | 0 | 10 |
| — F01 URI subset (`DriverUriValidationTests`) | 19 | 0 | 0 |
| B2-B-Q1 reviewer probes | 67 | 0 | 0 |
| B2-B-Q2 reviewer probes | 46 | 0 | 0 |
| B2-A-Q2 reviewer probes | 18 | 0 | 0 |
| B2-C-Q1 reviewer probes | 29 | 1 | 0 |

Core solution build: **0 warnings, 0 errors**; driver scaffold build: **0 warnings, 0 errors**.

**B2-C-Q1 reviewer probe note (expected).** The single B2-C-Q1 failure is
`C6_Dispose_alone_does_not_wake_a_blocked_read_documented`. That probe is a point-in-time
limitation probe that asserted the **pre-remediation** behaviour (`Assert.False(wokeOnDispose)`,
`OperationCanceledException` only). Remediating Q1-F02 intentionally changes exactly that behaviour,
so the probe now fails by design. It is **preserved unmodified** (per the task constraint) and
**not** counted as a regression: its sibling probes still pass and the new `HarnessDisposalTests`
assert the corrected behaviour. The B2-C-Q2 reviewer should supersede C6 with a corrected probe.

The pre-remediation baseline is preserved: 817 (786 existing + 31 H01–H07) → 826 after adding the 9
F02 cases; the driver scaffold baseline of 4 passed / 10 skipped → 24 passed / 10 skipped after
adding the 19 F01 cases and the constructor guard. Production `src/BoltSharp` is byte-for-byte
unchanged (`git diff --exit-code 2710bc7 <remediation> -- src` empty).

## 8. Remaining limitations

- D01–D10 remain gated; no executable driver connectivity, TLS, `HELLO`/`LOGON` or graph decode.
- The B0-A revision remains **PROVISIONAL**; no compatibility claim is made.
- `net8.0` in-memory evidence only; no transport/messaging implementation.
- URI validation is a policy check, not a connectivity check; it does not prove any endpoint works.
- Disposal makes the endpoint unusable (reads/writes throw); this is intended and documented.

## 9. Commit and tree identities

The remediation/test/evidence commit and any evidence commit are recorded in the final handoff
message of this task (this file is part of one of them, so it cannot contain its own SHA). Baseline
identities are in §1; the `src`/`tests`/evidence trees are captured in
`docs/reviews/evidence/B2-C-R1/identity.txt`.

## 10. Acceptance receipt

```text
Task: B2-C-R1 (targeted remediation of B2-C-Q1 Q1-F01 and Q1-F02)
Baseline commit: 2710bc73741ecd1f144cf4d60d7e2387d8f125f9
Baseline tree:   1c5dcc38a1138f662fb0b00fbbf5708b4d171f25
Changed files: tests/BoltSharp.Tests/Harness/HarnessStream.cs (dispose wakeup);
               tests/BoltSharp.Tests/Harness/HarnessDisposalTests.cs (new);
               tests/BoltSharp.DriverIntegrationTests/DriverTestSettings.cs (URI validation);
               tests/BoltSharp.DriverIntegrationTests/DriverUriValidationTests.cs (new);
               tests/BoltSharp.DriverIntegrationTests/DriverInteropScaffoldTests.cs (wiring);
               docs/reviews/B2-C-R1-targeted-remediation.md; docs/reviews/evidence/B2-C-R1/**
Production assembly changed: NO (src/BoltSharp untouched)
Commands: dotnet build BoltSharp.slnx -c Debug; dotnet test BoltSharp.slnx --no-build -c Debug;
          driver/scaffold build+test; B2-B-Q1/B2-B-Q2/B2-A-Q2/B2-C-Q1 probe projects
Tests: solution 826/0/0; harness 40; F02 9; driver 24 passed/10 skipped; F01 19;
       B2-B-Q1 67, B2-B-Q2 46, B2-A-Q2 18; B2-C-Q1 29 passed/1 expected-fail (C6, preserved)
Builds: 0 warnings / 0 errors
Independent reviewer verdict: PENDING (B2-C-Q2)
Owner acceptance: PENDING
Stop: Q1-F01 and Q1-F02 remediated; D01–D10 gated; no push; B3/B4/B5 not begun
```
