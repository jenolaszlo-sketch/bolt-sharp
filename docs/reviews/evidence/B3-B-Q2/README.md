# B3-B-Q2 retained reviewer evidence

Reviewed main: `801badcea8523372d4ed83f8f2942d80bc0373e1`, tree
`243998eb21d01aa918f4b83143cb8a0c6bb27dd1`; R1 implementation `454ae005245d6511b672f8962092829839fb7153`.
See `../../B3-B-Q2.md` for the disposition and contract adjudication.

## Reproduction

Use a fresh absolute scratch directory outside the canonical checkout. The scripts do not create
a worktree or change production/tests. `Run-Regression.ps1` deliberately checks the reviewed
baseline HEAD/tree; use an authorized checkout at that revision for a later reproduction, or
explicitly review/update the guard for a newly commissioned revision. Do not reset a live checkout
just to satisfy it. The final Q2 documentation commit necessarily has a different HEAD/tree.

```powershell
$evidence = 'D:\projects\bolt-sharp\docs\reviews\evidence\B3-B-Q2'
$scratch = 'C:\review-scratch\B3-B-Q2'
& "$evidence\Run-Regression.ps1" -Repo 'D:\projects\bolt-sharp' -Scratch $scratch
& "$evidence\Run-Q2.ps1" -Scratch $scratch -Probe "$evidence\probe"
```

The scripts log native exit codes; their PowerShell wrapper exit status is not the assertion
result. Expected reviewed-baseline probe exits are **4**, not 0: Q1 has the stale X04 mismatch;
Q2 has 18 failed assertions grouped into two material findings. The report records these as
failures, not test exclusions or expected-pass substitutions. Core/session/remediation/message/
integration tests and builds exit 0. No historical Q1 source or evidence was edited.

## Artifact map

- `commission.md`: exact supplied review commission.
- `baseline.json`, `history-audit.txt`, `reviewed-inputs.json`: actual review provenance.
- `historical-integrity.json`, `q1-input-hashes.json`: 26 original manifest entries verified;
  original Q1 input hashes retained for preservation checks.
- `implementation-delta.patch`: R1 source/test delta; `reviewed-session-numbered.txt` and
  `prior-session.txt`: text audit snapshots, not compilable remediation files.
- `*-build.log`, `*-tests.log`, `*.trx`: complete test/build results. Filtered tests overlap core.
- `q1-unchanged/`: original Q1 execution output, including its hardcoded old revision fields.
  The actual remediated DLL is identified by the current build/provenance, not those constants.
- `original-cleanup-subset.json`: X01-X05 extracted from that same run, with no changes or separate
  rerun claim. X01-X03's old formatted count is literal; use Q2 measured counters for proof.
- `probe/`, `Run-Q2.ps1`, `q2-probe-results.json`, `q2-run.log`: independently authored terminal,
  ownership, recovery, diagnostic, metadata, and state-matrix probes, 181 checks / 217 trace steps.
  The resource-live flag belongs to a simulated provider and changes only on successful disposal.
  Diagnostic mutation/latest-value checks characterize behavior, not its safety as a recovery rule.
- `environment.txt`, `manifest.json`: toolchain and SHA-256 evidence inventory. No binaries retained.

Reviewer tooling notes: the first Q2 build had two CS8602 warnings in diagnostic formatting;
the final source adds nullability-safe accesses, and rerunning yields the identical failure set
with zero warnings. Initial build/run logs are retained. A read-only audit command initially had
a PowerShell parse error before execution and was corrected. The initial regression baseline
text rendered nested arrays as `System.Object[]`; `baseline.json` is the corrected complete
capture, taken while repository HEAD/tree/status still exactly matched entry. `baseline.txt`
is retained as that initial capture, not relied on for detailed history/worktrees. The retained
regression script uses corrected JSON capture and requires an external scratch argument.

No separate standalone historical cleanup harness exists in the tracked B3-B review artifacts;
the original independent cleanup probes are Q1's `CleanupFailure()` / X01-X05 and execute in the
unchanged full Q1 program. The remediation class actually discovers 21 cases despite the R1
report's “20” description. All 21 passed.
