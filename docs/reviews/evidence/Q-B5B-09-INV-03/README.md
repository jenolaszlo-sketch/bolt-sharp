# Q-B5B-09-INV-03 evidence

[Investigation and decision gate](../../Q-B5B-09-INV-03-investigation.md)

- [source-identity.json](source-identity.json) records the verified original source archive, the INV-02 patch hash, the final replayed source hashes and the record count.
- [refinement.patch](refinement.patch) adds isolated phase, ownership and encrypted-write observation.
- [real-write-observation.patch](real-write-observation.patch) adds observation of actually pending inner writes.
- [Inv03QualificationTests.cs.txt](Inv03QualificationTests.cs.txt) contains the targeted loopback fixture.
- [campaign-summary.json](campaign-summary.json) projects the result counts and structured test stdout, including failures.
- [controls.json](controls.json) records the outcomes and restored SHA-256 for the source mutation controls.
- [execution-records.zip](execution-records.zip) retains the original logs and TRX records, including all three failing source mutation runs.
- [native-linux.sh](native-linux.sh) is the native Linux reproduction runner. It keeps the original and typed-adapter comparisons distinct.

## Replay and Windows campaigns

Extract an archive at 23d58da4e0c0e10aa46a31f05ef6a0c31ad3f2fa into a temporary directory.
Use core.autocrlf=false for archive and apply. If using an archive previously produced
under Windows conversion, normalize CRLF to LF in the known patch targets before
application. Apply the INV-02 prototype patch first, then the refinement patch and
real-write observation patch in that order. None apply in the canonical checkout.
Copy the INV-03 fixture to tests/BoltSharp.Tests/Transport/Inv03QualificationTests.cs.
Copy the two INV-02 fixtures to their original names and directories from the
[INV-02 replay instructions](../Q-B5B-09-INV-02/README.md). Dotnet target framework
is net8.0. The Windows run used SDK 10.0.401 and runtime 8.0.31.

```powershell
$core = 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj'
$driver = 'tests/BoltSharp.DriverInteropTests/BoltSharp.DriverInteropTests.csproj'
dotnet test $core --filter 'FullyQualifiedName~Inv03QualificationTests' --logger 'trx;LogFileName=gates.trx'
dotnet test $core --filter 'FullyQualifiedName~UnsupportedCommandRefusalTests|FullyQualifiedName~MultiClientTests|FullyQualifiedName~TlsIntegrationTests|FullyQualifiedName~ConnectionLifecycleTests' --logger 'trx;LogFileName=regression.trx'
dotnet test $driver --filter 'FullyQualifiedName~Inv02DriverTests|FullyQualifiedName~B5B10_05' --logger 'trx;LogFileName=driver.trx'
```

The Windows offline run restored from the authorized local cache with NuGet audit
disabled for that run. Cases create their own loopback endpoints. The managed TLS
stall and the real network observation are different cases in the fixture. The
authoritative classifications are in the report.

## WSL2 and native Linux

WSL2 used a separate archive with the INV-02 typed-linger adapter. The portable
net8.0 test assemblies were executed using dotnet vstest with the gate filter
and DOTNET_ROLL_FORWARD=Major on Linux .NET 10.0.14. The independent socket test
still issued the original bool and typed options. Keep the existing INV-02
exact-startup failure evidence.

On an authorized native Linux host with .NET 8, invoke bash with native-linux.sh,
the absolute checked-out repository path, and a new results directory. The runner
keeps exact and adapted runs in separate disposable directories, checks platform
and runtime, and preserves each process exit separately. The runner was syntax
checked locally. It remains unqualified on native Linux.

## Verify the evidence

Run from the repository root at the INV-03 commit, or pass its commit ID to
the verifier from a later checkout.

```text
python docs/reviews/evidence/Q-B5B-09-INV-03/verify-evidence.py
```

Each SHA-256 covers the exact Git blob payload returned by git cat-file blob,
with a separately verified byte count. The manifest excludes its own payload.
The verifier checks the complete inventory, exact parent, additions-only scope,
all hashes, and the execution archive's member count and CRCs.
