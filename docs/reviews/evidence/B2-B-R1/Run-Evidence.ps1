# B2-B-R1 remediation evidence runner.
# Captures environment, remediation identity, build, the full solution suite, the new regression
# suite, the preserved B2-B-Q1 reviewer probes and the earlier B2-A-Q2 reviewer probes. Records
# nonzero subprocess exit codes explicitly rather than hiding them in an overall PowerShell status.
# All output is UTF-8 without a BOM so the captured bytes are stable and hashable.
$ErrorActionPreference = 'Stop'
$reviewRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
Set-Location -LiteralPath $reviewRoot
$evidence = Join-Path $reviewRoot 'docs/reviews/evidence/B2-B-R1'
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
Capture 'regression' 'dotnet' @('test','BoltSharp.slnx','--no-build','-c','Debug','--filter','FullyQualifiedName~ZeroVersionNegotiationTests','--logger',"trx;LogFileName=regression.trx",'--results-directory',$evidence)
Capture 'reviewer' 'dotnet' @('test','docs/reviews/probes/B2-B-Q1/B2BQ1.Probes.csproj','-c','Debug','--logger',"trx;LogFileName=reviewer.trx",'--results-directory',$evidence)
Capture 'reviewer-four' 'dotnet' @('test','docs/reviews/probes/B2-B-Q1/B2BQ1.Probes.csproj','-c','Debug','--filter','FullyQualifiedName~Zero_version_must_never|FullyQualifiedName~Range_containing_zero','--logger',"trx;LogFileName=reviewer-four.trx",'--results-directory',$evidence)
Capture 'previous-reviewer' 'dotnet' @('test','docs/reviews/probes/B2-A-Q2/B2AQ2.Probes.csproj','-c','Debug','--logger',"trx;LogFileName=previous-reviewer.trx",'--results-directory',$evidence)
$records | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'commands.json') -Encoding utf8

# Derive counts from individual TRX result entries rather than from console summaries.
$summary = [ordered]@{
    branch = (git branch --show-current)
    head = (git rev-parse HEAD)
    headTree = (git rev-parse 'HEAD^{tree}')
    records = @()
}
foreach ($name in @('solution','regression','reviewer','reviewer-four','previous-reviewer')) {
    [xml]$trx = Get-Content -LiteralPath (Join-Path $evidence ($name + '.trx'))
    $rows = @($trx.TestRun.Results.UnitTestResult)
    $summary.records += [ordered]@{
        run = $name
        assembly = @($trx.TestRun.TestDefinitions.UnitTest.storage | Sort-Object -Unique)
        passed = @($rows | Where-Object outcome -eq 'Passed').Count
        failed = @($rows | Where-Object outcome -eq 'Failed').Count
        skipped = @($rows | Where-Object outcome -eq 'NotExecuted').Count
        discovered = $rows.Count
        failedTests = @($rows | Where-Object outcome -ne 'Passed' | ForEach-Object { $_.testName })
    }
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'summary.json') -Encoding utf8
