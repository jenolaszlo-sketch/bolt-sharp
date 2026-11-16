# Q-B5B-09-PLAIN-IMPL-01 producer evidence

Run from the repository root after checking out the evidence commit.

```powershell
python docs/reviews/evidence/Q-B5B-09-PLAIN-IMPL-01/verify-evidence.py HEAD
```

The verifier reads bytes with `git cat-file blob <revision>:<path>` and compares exact byte counts and SHA-256 digests. It verifies the evidence, implementation and linger inventories and parent chain, replays the pinned owner verifier, and checks the archived test results and mutation detections. The manifest excludes itself. The report and all other package entries are hashed. Source digests match the tested files after Windows checkout line endings are converted to the repository's LF representation at staging. The ZIP preserves original log, patch, and TRX bytes. Its separate index records each member's SHA-256 and length.

## Commands

Run from a clean checkout of implementation `20fbc461c0257620db28fb78a463218b242455bc`. The historical producer used an isolated Git archive plus the authorized edits, and compared the staged blobs with the inputs. Use the standard NuGet package source or its cached packages. `$results` is an absolute writable result directory. No changes to runtime source or dependency files are necessary.

```powershell
$core = 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj'
$driver = 'tests/BoltSharp.DriverInteropTests/BoltSharp.DriverInteropTests.csproj'
dotnet restore BoltSharp.slnx
dotnet restore $driver
dotnet build src/BoltSharp/BoltSharp.csproj --no-restore -nodeReuse:false
dotnet build BoltSharp.slnx --no-restore -nodeReuse:false
dotnet build $driver --no-restore -nodeReuse:false
dotnet test $core --no-build --no-restore -nodeReuse:false --logger 'trx;LogFileName=win-core-final.trx' --results-directory $results
dotnet test $driver --no-build --no-restore -nodeReuse:false --logger 'trx;LogFileName=win-driver-final.trx' --results-directory $results
$scope = git diff --name-only fac10f6c5ece37dbc3f38c9c831c668d49a3d290 20fbc461c0257620db28fb78a463218b242455bc
$driverScope = $scope | Where-Object { $_ -like 'tests/BoltSharp.DriverInteropTests/*' }
$coreScope = $scope | Where-Object { $_ -notin $driverScope }
dotnet format BoltSharp.slnx --verify-no-changes --no-restore --include $coreScope
dotnet format $driver --verify-no-changes --no-restore --include $driverScope
git diff --check fac10f6c5ece37dbc3f38c9c831c668d49a3d290 HEAD
```

The producer used Debug configuration and the listed SDK and runtime. The Windows driver project ran separately because it sits outside the core solution. On Ubuntu WSL2 run the built assemblies with the installed native Linux runtime, from the same checkout, using an absolute WSL path in `$results`.

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet vstest tests/BoltSharp.Tests/bin/Debug/net8.0/BoltSharp.Tests.dll \
  '--Logger:trx;LogFileName=wsl-core-final.trx' "--ResultsDirectory:$results"
dotnet vstest tests/BoltSharp.DriverInteropTests/bin/Debug/net8.0/BoltSharp.DriverInteropTests.dll \
  '--Logger:trx;LogFileName=wsl-driver-final.trx' "--ResultsDirectory:$results"
```

The standalone and new scenario test cases are also visible in each full TRX. The pressure cases require a pending terminal write and verify the earlier normal response was completed. A run that never gets a persistent terminal await fails the oracle. The initial WSL2 run, refined pressure run and final run are all archived.

## Isolated comparisons and controls

For the linger-only comparison use a disposable checkout of `dac52d55ade0a9ae703b801c8a4066e8aef2bd58`. Add the new core test harness from the implementation revision and relax the two receipt assertions in `QueuedTailComparison` for this baseline observation. Run with the combined filter `FullyQualifiedName~Startup_UsesDisabledLinger|FullyQualifiedName~QueuedTailComparison`. The final implementation comparisons retain both assertions. For direct accepted-socket readback on the linger commit, apply only `linger-readback-observer.patch` from the archive and run with `FullyQualifiedName~Startup_UsesDisabledLinger`. The supplemental probe reads the accepted socket's option without changing it. The initial WSL2 readback log includes a logger-quoting shell error after two passing tests. The successful rerun used the quoted options shown above and the startup filter. The probe was outside the production runs.

Each mutation used an isolated copy of the production entry point. `negative-controls.json` records the changed paths, exact behavioral edits, per-case test filter, expected violation, observed assertion and restoration checks. Execute each case on Windows with `dotnet test $core --no-restore -nodeReuse:false --filter <test_filter> --logger 'trx;LogFileName=NC-0n.trx' --results-directory $results`. The archived `NC-00` run is the unmodified control. A failure in the directed oracle is the expected result for NC-01 through NC-07. Compare source hashes before and after each isolated change.

[Producer report](../../Q-B5B-09-PLAIN-IMPL-01-implementation.md)
