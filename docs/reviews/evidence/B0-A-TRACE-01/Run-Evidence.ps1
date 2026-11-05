# B0-A-TRACE-01 evidence runner (consolidated).
#
# Origin: written by parallel implementation B0-A-TRACE-01-B during the concurrent-authoring
# incident of 2026-11-05; adapted by B0-A-TRACE-01-RECOVERY for the canonical implementation
# (loopback listener + HandshakeTraceProbeTests). Captures environment/identity, the core solution
# build+tests, the preserved reviewer probes, the driver integration project (scaffold + gated
# D01-D10 + authentic handshake trace tests), and the sanitized handshake trace JSON. Records
# nonzero exit codes without treating native stderr as terminating. UTF-8, no BOM.
$ErrorActionPreference = 'Stop'
$reviewRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
Set-Location -LiteralPath $reviewRoot
$evidence = Join-Path $reviewRoot 'docs/reviews/evidence/B0-A-TRACE-01'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
function Write-Log([string]$Path, [string]$Text) { [System.IO.File]::WriteAllText($Path, $Text, $utf8) }
function Append-Log([string]$Path, [string]$Text) { [System.IO.File]::AppendAllText($Path, $Text, $utf8) }
$records = [System.Collections.Generic.List[object]]::new()
function Capture([string]$name, [string]$program, [string[]]$arguments) {
    $log = Join-Path $evidence ($name + '.txt')
    Write-Log $log ("Command: $program $($arguments -join ' ')`r`n")
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & $program @arguments 2>&1 | Out-String }
    finally { $ErrorActionPreference = $previous }
    $code = $LASTEXITCODE
    Append-Log $log $output
    $records.Add([pscustomobject]@{name=$name; program=$program; arguments=$arguments; exitCode=$code})
}
Capture 'environment' 'dotnet' @('--info')
Capture 'identity' 'git' @('rev-parse','HEAD','HEAD^{tree}','HEAD:src','HEAD:tests')
Capture 'provenance' 'git' @('log','-1','--format=fuller')
Capture 'build' 'dotnet' @('build','BoltSharp.slnx','-c','Debug')
Capture 'solution' 'dotnet' @('test','BoltSharp.slnx','--no-build','-c','Debug','--logger',"trx;LogFileName=solution.trx",'--results-directory',$evidence)
Capture 'b2a-q2' 'dotnet' @('test','docs/reviews/probes/B2-A-Q2/B2AQ2.Probes.csproj','-c','Debug','--logger',"trx;LogFileName=b2a-q2.trx",'--results-directory',$evidence)
Capture 'b2b-q1' 'dotnet' @('test','docs/reviews/probes/B2-B-Q1/B2BQ1.Probes.csproj','-c','Debug','--logger',"trx;LogFileName=b2b-q1.trx",'--results-directory',$evidence)
Capture 'b2b-q2' 'dotnet' @('test','docs/reviews/probes/B2-B-Q2/B2BQ2.Probes.csproj','-c','Debug','--logger',"trx;LogFileName=b2b-q2.trx",'--results-directory',$evidence)
Capture 'b2c-q2' 'dotnet' @('test','docs/reviews/probes/B2-C-Q2/B2CQ2.Probes.csproj','-c','Debug','--logger',"trx;LogFileName=b2c-q2.trx",'--results-directory',$evidence)
Capture 'driver-build' 'dotnet' @('build','tests/BoltSharp.DriverIntegrationTests/BoltSharp.DriverIntegrationTests.csproj','-c','Debug')
Capture 'driver' 'dotnet' @('test','tests/BoltSharp.DriverIntegrationTests/BoltSharp.DriverIntegrationTests.csproj','--no-build','-c','Debug','--logger',"trx;LogFileName=driver.trx",'--results-directory',$evidence)
# Authentic Neo4j.Driver 6.4.0 handshake experiment: writes the sanitized trace JSON and hex files
# (client handshake + server response only; no post-handshake bodies) into this directory.
$env:BOLTSHARP_TRACE01_EVIDENCE_DIR = $evidence
Capture 'trace' 'dotnet' @('test','tests/BoltSharp.DriverIntegrationTests/BoltSharp.DriverIntegrationTests.csproj','--no-build','-c','Debug','--filter','FullyQualifiedName~HandshakeTraceProbeTests','--logger',"trx;LogFileName=trace.trx",'--results-directory',$evidence)
Remove-Item Env:BOLTSHARP_TRACE01_EVIDENCE_DIR
$records | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'commands.json') -Encoding utf8

$summary = [ordered]@{
    branch = (git branch --show-current)
    head = (git rev-parse HEAD)
    headTree = (git rev-parse 'HEAD^{tree}')
    driverPackage = 'Neo4j.Driver 6.4.0'
    records = @()
}
foreach ($name in @('solution','b2a-q2','b2b-q1','b2b-q2','b2c-q2','driver','trace')) {
    [xml]$trx = Get-Content -LiteralPath (Join-Path $evidence ($name + '.trx'))
    $rows = @($trx.TestRun.Results.UnitTestResult)
    $summary.records += [ordered]@{
        run = $name
        assembly = @($trx.TestRun.TestDefinitions.UnitTest.storage | Sort-Object -Unique)
        passed = @($rows | Where-Object outcome -eq 'Passed').Count
        failed = @($rows | Where-Object outcome -eq 'Failed').Count
        skipped = @($rows | Where-Object { $_.outcome -ne 'Passed' -and $_.outcome -ne 'Failed' }).Count
        discovered = $rows.Count
        failedTests = @($rows | Where-Object outcome -eq 'Failed' | ForEach-Object { $_.testName })
    }
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'summary.json') -Encoding utf8
Write-Host "Evidence captured under $evidence"
Get-Content -LiteralPath (Join-Path $evidence 'summary.json')
