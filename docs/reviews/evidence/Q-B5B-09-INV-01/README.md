# Q-B5B-09-INV-01 evidence

[Investigation and candidate comparison](../../Q-B5B-09-INV-01-investigation.md)

| File | Contents |
| --- | --- |
| `tcp-close-harness.py` | Fixed in-process IPv4 loopback matrix using synthetic request/response bytes and ephemeral listeners. |
| `windows.json`, `wsl2.json` | Complete per-trial measurements, including observed hex bytes, peer end condition, completed writes, terminal flag, byte counts, close time and group accounting. |
| `summarize.py`, `matrix-summary.txt` | Reproducible aggregation and per-cell observed results. |
| `driver-baseline.log`, `driver-baseline.trx` | Restore, build and test evidence from a Windows archive of the entry revision. |
| `manifest.json`, `verify-evidence.py` | Git blob hashes, byte counts, parent identity, inventory and scope verification. |

## Matrix reproduction

From this directory, use CPython 3.11 or later (3.14 tested). Use Windows and Linux
shells in their respective environments. The test creates both peers. It accepts one
optional output-file path and uses the standard library.

```text
python tcp-close-harness.py result.json
python summarize.py result.json
```

The WSL2 run replayed the same script via standard input to Python. Each platform
runs four close modes and fourteen cases with five groups each. Each concurrency
group contains six connections. The fixed socket test uses a one-second deadline
per group and a 45-second campaign limit, with a 75 ms drain timer, 8 KiB discard cap
and 2 KiB receive buffer. Each continuous-input peer attempts up to sixteen writes
within the fixed test schedule. The harness compares exact synthetic reply bytes.
The no-read case records no application receipt observation. The byte and timer
caps bound the experiment while allowing scheduler latency at close. The synthetic
counters track actual local socket disposal in the harness.

## Driver baseline reproduction

From an isolated archive at `29d521cea9e65fe2d240421156454444e0d055ea` containing
`src`, `tests`, and `Directory.Build.props`, restore from the local NuGet cache
with NuGet auditing disabled for the offline run, then invoke the existing tests
using .NET SDK 10.0.401 and runtime 8.0.31. The archive has no candidate code.

```powershell
$project = 'tests/BoltSharp.DriverInteropTests/BoltSharp.DriverInteropTests.csproj'
$filter = 'FullyQualifiedName~B5B10_05|FullyQualifiedName~B5B9_01|FullyQualifiedName~B5B9_03|FullyQualifiedName~B5B7_01'
dotnet restore $project --source $env:NUGET_PACKAGES -p:NuGetAudit=false -nodeReuse:false
dotnet test $project --no-restore -c Debug -nodeReuse:false --filter $filter --logger 'trx;LogFileName=driver-baseline.trx' --results-directory .
```

Historical exact Bolt response and terminality evidence is linked in the report.
The new raw TCP observations and existing driver assertions cover different parts of the
investigation.

## Verification

Run from the canonical repository root after checkout of the investigation commit, or
pass its commit ID as the sole argument from a later checkout.

```text
python docs/reviews/evidence/Q-B5B-09-INV-01/verify-evidence.py
```

The manifest records SHA-256 and byte length of each exact Git blob payload as returned
by `git cat-file blob <investigation-commit>:<repository-path>`. It includes the report
and each evidence file except itself. The commit tree pins the manifest. The staged
evidence uses LF line endings, including the archived harness, summaries, raw JSON
results and retained driver transcripts. The verifier reads the committed manifest,
checks every entry and the package inventory, and requires an additions-only commit
with the exact verified owner parent and tree.
