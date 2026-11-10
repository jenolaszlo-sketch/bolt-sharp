# B5-B-Q1 independent reviewer regression reproduction.
# Re-runs the full accepted regression baseline at the reviewer's starting HEAD.
$ErrorActionPreference = 'Continue'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
Set-Location $repoRoot

$ev = $PSScriptRoot
"branch: $(git branch --show-current)" | Out-File -FilePath (Join-Path $ev '02-regression-preflight.txt') -Encoding utf8
"HEAD: $(git rev-parse HEAD)" | Out-File (Join-Path $ev '02-regression-preflight.txt') -Append -Encoding utf8
"tree: $(git rev-parse 'HEAD^{tree}')" | Out-File (Join-Path $ev '02-regression-preflight.txt') -Append -Encoding utf8
"dotnet: $(dotnet --version)" | Out-File (Join-Path $ev '02-regression-preflight.txt') -Append -Encoding utf8
"os: $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)" | Out-File (Join-Path $ev '02-regression-preflight.txt') -Append -Encoding utf8

# Builds.
dotnet build src/BoltSharp/BoltSharp.csproj -c Debug --no-incremental --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '03-build-production.txt') -Encoding utf8
dotnet build BoltSharp.slnx -c Debug --no-incremental --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '04-build-solution.txt') -Encoding utf8

# B5-B producer live-driver suite (regression evidence only).
dotnet test tests/BoltSharp.DriverInteropTests/BoltSharp.DriverInteropTests.csproj -c Debug `
    --logger 'trx;LogFileName=b5bq1-producer-suite.trx' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '05-producer-interop-suite.txt') -Encoding utf8

# Scoped suites.
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --filter 'FullyQualifiedName~BoltSharp.Tests.Transport' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '06-transport-suite.txt') -Encoding utf8
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --filter 'FullyQualifiedName~BoltSharp.Tests.Backend' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '07-b3c-backend.txt') -Encoding utf8
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --filter 'FullyQualifiedName~BoltSharp.Tests.Sessions' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '08-b3b-sessions.txt') -Encoding utf8
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --filter 'FullyQualifiedName~BoltSharp.Tests.Messages' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '09-b3a-messages.txt') -Encoding utf8
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --logger 'trx;LogFileName=b5bq1-full-regression.trx' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '10-full-regression.txt') -Encoding utf8
dotnet test tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj -c Debug --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '11-driver-integration-b0b.txt') -Encoding utf8

# Second fresh-process run of the B5-B producer suite (repeat stability).
dotnet test tests/BoltSharp.DriverInteropTests/BoltSharp.DriverInteropTests.csproj -c Debug --no-build --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '12-producer-interop-suite-repeat.txt') -Encoding utf8

'B5-B-Q1 regression reproduction complete' | Out-File (Join-Path $ev '13-regression-complete.txt') -Encoding utf8
