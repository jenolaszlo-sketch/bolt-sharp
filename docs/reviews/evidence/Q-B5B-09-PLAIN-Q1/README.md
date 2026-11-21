# Q-B5B-09-PLAIN-Q1 evidence

[Qualification report](../../Q-B5B-09-PLAIN-Q1-qualification.md) · [Requirement matrix](requirement-matrix.md) · [Findings register](findings.md) · [Negative controls](negative-controls.md).

Run from repository root at the qualification commit.

```powershell
python docs/reviews/evidence/Q-B5B-09-PLAIN-Q1/verify-evidence.py HEAD
```

The verifier reads `git cat-file blob REV:PATH` and checks SHA-256 and byte count. The manifest excludes itself. The exact archive inventory includes a digest and length for each record. The verifier checks the qualification's sole parent, additions-only scope, the protected owner tree, the archive inventory, key campaign counters and the named failures for the defect and negative controls.

## Campaign record

| Platform | Core | Pinned official driver | Focused runs | Test runner and runtime |
| --- | --- | --- | --- | --- |
| Windows x64 build 26200 | 1,250 passed, 3 s | 43 passed, 2 m 9 s | 3 × 50 passed, 3 s each | SDK 10.0.401, VSTest and xUnit VS adapter 2.8.2, .NET 8.0.31 |
| Ubuntu 26.04.1 WSL2 x64 | 1,250 passed, 3 s | 43 passed, 14 s | 3 × 50 passed, 3 s each | SDK 10.0.114, VSTest 18.0.2 and the same xUnit adapter, .NET 10.0.14 |

All table entries had zero failures and zero skips. Times are runner summaries. TRX files preserve run and per-case timestamps. The Windows build produced Debug net8.0 assemblies. WSL2 ran those exact portable assemblies on kernel `6.6.114.1-microsoft-standard-WSL2`, using Linux CLR and sockets. WSL2 only had .NET 10.0.14 installed, so the launcher explicitly selected `DOTNET_ROLL_FORWARD=Major`. The Linux results cover that compatibility configuration. They cannot distinguish kernel and runtime effects, and leave native Linux and Linux .NET 8 verification outstanding.

Production and driver builds each had zero warnings. The solution had two xUnit2012 warnings, at lines 71 and 72 of BoltRequestDecodeTests.cs. The pristine owner baseline build showed the same two warnings. Both scoped format checks and Git whitespace verification passed. Build and restore logs record durations and command outcomes.

Each platform then ran the final comparison with eight cases on the linger and final revisions, five supplemental final boundary/ownership cases, and one F01 probe which failed as documented. The Windows original comparison had eight observations. The WSL2 original comparison had two tests observing EINVAL 22. Clean-source startup comparisons on the original and linger revisions ran two cases each per platform. Both original WSL2 cases failed with the expected startup error. The other six clean-source platform/revision cases passed. Accepted-socket readback used a disposable, read-only observation patch, preserved in the archive.

The archive retains the initial WSL2 launch attempt with an inline command-string quoting failure, and the first comparison launcher with pipe/semicolon splitting. Only a subset of the intended WSL2 comparisons reached the runner in that attempt. The fallback runner output and TRX files are preserved in `comparison-attempt-1`. Subsequent WSL2 runs used checked-in evidence scripts that quote VSTest options inside Bash. F01 reproduced before and after the retry.

## Reproduce

Use three disposable copies of the pinned source. The Windows launcher and WSL regression launcher use the clean producer-evidence revision in a directory named `source`. The fault launcher uses `controls` at the plaintext revision. The comparison and pristine launchers use `comparison` and change its detached HEAD to the listed revisions. Set the WSL scripts' base directories to match the writable local workspace. Keep results in an adjacent `campaign` directory. Extract the archive's `commands` files alongside the copies.

```powershell
& .\win-campaign.ps1
wsl.exe -d Ubuntu -- bash /path/to/wsl-campaign.sh
python run_controls.py
python run_comparison.py
Push-Location comparison
& ..\run-pristine.ps1
Pop-Location
```

The standard regressions exercise the unchanged TS1 standalone byte oracle, pipelined terminality and existing multi-client, TLS and driver contracts. The reviewer-owned probes add raw frame capture, accepted socket readback, exact byte boundaries, a healthy client alongside an admitted drain at MaxConnections 2, permit recovery after post-admission cancellation and a socket exception, single-use enforcement, and a post-flush write-timeout discriminator. The test additions and mutations remain in the evidence archive and disposable copies.

Evidence for the producer package was rehashed at `5d27b39c32a2d1e56a3d8ef7ce116a36445427cf` using its verifier. Its 8-file and 59-member pass and the owner/prior package replay are in `producer-verifier.log`. The baseline-to-producer diff showed no modification or removal under historical documentation and evidence. The raw-worktree CRLF caveat is recorded in the accepted historical TS1 Q1 package. The qualification verifier always uses committed bytes.
