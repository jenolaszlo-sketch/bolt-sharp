# B2-B-Q1 reviewer probes

Reviewer-owned net8.0 project, excluded from the main solution. Production files and existing
test projects are unchanged. There are 67 discovered cases across 16 test methods; loops inside
cases exercise wire-space and transition matrices without inflating the discovery count.

From the canonical repository root:

```powershell
dotnet test docs/reviews/probes/B2-B-Q1/B2BQ1.Probes.csproj -c Debug
```

Expected at implementation `ca36d36`: **63 passed, 4 failed, 0 skipped**. The failures are
intentional preserved regression assertions for finding **B2BQ1-F01**, not expected-failure
tests that conceal a defective result. A successful fix should make them pass.

Minimal defect reproduction:

```powershell
dotnet test docs/reviews/probes/B2-B-Q1/B2BQ1.Probes.csproj -c Debug --filter 'FullyQualifiedName~Zero_version_must_never|FullyQualifiedName~Range_containing_zero'
```

This selects the four failing sync/async cases. Its command is provided for reproduction;
the retained evidence is from the executed full 67-case run.

`Run-Evidence.ps1 -IncludeReviewer` captures the build, all existing tests, and reviewer probes.
It deliberately records nonzero subprocess exit codes rather than hiding them in an overall
PowerShell success status. `Verify-Evidence.ps1` checks ancestry/preservation and derives counts
from individual TRX result entries and assembly identities, then hashes evidence files.
Rerunning overwrites evidence: use a separate evidence directory or preserve the current commit
before producing a later qualification record.

The report's inventory maps every method to a falsification claim and plausible mutation.
GuardStream supplies separate input/output, read-size guards, fragmentation, deterministic
cancellation, partial-response write failure, flush/read exceptions, and ownership observations.
Literal hex/radix arithmetic constructs fixtures independently of production encoders and
producer test helpers. The version writer is compared with all 65,536 manually indexed concrete
byte layouts. No production mutation was performed.
