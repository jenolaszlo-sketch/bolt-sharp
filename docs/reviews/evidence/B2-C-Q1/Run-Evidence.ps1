# B2-C-Q1 independent qualification evidence runner.
# Captures environment/identity, core solution build+tests, the producer harness and baseline
# subsets, the opt-in driver scaffold, the preserved B2-B-Q1/B2-B-Q2/B2-A-Q2 reviewer probes and
# the new B2-C-Q1 reviewer probes. Records nonzero exit codes. UTF-8, no BOM.
$ErrorActionPreference = 'Stop'
$reviewRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
Set-Location -LiteralPath $reviewRoot
$evidence = Join-Path $reviewRoot 'docs/reviews/evidence/B2-C-Q1'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
function Write-Log([string]$Path, [string]$Text) { [System.IO.File]::WriteAllText($Path, $Text, $utf8) }
function Append-Log([string]$Path, [string]$Text) { [System.IO.File]::AppendAllText($Path, $Text, $utf8) }
$records = [System.Collections.Generic.List[object]]::new()
function Capture([string]$name, [string]$program, [string[]]$arguments) {
    $log = Join-Path $evidence ($name + '.txt')
    Write-Log $log ("Command: $program $($arguments -join ' ')`r`n")
    $output = & $program @arguments 2>&1 | Out-String
    $code = $LASTEXITCODE
    Append-Log $log $output
    $records.Add([pscustomobject]@{name=$name; program=$program; arguments=$arguments; exitCode=$code})
}
Capture 'environment' 'dotnet' @('--info')
Capture 'identity' 'git' @('rev-parse','HEAD','HEAD^{tree}','HEAD:src','HEAD:tests')
Capture 'provenance' 'git' @('log','-1','--format=fuller')
Capture 'build' 'dotnet' @('build','BoltSharp.slnx','-c','Debug')
Capture 'solution' 'dotnet' @('test','BoltSharp.slnx','--no-build','-c','Debug','--logger',"trx;LogFileName=solution.trx",'--results-directory',$evidence)
Capture 'harness' 'dotnet' @('test','BoltSharp.slnx','--no-build','-c','Debug','--filter','FullyQualifiedName~HarnessProbes','--logger',"trx;LogFileName=harness.trx",'--results-directory',$evidence)
Capture 'baseline-excluding-harness' 'dotnet' @('test','BoltSharp.slnx','--no-build','-c','Debug','--filter','FullyQualifiedName!~HarnessProbes','--logger',"trx;LogFileName=baseline-excluding-harness.trx",'--results-directory',$evidence)
Capture 'driver-build' 'dotnet' @('build','tests/BoltSharp.DriverIntegrationTests/BoltSharp.DriverIntegrationTests.csproj','-c','Debug')
Capture 'driver-test' 'dotnet' @('test','tests/BoltSharp.DriverIntegrationTests/BoltSharp.DriverIntegrationTests.csproj','-c','Debug','--logger',"trx;LogFileName=driver.trx",'--results-directory',$evidence)
Capture 'reviewer' 'dotnet' @('test','docs/reviews/probes/B2-B-Q1/B2BQ1.Probes.csproj','-c','Debug','--logger',"trx;LogFileName=reviewer.trx",'--results-directory',$evidence)
Capture 'q2' 'dotnet' @('test','docs/reviews/probes/B2-B-Q2/B2BQ2.Probes.csproj','-c','Debug','--logger',"trx;LogFileName=q2.trx",'--results-directory',$evidence)
Capture 'aq2' 'dotnet' @('test','docs/reviews/probes/B2-A-Q2/B2AQ2.Probes.csproj','-c','Debug','--logger',"trx;LogFileName=aq2.trx",'--results-directory',$evidence)
Capture 'b2cq1-probes' 'dotnet' @('test','docs/reviews/probes/B2-C-Q1/B2CQ1.Probes.csproj','-c','Debug','--logger',"trx;LogFileName=b2cq1.trx",'--results-directory',$evidence)
$records | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'commands.json') -Encoding utf8

$summary = [ordered]@{
    branch = (git branch --show-current)
    head = (git rev-parse HEAD)
    headTree = (git rev-parse 'HEAD^{tree}')
    reviewedImplementation = '5295137bd688d38a2c1e3c67e6cc2daa51c050fb'
    reviewedImplementationTree = '5351dacd16475c75a2b44054a033ba34bd610e51'
    reviewedBaseline = '8f8b7e43b7d7fb3a9576db6e9465c13b74264d87'
    reviewedBaselineTree = 'f7b09159be81d6855d0c7e4a4e632c242cd63187'
    records = @()
}
foreach ($name in @('solution','harness','baseline-excluding-harness','driver','reviewer','q2','aq2','b2cq1')) {
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
