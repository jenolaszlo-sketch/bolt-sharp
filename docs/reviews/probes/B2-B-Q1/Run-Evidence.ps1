param([switch]$IncludeReviewer)
$ErrorActionPreference = 'Stop'
$reviewRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
Set-Location -LiteralPath $reviewRoot
$evidence = Join-Path $reviewRoot 'docs/reviews/evidence/B2-B-Q1'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$records = [System.Collections.Generic.List[object]]::new()
function Capture([string]$name, [string]$program, [string[]]$arguments) {
    $log = Join-Path $evidence ($name + '.txt')
    "Command: $program $($arguments -join ' ')" | Set-Content -LiteralPath $log
    & $program @arguments 2>&1 | Tee-Object -FilePath $log -Append
    $code = $LASTEXITCODE
    $records.Add([pscustomobject]@{name=$name; program=$program; arguments=$arguments; exitCode=$code})
}
Capture 'environment' 'dotnet' @('--info')
Capture 'provenance' 'git' @('log','-1','--format=fuller')
Capture 'identity' 'git' @('rev-parse','HEAD','HEAD^{tree}','HEAD:src','HEAD:tests')
Capture 'build' 'dotnet' @('build','BoltSharp.slnx','-c','Debug')
Capture 'solution' 'dotnet' @('test','BoltSharp.slnx','--no-build','-c','Debug','--logger',"trx;LogFileName=solution.trx",'--results-directory',$evidence)
Capture 'previous-reviewer' 'dotnet' @('test','docs/reviews/probes/B2-A-Q2/B2AQ2.Probes.csproj','-c','Debug','--logger',"trx;LogFileName=previous-reviewer.trx",'--results-directory',$evidence)
if ($IncludeReviewer) {
    Capture 'reviewer' 'dotnet' @('test','docs/reviews/probes/B2-B-Q1/B2BQ1.Probes.csproj','-c','Debug','--logger',"trx;LogFileName=reviewer.trx",'--results-directory',$evidence)
}
$records | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'commands.json')
