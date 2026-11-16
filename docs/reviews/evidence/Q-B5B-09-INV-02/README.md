# Q-B5B-09-INV-02 evidence

[Investigation report](../../Q-B5B-09-INV-02-investigation.md)

- [source-identity.json](source-identity.json) pins the archived source and the hashes of the four replayed candidate source files.
- [prototype.patch](prototype.patch) applies only to a disposable extraction. The production path is used directly.
- [wsl-listener-adapter.patch](wsl-listener-adapter.patch) is the separate, common WSL comparison adapter.
- [transport fixture](Inv02TerminalTests.cs.txt) and [driver fixture](Inv02DriverTests.cs.txt) are identical in the two variants.
- [matrix-observations.txt](matrix-observations.txt) is a readable projection of per-case stdout in TRX. The latter retains the exact observed Bolt payload hex.
- [execution-records.zip](execution-records.zip) contains the original matrix, core, driver, and control logs and TRX records, including aborted and failed runs.
- [controls-results.json](controls-results.json) and [cancel-timing-results.json](cancel-timing-results.json) summarize the source-level controls.

## Replay the isolated sources

Create two empty temporary directories outside the canonical checkout. Use Git to export
23d58da4e0c0e10aa46a31f05ef6a0c31ad3f2fa to a tar archive and extract it into both directories.
Set core.autocrlf=false for the export and application. If the export used Windows EOL conversion,
normalize CRLF to LF in the three existing patch targets before applying the patch. The patch
creates BoltTerminalClose.cs. Run the following from the candidate extraction, with the
absolute path of the evidence directory set by the caller:

```text
git -c core.autocrlf=false apply --check <evidence-directory>/prototype.patch
git -c core.autocrlf=false apply <evidence-directory>/prototype.patch
```

In **both** extractions, copy the transport fixture to
tests/BoltSharp.Tests/Transport/Inv02TerminalTests.cs and the driver fixture to
tests/BoltSharp.DriverInteropTests/Scenarios/Inv02DriverTests.cs. Keep the original file
names in the canonical evidence package. Fixtures use fixed loopback listeners and ephemeral
ports they create. The TLS certificate is ephemeral.

## Windows execution

.NET SDK 10.0.401 and runtime 8.0.31 were available for the net8.0 projects. Restore the two
projects from the configured NuGet source. The original Windows execution restored from the
local NuGet cache with NuGet audit disabled for that offline run. Set the candidate flag to
0 for the baseline or 1 for the candidate. Use separate result directories for each variant.

```powershell
$env:INV02_EXPECT_CANDIDATE = '1'
$core = 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj'
$driver = 'tests/BoltSharp.DriverInteropTests/BoltSharp.DriverInteropTests.csproj'
dotnet restore $core
dotnet restore $driver
dotnet test $core --no-restore --filter 'FullyQualifiedName~Inv02TerminalTests' --logger 'trx;LogFileName=matrix.trx'
dotnet test $core --no-restore --filter 'FullyQualifiedName!~Inv02TerminalTests' --logger 'trx;LogFileName=core.trx'
dotnet test $driver --no-restore --logger 'trx;LogFileName=driver.trx'
```

The initial Windows test-host listener required loopback support, and the sandboxed test
host aborted before running tests. Those logs are preserved alongside the authorized runs.

## WSL2 and native Linux

The initial WSL2 matrix used Windows-built portable managed test artifacts with .NET
roll-forward to the installed Linux runtime 10.0.14. Both exact variants stopped at the
shared SO_LINGER startup call. In fresh isolated comparison copies, apply
wsl-listener-adapter.patch to both variants, rebuild and repeat. The adapter uses the
typed disabled LingerOption without changing the candidate lifecycle.

For native Linux, use a separately available, authorized system with the .NET 8 runtime,
a compatible SDK, and restored NuGet packages. Run the exact-source matrix first using
the commands below. Record OS, kernel, build/runtime versions, and whether the original
linger call fails. Use a second pair with the shared typed adapter if needed, and retain
both result sets.

```sh
uname -sr
dotnet --info
export INV02_EXPECT_CANDIDATE=1
core=tests/BoltSharp.Tests/BoltSharp.Tests.csproj
driver=tests/BoltSharp.DriverInteropTests/BoltSharp.DriverInteropTests.csproj
dotnet test "$core" --filter 'FullyQualifiedName~Inv02TerminalTests' --logger 'trx;LogFileName=matrix.trx'
dotnet test "$core" --filter 'FullyQualifiedName!~Inv02TerminalTests' --logger 'trx;LogFileName=core.trx'
dotnet test "$driver" --logger 'trx;LogFileName=driver.trx'
```

The executed WSL2 run invoked dotnet vstest on the managed core test DLL with the same
matrix filter and DOTNET_ROLL_FORWARD=Major. That environment is separate from a native
Linux .NET 8 qualification.

## Evidence verification

With Git and Python 3, run from the repository root at the investigation commit:

```text
python docs/reviews/evidence/Q-B5B-09-INV-02/verify-evidence.py
```

The verifier can also take an explicit investigation commit hash. Each entry records the
SHA-256 and byte count of the exact blob returned by git cat-file blob. The manifest omits
its own payload from the inventory. Its bytes are pinned by the Git tree. The verifier
requires the exact INV-01 parent tree, the entire package inventory, matching blob bytes,
a valid execution ZIP and an additions-only investigation scope.
