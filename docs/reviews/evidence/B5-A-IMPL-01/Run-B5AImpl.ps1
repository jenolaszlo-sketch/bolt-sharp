# B5-A-IMPL-01 reproduction driver.
# Regenerates the evidence logs: builds, suite runs, network probe traces and historical
# manifest verification. Deterministic apart from ephemeral ports and in-memory certificates.
$ErrorActionPreference = 'Continue'
$repoRoot = Join-Path $PSScriptRoot '..' | Join-Path -ChildPath '..' | Join-Path -ChildPath '..'
$repoRoot = [System.IO.Path]::GetFullPath($repoRoot)
Set-Location $repoRoot

$ev = Join-Path $PSScriptRoot 'logs'
if (-not (Test-Path $ev)) { New-Item -ItemType Directory -Force -Path $ev | Out-Null }
$stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

"git state @ $stamp" | Out-File -FilePath (Join-Path $ev '01-preflight.txt') -Encoding utf8
"branch: $(git branch --show-current)" | Out-File (Join-Path $ev '01-preflight.txt') -Append -Encoding utf8
"HEAD: $(git rev-parse HEAD)" | Out-File (Join-Path $ev '01-preflight.txt') -Append -Encoding utf8
"tree:  $(git rev-parse 'HEAD^{tree}')" | Out-File (Join-Path $ev '01-preflight.txt') -Append -Encoding utf8
git worktree list | Out-File (Join-Path $ev '01-preflight.txt') -Append -Encoding utf8
git status -sb | Out-File (Join-Path $ev '01-preflight.txt') -Append -Encoding utf8

# 1. Builds.
dotnet build src/BoltSharp/BoltSharp.csproj -c Debug --no-incremental --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '02-build-production.txt') -Encoding utf8
dotnet build BoltSharp.slnx -c Debug --no-incremental --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '03-build-solution.txt') -Encoding utf8

# 2. Transport suite.
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --filter 'FullyQualifiedName~BoltSharp.Tests.Transport' --logger 'trx;LogFileName=transport-tests.trx' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '04-transport-suite.txt') -Encoding utf8
Copy-Item (Join-Path $repoRoot 'tests/BoltSharp.Tests/TestResults/transport-tests.trx') (Join-Path $PSScriptRoot 'transport-tests.trx') -Force

# 3. Full regression.
dotnet test tests/BoltSharp.Tests/BoltSharp.Tests.csproj -c Debug --logger 'trx;LogFileName=full-regression.trx' --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '05-full-regression.txt') -Encoding utf8
Copy-Item (Join-Path $repoRoot 'tests/BoltSharp.Tests/TestResults/full-regression.trx') (Join-Path $PSScriptRoot 'full-regression.trx') -Force

# 4. Official-driver integration tests (B0-B, unchanged).
dotnet test tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj -c Debug --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '06-driver-integration.txt') -Encoding utf8

# 5. Historical manifest verification, then the evidence manifest itself.
powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Verify-HistoricalManifests.ps1')
powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'New-EvidenceManifest.ps1')

# 6. Network probe: scratch console project outside the repository; prints byte traces.
#    The probe source is retained at network-probe-source.cs in this evidence directory.
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) 'opencode\b5a-probe-run'
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
    <ProjectReference Include="$([System.IO.Path]::GetFullPath('src/BoltSharp/BoltSharp.csproj'))" />
  </ItemGroup>
</Project>
"@ | Out-File -FilePath (Join-Path $scratch 'probe.csproj') -Encoding utf8
Copy-Item (Join-Path $PSScriptRoot 'network-probe-source.cs') (Join-Path $scratch 'Program.cs')
dotnet build (Join-Path $scratch 'probe.csproj') -c Debug --nodeReuse:false 2>&1 |
    Out-File (Join-Path $ev '10-probe-build.txt') -Encoding utf8
dotnet run --project $scratch -c Debug --no-build 2>&1 |
    Out-File (Join-Path $ev 'network-traces.txt') -Encoding utf8

Write-Host 'Suite summaries:'
Get-Content (Join-Path $ev '04-transport-suite.txt') | Select-String -Pattern 'Passed!|Failed!'
Get-Content (Join-Path $ev '05-full-regression.txt') | Select-String -Pattern 'Passed!|Failed!'
Get-Content (Join-Path $ev '06-driver-integration.txt') | Select-String -Pattern 'Passed!|Failed!'
