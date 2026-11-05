# B0-A-TRACE-Q2 reviewer probes

These tests intentionally retain failing contract assertions. They are separate from the solution.
The final exact-source result is 20 passed / 6 failed, plus a separate GetStream hook result of
1 passed / 1 failed. See the reviewer report for classification findings and evidence limitations.

From the repository root, a direct exact-source reproduction is:

```powershell
dotnet build docs/reviews/probes/B0-A-TRACE-Q2/B0ATRACEQ2.Probes.csproj -c Debug --no-incremental
dotnet test docs/reviews/probes/B0-A-TRACE-Q2/B0ATRACEQ2.Probes.csproj -c Debug --no-build
```

The default project links the qualified listener without modification. Reflection creates
controlled local lifecycle states. The stream adapter owns its socket and delegates native
reads to an exact NetworkStream. Synthetic exception injection and preamble delivery are
explicitly separate from the authentic driver run. Eight-second waits detect deadlock; task
signals establish phases. Forty iterations exercise four ordered shutdown alternatives, with
task/client checks each time and port-refusal sampling every tenth iteration.

The Python runners preserve the actual sequence, including superseded adapter runs. Their
recorded per-command exits are authoritative: a Python orchestration exit 0 does not mean the
assertions passed. `Run-Qualification.py` checks the original pinned entry HEAD and is an
execution record for that entry state; it deliberately refuses a changed HEAD, including the
later additive reviewer commit. It is not an instruction to reset or rewrite the checkout.

The pre-fix and single-call GetStream-hook listener snapshots are under new Q2 evidence paths.
`source-controls.json` records the Git source identity and the exact one-expression hook
transformation. `Run-Final.py` shows their project-source override arguments. No source under
production, producer tests or historical review artifacts is edited by these controls.

`Audit-Results.py` independently asserts handshake byte identity and sanitized framing metadata,
and checks TRX results against counters. `Finalize-Review.py` seals the report/artifact/evidence
manifests after testing. It also checks the pinned entry HEAD, and should not be used to
overwrite a committed review. Final manifests and source/assembly hashes identify the delivered
review; the historical manifests remain unchanged.
