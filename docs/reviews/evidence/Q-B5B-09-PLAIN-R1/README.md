# Q-B5B-09-PLAIN-R1 producer evidence

[Implementation report, finding-by-finding and regression matrices](../../Q-B5B-09-PLAIN-R1-implementation.md).

From the repository root, verify the evidence commit with `python docs/reviews/evidence/Q-B5B-09-PLAIN-R1/verify-evidence.py HEAD`. The verifier uses SHA-256 and byte length over `git cat-file blob REV:PATH`. It checks the implementation and evidence inventories, sole-parent relationships, historic documentation identity, exact ZIP members, test counts and mutation assertion messages. The manifest excludes itself. The archive preserves the recorded test, patch and log bytes.

## Re-execution

Use disposable copies in directories named `baseline`, `source` and `controls`, with the archived command files and a writable `campaign` directory alongside them. Set the base path in the Bash launchers and the controls runner to match the WSL mount of your workspace.

- **Baseline.** Check out `20fbc461c0257620db28fb78a463218b242455bc`. Apply only `campaign/baseline-probe.patch`. Run `dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -nodeReuse:false --filter FullyQualifiedName~Q1WriteTimeoutMustEndAtWriteAndFlush --logger "trx;LogFileName=win-baseline.trx"`. The PowerShell run added `--results-directory` pointing to the campaign directory. Run the Bash baseline launcher in Ubuntu WSL2 on the same compiled assemblies. The expected result is one failed probe per platform after exact peer receipt, with `phase=write`, `reason=write-deadline`, one failure and zero admitted drains. The probe adds test code only.
- **R1.** Put the implementation revision in `source` and run `& ..\win-campaign.ps1` from that directory. Run `wsl.exe -d Ubuntu -- bash /path/to/wsl-campaign.sh` after the Windows builds complete. The commands restore packages, build production, solution and driver, run the full suites and three focused repetitions, and scope `dotnet format --verify-no-changes` to the four R1 paths.
- **Controls.** Put the entry Q1 revision in `controls` and run `python run-controls.py`. It copies the four R1 files into the isolated tree, builds and tests one control at a time, restores the before-image and verifies the candidate diff after each mutation. Exact combined patches from the Q1 parent are in `campaign/nc-01.patch` through `nc-08.patch`. Compare them to `controls-candidate.patch` for each isolated edit. The TRX files capture the detecting assertion and all other test rows.

## Platforms and attempts

The build is Debug net8.0. Windows used SDK 10.0.401, the xUnit adapter via VSTest, and .NET 8.0.31. Ubuntu 26.04.1 WSL2 used SDK 10.0.114, VSTest 18.0.2 and Linux .NET 10.0.14 selected explicitly through `DOTNET_ROLL_FORWARD=Major`. The same managed Windows build ran on the Linux CLR and sockets. Runtime/OS details, per-run durations and counts are in the SDK logs and TRX. Native Linux and Linux .NET 8 coverage remain outstanding.

The first production build failed when the local cancellation function was scoped inside the wrong registration block. Its log is preserved. The next build and full campaigns passed. A later refinement took the disposal-confirmation timestamp before final diagnostic subscribers could delay it. The earlier full campaigns and controls are preserved under `before-disposal-timestamp-refinement`. Final campaigns verified the refined source. The expected failed baseline and mutant test runs remain in the main campaign directory.

## Negative controls

| ID | Injected fault | Intended detection |
| --- | --- | --- |
| NC-01 | Restore Ready after terminal classification and feed the next parsed frame to the session | Small and delayed tails each hit the zero-provider assertion with one provider preparation. The exact terminal refusal was received before the assertion. |
| NC-02 | Bypass socket disposal in the helper and its enclosing owner | After listener stop, the captured handle remained open. |
| NC-03 | Remove the listener token registration | The admitted drain returned on drain-deadline instead of listener-shutdown. |
| NC-04 | Renew the total timer at drain entry | The stalled drain used its own full phase budget instead of the remaining absolute budget. |
| NC-05 | Raise the cap to 12 KiB | The large tail observed 12,288 discarded bytes against the 8,192-byte expectation. |
| NC-06 | Increase limit to two permits | The three-contender test observed two admitted drains against one. |
| NC-07 | Permit helper entry on TLS, with an encrypted writer and a zero-permit policy | The peer received the exact encrypted refusal and the forbidden plaintext-helper events appeared. No raw TLS-owned data was read. |
| NC-08 | Keep the short timer armed through MessageProcessed and give it owner-close authority before leaving the write phase | The 140 ms post-flush delay row asserted expected peer-eof and got write-deadline, no admission and one failure, on Windows and WSL2. Additional rows give corroboration; the delay row independently detects the intended defect. |

The clean control campaign passed before and after mutation. NC-01 through NC-07 ran on Windows. NC-08 ran on both platforms. No canonical source was mutated for these checks.
