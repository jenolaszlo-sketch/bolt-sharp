# B0-B-Q1 reviewer negative controls

Reviewer-only probe. It links the producer harness infrastructure source files read-only from
`tests/BoltSharp.IntegrationTests/Infrastructure/` and runs copies of selected producer assertions
against a server script mutated in exactly one place per control.

- `DETECTED` means the copied producer assertion (or the producer test's uncaught driver call)
  failed against the mutation, so the producer test is sensitive to that defect.
- `NOT_DETECTED` means the copied producer assertions still passed, documenting an assertion blind
  spot rather than a harness pass.

Build and run:

```powershell
dotnet build docs/reviews/probes/B0-B-Q1/NegativeControls/NegativeControls.csproj -c Release
dotnet run --project docs/reviews/probes/B0-B-Q1/NegativeControls/NegativeControls.csproj -c Release -- docs/reviews/evidence/B0-B-Q1/negative-controls
```

The console writes one `summary.json`. Exit code 0 means every control produced its expected
detection disposition; exit code 4 means a control did not.
