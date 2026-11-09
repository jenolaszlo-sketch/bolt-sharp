# B5-B-IMPL-01 reproduction driver.
# Regenerates the evidence: builds, every regression suite, the official-driver interoperability
# suite (with its per-scenario JSON evidence), historical manifest verification and the evidence
# manifest. Deterministic apart from ephemeral ports and in-memory certificate material.
$ErrorActionPreference = 'Continue'
# docs/reviews/evidence/B5-B-IMPL-01 -> four levels up to the repository root.
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
Set-Location $repoRoot

$ev = Join-Path $PSScriptRoot 'logs'
if (-not (Test-Path $ev)) { New-Item -ItemType Directory -Force -Path $ev | Out-Null }
$stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

"B5-B-IMPL-01 reproduction run @ $stamp" | Out-File -FilePath (Join-Path $ev '01-preflight.txt') -Encoding utf8
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

# 2. Official-driver interoperability suite (new B5-B), with JSON evidence generation.
$env:BOLTSHARP_B5B_EVIDENCE_DIR = $PSScriptRoot
dotnet test tests/BoltSharp.DriverInteropTests/BoltSharp.DriverInteropTests.csproj -c Debug `
    --logger 'trx;LogFileName=b5b-interop-suite.trx' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '04-b5b-interop-suite.txt') -Encoding utf8
Copy-Item (Join-Path $repoRoot 'tests/BoltSharp.DriverInteropTests/TestResults/b5b-interop-suite.trx') (Join-Path $PSScriptRoot 'b5b-interop-suite.trx') -Force
Remove-Item Env:\BOLTSHARP_B5B_EVIDENCE_DIR

# 2b. CI-equivalent scoped format check for the new test project.
dotnet format tests/BoltSharp.DriverInteropTests/BoltSharp.DriverInteropTests.csproj --verify-no-changes --no-restore 2>&1 |
    Out-File (Join-Path $ev '12-format-check.txt') -Encoding utf8
"format check exit code: $LASTEXITCODE" | Out-File (Join-Path $ev '12-format-check.txt') -Append -Encoding utf8

# 3. Existing suites (unchanged acceptance baselines).
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --filter 'FullyQualifiedName~BoltSharp.Tests.Transport' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '05-transport-suite.txt') -Encoding utf8
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --filter 'FullyQualifiedName~BoltSharp.Tests.Backend' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '06-b3c-backend.txt') -Encoding utf8
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --filter 'FullyQualifiedName~BoltSharp.Tests.Sessions' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '07-b3b-sessions.txt') -Encoding utf8
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --filter 'FullyQualifiedName~BoltSharp.Tests.Messages' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '08-b3a-messages.txt') -Encoding utf8
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --logger 'trx;LogFileName=full-regression.trx' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '09-full-regression.txt') -Encoding utf8
Copy-Item (Join-Path $repoRoot 'tests/BoltSharp.Tests/TestResults/full-regression.trx') (Join-Path $PSScriptRoot 'full-regression.trx') -Force
dotnet test tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj -c Debug --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '10-driver-integration-b0b.txt') -Encoding utf8

# 4. Historical manifest verification, then the evidence manifest itself.
powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Verify-HistoricalManifests.ps1')
powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'New-EvidenceManifest.ps1')

Write-Host 'Suite summaries:'
Get-Content (Join-Path $ev '04-b5b-interop-suite.txt') | Select-String -Pattern 'Passed!|Failed!'
Get-Content (Join-Path $ev '05-transport-suite.txt') | Select-String -Pattern 'Passed!|Failed!'
Get-Content (Join-Path $ev '06-b3c-backend.txt') | Select-String -Pattern 'Passed!|Failed!'
Get-Content (Join-Path $ev '07-b3b-sessions.txt') | Select-String -Pattern 'Passed!|Failed!'
Get-Content (Join-Path $ev '08-b3a-messages.txt') | Select-String -Pattern 'Passed!|Failed!'
Get-Content (Join-Path $ev '09-full-regression.txt') | Select-String -Pattern 'Passed!|Failed!'
Get-Content (Join-Path $ev '10-driver-integration-b0b.txt') | Select-String -Pattern 'Passed!|Failed!'
