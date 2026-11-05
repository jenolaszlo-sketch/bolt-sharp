# B2-C-Q2 independent requalification evidence runner.
# Captures environment/identity, core build+tests, harness+disposal, driver+URI subsets, the
# preserved B2-B-Q1/B2-B-Q2/B2-A-Q2/B2-C-Q1 reviewer probes, and the new B2-C-Q2 probes.
# Records nonzero exit codes without treating native stderr as a terminating error. UTF-8, no BOM.
$ErrorActionPreference = 'Stop'
$reviewRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
Set-Location -LiteralPath $reviewRoot
$evidence = Join-Path $reviewRoot 'docs/reviews/evidence/B2-C-Q2'
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
Capture 'harness' 'dotnet' @('test','BoltSharp.slnx','--no-build','-c','Debug','--filter','FullyQualifiedName~Harness','--logger',"trx;LogFileName=harness.trx",'--results-directory',$evidence)
Capture 'f02-disposal' 'dotnet' @('test','BoltSharp.slnx','--no-build','-c','Debug','--filter','FullyQualifiedName~HarnessDisposalTests','--logger',"trx;LogFileName=f02.trx",'--results-directory',$evidence)
Capture 'driver-build' 'dotnet' @('build','tests/BoltSharp.DriverIntegrationTests/BoltSharp.DriverIntegrationTests.csproj','-c','Debug')
Capture 'driver-test' 'dotnet' @('test','tests/BoltSharp.DriverIntegrationTests/BoltSharp.DriverIntegrationTests.csproj','-c','Debug','--logger',"trx;LogFileName=driver.trx",'--results-directory',$evidence)
Capture 'f01-uri' 'dotnet' @('test','tests/BoltSharp.DriverIntegrationTests/BoltSharp.DriverIntegrationTests.csproj','--no-build','-c','Debug','--filter','FullyQualifiedName~DriverUriValidationTests','--logger',"trx;LogFileName=f01.trx",'--results-directory',$evidence)
Capture 'reviewer' 'dotnet' @('test','docs/reviews/probes/B2-B-Q1/B2BQ1.Probes.csproj','-c','Debug','--logger',"trx;LogFileName=reviewer.trx",'--results-directory',$evidence)
Capture 'q2' 'dotnet' @('test','docs/reviews/probes/B2-B-Q2/B2BQ2.Probes.csproj','-c','Debug','--logger',"trx;LogFileName=q2.trx",'--results-directory',$evidence)
Capture 'aq2' 'dotnet' @('test','docs/reviews/probes/B2-A-Q2/B2AQ2.Probes.csproj','-c','Debug','--logger',"trx;LogFileName=aq2.trx",'--results-directory',$evidence)
Capture 'b2cq1' 'dotnet' @('test','docs/reviews/probes/B2-C-Q1/B2CQ1.Probes.csproj','-c','Debug','--logger',"trx;LogFileName=b2cq1.trx",'--results-directory',$evidence)
Capture 'b2cq2' 'dotnet' @('test','docs/reviews/probes/B2-C-Q2/B2CQ2.Probes.csproj','-c','Debug','--logger',"trx;LogFileName=b2cq2.trx",'--results-directory',$evidence)
$records | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'commands.json') -Encoding utf8

$summary = [ordered]@{
    branch = (git branch --show-current)
    head = (git rev-parse HEAD)
    headTree = (git rev-parse 'HEAD^{tree}')
    reviewedRemediation = '87e029f490fc1cd965be2ce656c4e22ccfa26cf5'
    reviewedRemediationTree = '7983a0643f9f97031ecb03561e0cc2d429fe7272'
    reviewedQualification = '2710bc73741ecd1f144cf4d60d7e2387d8f125f9'
    records = @()
}
foreach ($name in @('solution','harness','f02','driver','f01','reviewer','q2','aq2','b2cq1','b2cq2')) {
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
