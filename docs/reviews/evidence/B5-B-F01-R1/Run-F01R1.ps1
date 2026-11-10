# B5-B-F01-R1 reproduction driver.
# Regenerates the evidence: preflight, builds, all regression suites, the corrected driver-suite
# scenario documents, the scoped format checks, the post-remediation reproduction probe,
# historical manifest verification and the evidence manifest.
$ErrorActionPreference = 'Continue'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
Set-Location $repoRoot

$ev = Join-Path $PSScriptRoot 'logs'
if (-not (Test-Path $ev)) { New-Item -ItemType Directory -Force -Path $ev | Out-Null }
$stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

"B5-B-F01-R1 reproduction run @ $stamp" | Out-File -FilePath (Join-Path $ev '01-preflight.txt') -Encoding utf8
"branch: $(git branch --show-current)" | Out-File (Join-Path $ev '01-preflight.txt') -Append -Encoding utf8
"HEAD: $(git rev-parse HEAD)" | Out-File (Join-Path $ev '01-preflight.txt') -Append -Encoding utf8
"tree:  $(git rev-parse 'HEAD^{tree}')" | Out-File (Join-Path $ev '01-preflight.txt') -Append -Encoding utf8
git worktree list | Out-File (Join-Path $ev '01-preflight.txt') -Append -Encoding utf8
git status -sb | Out-File (Join-Path $ev '01-preflight.txt') -Append -Encoding utf8
"dotnet --version: $(dotnet --version)" | Out-File (Join-Path $ev '01-preflight.txt') -Append -Encoding utf8
"os: $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)" | Out-File (Join-Path $ev '01-preflight.txt') -Append -Encoding utf8

# 1. Builds.
dotnet build src/BoltSharp/BoltSharp.csproj -c Debug --no-incremental --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '02-build-production.txt') -Encoding utf8
dotnet build BoltSharp.slnx -c Debug --no-incremental --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '03-build-solution.txt') -Encoding utf8
dotnet build tests/BoltSharp.DriverInteropTests/BoltSharp.DriverInteropTests.csproj -c Debug --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '04-build-driver-tests.txt') -Encoding utf8

# 2. Core suite and scoped suites.
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --logger 'trx;LogFileName=f01r1-full-regression.trx' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '05-core-full-regression.txt') -Encoding utf8
Copy-Item (Join-Path $repoRoot 'tests/BoltSharp.Tests/TestResults/f01r1-full-regression.trx') (Join-Path $PSScriptRoot 'f01r1-full-regression.trx') -Force
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --filter 'FullyQualifiedName~BoltSharp.Tests.Transport' --logger 'trx;LogFileName=f01r1-transport.trx' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '06-transport-suite.txt') -Encoding utf8
Copy-Item (Join-Path $repoRoot 'tests/BoltSharp.Tests/TestResults/f01r1-transport.trx') (Join-Path $PSScriptRoot 'f01r1-transport.trx') -Force
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --filter 'FullyQualifiedName~BoltSharp.Tests.Backend' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '07-b3c-backend.txt') -Encoding utf8
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --filter 'FullyQualifiedName~BoltSharp.Tests.Sessions' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '08-b3b-sessions.txt') -Encoding utf8
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --filter 'FullyQualifiedName~BoltSharp.Tests.Messages' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '09-b3a-messages.txt') -Encoding utf8

# 3. Official-driver suite with corrected scenario evidence documents.
$env:BOLTSHARP_B5B_EVIDENCE_DIR = Join-Path $PSScriptRoot 'driver-scenarios'
dotnet test tests/BoltSharp.DriverInteropTests/BoltSharp.DriverInteropTests.csproj -c Debug --no-build `
    --logger 'trx;LogFileName=f01r1-driver-suite.trx' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '10-driver-suite.txt') -Encoding utf8
Copy-Item (Join-Path $repoRoot 'tests/BoltSharp.DriverInteropTests/TestResults/f01r1-driver-suite.trx') (Join-Path $PSScriptRoot 'f01r1-driver-suite.trx') -Force
Remove-Item Env:\BOLTSHARP_B5B_EVIDENCE_DIR

# 4. Historical B0-B driver integration (unchanged project).
dotnet test tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj -c Debug --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '11-driver-integration-b0b.txt') -Encoding utf8

# 5. Scoped format checks (F01-R1 paths) plus the recorded pre-existing core observation.
dotnet format tests/BoltSharp.DriverInteropTests/BoltSharp.DriverInteropTests.csproj --verify-no-changes --no-restore 2>&1 |
    Out-File (Join-Path $ev '12-format-driver.txt') -Encoding utf8
"driver format exit code: $LASTEXITCODE" | Out-File (Join-Path $ev '12-format-driver.txt') -Append -Encoding utf8
dotnet format src/BoltSharp/BoltSharp.csproj --verify-no-changes --no-restore --include src/BoltSharp/Transport 2>&1 |
    Out-File (Join-Path $ev '13-format-src-transport.txt') -Encoding utf8
"src/Transport format exit code: $LASTEXITCODE" | Out-File (Join-Path $ev '13-format-src-transport.txt') -Append -Encoding utf8
dotnet format tests/BoltSharp.Tests/BoltSharp.Tests.csproj --verify-no-changes --no-restore --include tests/BoltSharp.Tests/Transport 2>&1 |
    Out-File (Join-Path $ev '14-format-tests-transport.txt') -Encoding utf8
"tests/Transport format exit code: $LASTEXITCODE" | Out-File (Join-Path $ev '14-format-tests-transport.txt') -Append -Encoding utf8
"Pre-existing observation (unchanged by F01-R1): the full tests/BoltSharp.Tests project format check reports WHITESPACE findings in tests/BoltSharp.Tests/Messages/BoltMessageFixtures.cs (B3-A-era file, recorded as a pre-existing observation in the B3-A owner acceptance 6.4 and not modified by this remediation; the same file produces no compile warnings)." |
    Out-File (Join-Path $ev '14-format-tests-transport.txt') -Append -Encoding utf8

# 6. Post-remediation reproduction probe (same source as the committed probe).
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) 'opencode\f01r1-probe-run'
if (Test-Path $scratch) { Remove-Item -Recurse -Force $scratch }
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Neo4j.Driver" Version="6.4.0" />
    <ProjectReference Include="$([System.IO.Path]::GetFullPath('src/BoltSharp/BoltSharp.csproj'))" />
  </ItemGroup>
</Project>
"@ | Out-File -FilePath (Join-Path $scratch 'probe.csproj') -Encoding utf8
Copy-Item (Join-Path $PSScriptRoot 'f01-repro-probe-source.cs') (Join-Path $scratch 'Program.cs')
dotnet build (Join-Path $scratch 'probe.csproj') -c Debug --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '15-probe-build.txt') -Encoding utf8
dotnet run --project $scratch -c Debug --no-build 2>&1 |
    Out-File (Join-Path $PSScriptRoot 'repro-post-remediation.txt') -Encoding utf8

# 7. Historical manifest verification, then the evidence manifest itself.
powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Verify-HistoricalManifests.ps1')
powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'New-EvidenceManifest.ps1')

Write-Host 'Suite summaries:'
Get-Content (Join-Path $ev '05-core-full-regression.txt') | Select-String -Pattern 'Passed!|Failed!'
Get-Content (Join-Path $ev '06-transport-suite.txt') | Select-String -Pattern 'Passed!|Failed!'
Get-Content (Join-Path $ev '07-b3c-backend.txt') | Select-String -Pattern 'Passed!|Failed!'
Get-Content (Join-Path $ev '08-b3b-sessions.txt') | Select-String -Pattern 'Passed!|Failed!'
Get-Content (Join-Path $ev '09-b3a-messages.txt') | Select-String -Pattern 'Passed!|Failed!'
Get-Content (Join-Path $ev '10-driver-suite.txt') | Select-String -Pattern 'Passed!|Failed!'
Get-Content (Join-Path $ev '11-driver-integration-b0b.txt') | Select-String -Pattern 'Passed!|Failed!'
