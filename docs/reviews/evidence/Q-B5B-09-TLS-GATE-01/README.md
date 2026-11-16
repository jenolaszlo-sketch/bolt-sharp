# Q-B5B-09-TLS-GATE-01 evidence

[Report and owner decision options](../../Q-B5B-09-TLS-GATE-01-investigation.md)

- [predeclared configuration](configuration.json) fixes the cell order, per-platform budget and link profiles.
- [source identity](source-identity.json) records the exact archive and candidate source hashes, patch hashes, fixture and configuration hashes, and the WSL adapter identity.
- [link observer patch](link-observation.patch) adds an in-process test notification to the isolated copy.
- [TLS qualification fixture](TlsGate01Tests.cs.txt) owns both ends of an ephemeral IPv4 loopback connection.
- [Windows journal](windows.jsonl), [WSL2 journal](wsl2.jsonl), and [matrix](matrix.json) record all attempts, including failures to induce the requested condition.
- [Execution records](execution-records.zip) contains original test-run logs and TRX. The pass count is a campaign-runner result; the journal records whether each gate actually passed.

## Reconstruct the isolated candidate

Export repository commit 23d58da4e0c0e10aa46a31f05ef6a0c31ad3f2fa with Git archive into a
new temporary directory. Set Git core.autocrlf=false for archive and patch application.
For an old archive made with Windows CRLF conversion, normalize the existing patch
targets to LF first. In the extracted copy, apply the INV-02 prototype patch, the
INV-03 refinement patch, and the INV-03 real-write-observation patch. Compare the
source hashes in source-identity.json before applying link-observation.patch. Copy
TlsGate01Tests.cs.txt to tests/BoltSharp.Tests/Transport/TlsGate01Tests.cs. All
patches and fixture files belong to the disposable copy.

The Windows qualification used SDK 10.0.401 and .NET 8.0.31. Restore the core project
from an authorized package source. The executed offline restore used the local NuGet
cache. From the extracted candidate, set the journal to a new absolute path:

```powershell
$project = 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj'
$env:BOLTSHARP_TLS_GATE_JOURNAL = '<new absolute Windows journal path>'
dotnet restore $project
dotnet test $project --no-restore --filter 'FullyQualifiedName~TlsGate01Tests.FixedCampaign' --blame-hang-timeout 75s --logger 'trx;LogFileName=tls-gate.trx'
```

For adapted WSL2, reproduce a separate copy and additionally apply the INV-02 typed
linger adapter. The executed WSL2 campaign used the portable net8.0 test assembly
built with Windows SDK 10.0.401 and the existing Linux .NET 10.0.14 runtime via
DOTNET_ROLL_FORWARD=Major. Invoke dotnet vstest on the assembly with the same filter
and an 80-second external watchdog. Set the journal path to a new absolute WSL2 path.
This replay is a WSL2 comparison. Native Linux release qualification remains open.

The fixture refuses to use an existing journal file. The predeclared policy stops a
cell after a qualifying result and allocates at most three attempts per cell per
platform. A journal start without a completion is an attempted scenario. Keep all
starts, exits and partial records. Reproduction commands are for separately
authorized replay; this commission consumed its fixed budget.

## Verify committed evidence

From the repository root, run the verifier at the investigation commit or give
its hash as the sole argument:

```text
python docs/reviews/evidence/Q-B5B-09-TLS-GATE-01/verify-evidence.py
```

The manifest covers SHA-256 and byte count over exact Git blob payloads. The
commit tree pins the manifest. The verifier checks the exact INV-03 parent,
inventory, additions-only scope, all hashes, archive CRCs, and consistency of the
attempt journals with the bounded matrix.
