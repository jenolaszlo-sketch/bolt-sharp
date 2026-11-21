# Q-B5B-09-TLS-E1 evidence

**TLS graceful-close investigation, bounded; outcome B — supported with qualifications.**

- Entry commit / tree: `250fb99273f975ae6552eb4e0ebefcc149796fa7` / `bb69f2e3986dbae6a5db705c20690cb9f1cb9568`
- Investigation report: [Q-B5B-09-TLS-E1-report.md](../Q-B5B-09-TLS-E1-report.md)
- Predeclared matrix: [commissioning-matrix.json](commissioning-matrix.json)
- Accepted budget reconciliation: [commissioning-budget-reconciliation.json](commissioning-budget-reconciliation.json)
- Journal consistency and checksummed evidence verifier: [verify-evidence.py](verify-evidence.py)
- Complete inventory:
  - [source-identity.json](source-identity.json)
  - [commissioning-matrix.json](commissioning-matrix.json)
  - [configuration.json](configuration.json)
  - [matrix.json](matrix.json)
  - [commissioning-budget-reconciliation.json](commissioning-budget-reconciliation.json)
  - [TlsE1Tests.cs.txt](TlsE1Tests.cs.txt)
  - [TlsE1MechanismTests.cs.txt](TlsE1MechanismTests.cs.txt)
  - [ProductionTlsCloseTests.cs.txt](ProductionTlsCloseTests.cs.txt)
  - [windows-campaign-first-run.jsonl](windows-campaign-first-run.jsonl)
  - [windows-campaign.jsonl](windows-campaign.jsonl)
  - [wsl2-campaign.jsonl](wsl2-campaign.jsonl)
  - [wsl2-campaign-repeat.jsonl](wsl2-campaign-repeat.jsonl)
  - [windows-mechanism.jsonl](windows-mechanism.jsonl)
  - [wsl2-mechanism.jsonl](wsl2-mechanism.jsonl)
  - [windows-production.jsonl](windows-production.jsonl)
  - [wsl2-production.jsonl](wsl2-production.jsonl)
  - [wsl2-production-repeat.jsonl](wsl2-production-repeat.jsonl)
  - [nc1-cipher-cap-tamper.jsonl](nc1-cipher-cap-tamper.jsonl)
  - [nc2-deadline-renewal.jsonl](nc2-deadline-renewal.jsonl)
  - [windows-mechanism.log](transcripts/windows-mechanism.log)
  - [windows-campaign.log](transcripts/windows-campaign.log)
  - [windows-production.log](transcripts/windows-production.log)
  - [wsl2-campaign.log](transcripts/wsl2-campaign.log)
  - [wsl2-campaign-repeat.log](transcripts/wsl2-campaign-repeat.log)
  - [wsl2-production.log](transcripts/wsl2-production.log)
  - [wsl2-production-repeat.log](transcripts/wsl2-production-repeat.log)
  - [nc1-cipher-cap-tamper.log](transcripts/nc1-cipher-cap-tamper.log)
  - [nc2-deadline-renewal.log](transcripts/nc2-deadline-renewal.log)
  - Runner scripts: [run-wsl2-candidate.sh](run-wsl2-candidate.sh), [run-wsl2-candidate-repeat.sh](run-wsl2-candidate-repeat.sh), [run-wsl2-production.sh](run-wsl2-production.sh), [run-wsl2-production-repeat.sh](run-wsl2-production-repeat.sh)
  - [.gitattributes](.gitattributes)

## Budget reconciliation

`commissioning-matrix.json` contains `windows_platform_attempts_total: 21` and `wsl2_campaign_attempts_total: 21`. Those fields are not compatible with the same file's explicit per-cell repeat rule: up to six runs for T1–T4 and three for T5/T6. The effective and predeclared bounds are:

- Discovery attempts per cell: 4
- Qualifying repeats after discovery: 2
- Maximum runs per cell: 6 (3 for cap cells)
- Maximum attempt lifetime: 3500 ms

Executed final candidate attempts were 30 on Windows and 26 on WSL2; the retained first Windows run, controls, mechanism probes, and production runs are separately enumerated in [commissioning-budget-reconciliation.json](commissioning-budget-reconciliation.json). The investigation verdict stays B: no per-case or per-cell bound was exceeded, and the stale aggregate record is disclosed rather than used as a claim.

## Reconstruct the isolated candidate

All temporary files belong in a fresh disposable copy. It is not the production checkout.

```powershell
git -c core.autocrlf=false archive --format=tar --output base.tar 23d58da4e0c0e10aa46a31f05ef6a0c31ad3f2fa
tar -xf base.tar
# IN the temp copy:
git apply <repo>/docs/reviews/evidence/Q-B5B-09-TLS-GATE-01/link-observation.patch
```

The exact order from the replay procedure is:

1. Export base `23d58da4e0c0e10aa46a31f05ef6a0c31ad3f2fa`.
2. Apply the INV-02 prototype patch.
3. Apply the INV-03 refinement patch.
4. Apply the INV-03 real-write-observation patch.
5. Apply the TLS-GATE-01 link-observation patch.
6. Compare `source-identity.json` hashes.
7. Copy `TlsE1Tests.cs.txt` to the temp copy's `tests/BoltSharp.Tests/Transport/TlsE1Tests.cs`.
8. Copy `TlsE1MechanismTests.cs.txt` and `ProductionTlsCloseTests.cs.txt` as executed fixtures.

For the separate adapted WSL2 candidate, also apply the INV-02 typed-linger WSL2 listener adapter. Verify that the adapted listener source hash is `c935031985cd921eb9e56857ca6ffe8b838c4f4e33747e96b853d9c69ebb29c7`.

Use fresh absolute journal paths for every campaign. The fixtures refuse to overwrite an existing journal.

Windows candidate commands (per the runner comments):

```text
dotnet restore tests/BoltSharp.Tests/BoltSharp.Tests.csproj
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj --no-restore --filter "FullyQualifiedName~TlsE1Tests.FixedCampaign"
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj --no-restore --filter "FullyQualifiedName~TlsE1MechanismTests"
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj --no-restore --filter "FullyQualifiedName~ProductionTlsCloseTests"
```

For production characterization, no TLS-GATE-01 link patch or prototype patch is needed; exercise the production HEAD directly.

WSL2 used the `dotnet vstest` runner because bare `--filter` and spaced `--TestCaseFilter` were both unavailable:

```text
dotnet vstest BoltSharp.Tests.dll "--TestCaseFilter:FullyQualifiedName~TlsE1Tests.FixedCampaign"
dotnet vstest BoltSharp.Tests.dll "--TestCaseFilter:FullyQualifiedName~TlsE1MechanismTests"
dotnet vstest BoltSharp.Tests.dll "--TestCaseFilter:FullyQualifiedName~ProductionTlsCloseTests"
```

The scripts set a fresh journal path and `DOTNET_ROLL_FORWARD=Major`. Windows SDK 10.0.401 built the portable assemblies; the installed WSL2 Linux .NET 10.0.14 runtime executed them. This is WSL2, not native Linux.

## Verify committed evidence

```powershell
python docs/reviews/evidence/Q-B5B-09-TLS-E1/verify-evidence.py
```

The verifier checks the exact parent, canonical tree, inventory, all additions-only changes, blob SHA-256 and byte counts, journal structure, per-cell discovery/repeat/cap bounds, encrypted and plaintext caps, transcript and runner identities, and campaign outcome summaries.
