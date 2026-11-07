param([string]$Repo = 'D:\projects\bolt-sharp', [Parameter(Mandatory=$true)][string]$Scratch)
$ErrorActionPreference = 'Stop'
$evidence = Join-Path $Scratch 'evidence'
$artifacts = Join-Path $Scratch 'artifacts'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
Set-Location -LiteralPath $Repo
function Capture([string]$Name, [string]$Command, [string[]]$Arguments) {
    $log = Join-Path $evidence ($Name + '.log')
    "COMMAND: $Command $($Arguments -join ' ')" | Set-Content -LiteralPath $log
    & $Command @Arguments >> $log 2>&1
    $code = $LASTEXITCODE
    "EXIT: $code" | Add-Content -LiteralPath $log
    Write-Output "$Name exit=$code"
    Get-Content -LiteralPath $log | Select-Object -Last 8
    return
}
$head = git rev-parse HEAD
$tree = git rev-parse 'HEAD^{tree}'
if ($head -ne '801badcea8523372d4ed83f8f2942d80bc0373e1' -or $tree -ne '243998eb21d01aa918f4b83143cb8a0c6bb27dd1') { throw 'Baseline moved' }
[ordered]@{ head=$head; tree=$tree; branch=(git branch --show-current); worktrees=@(git worktree list --porcelain); status=@(git status --porcelain=v1 --untracked-files=all); history=@(git log -10 --format=fuller); implementation_delta=@(git diff --stat '454ae00^' 454ae00); remote_refs=@(git for-each-ref refs/remotes --format='%(refname) %(objectname)') } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'baseline.json')
git diff '454ae00^' 454ae00 -- src tests | Set-Content (Join-Path $evidence 'implementation-delta.patch')
dotnet --info | Set-Content (Join-Path $evidence 'environment.txt')
$checks = foreach ($m in @('docs/reviews/evidence/B3-B-Q1/manifest.json','docs/reviews/evidence/B3-B-R1-IMPL-01/manifest.json')) {
    $manifest = Get-Content -Raw $m | ConvertFrom-Json
    foreach ($property in $manifest.PSObject.Properties) {
        foreach ($entry in @($property.Value)) {
            if ($entry.path -and $entry.sha256) {
                $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $entry.path).Hash.ToLowerInvariant()
                [pscustomobject]@{ manifest=$m; path=$entry.path; expected=$entry.sha256; actual=$actual; matched=($actual -eq $entry.sha256) }
            }
        }
    }
}
$checks | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'historical-integrity.json')
$historical = @(git ls-files docs/reviews/B3-B-Q1-independent-qualification.md docs/reviews/probes/B3-B-Q1 docs/reviews/evidence/B3-B-Q1)
$historical | ForEach-Object { [pscustomobject]@{path=$_; sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $_).Hash.ToLowerInvariant()} } | ConvertTo-Json | Set-Content (Join-Path $evidence 'q1-input-hashes.json')
Capture 'core-build' 'dotnet' @('build','BoltSharp.slnx','-c','Debug','--no-incremental','--artifacts-path',$artifacts)
Capture 'integration-build' 'dotnet' @('build','tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj','-c','Debug','--no-incremental','--artifacts-path',$artifacts)
Capture 'core-tests' 'dotnet' @('test','tests/BoltSharp.Tests/BoltSharp.Tests.csproj','-c','Debug','--no-build','--no-restore','--artifacts-path',$artifacts,'--logger','trx;LogFileName=core.trx','--results-directory',$evidence)
Capture 'session-tests' 'dotnet' @('test','tests/BoltSharp.Tests/BoltSharp.Tests.csproj','-c','Debug','--no-build','--no-restore','--artifacts-path',$artifacts,'--filter','FullyQualifiedName~BoltSharp.Tests.Sessions','--logger','trx;LogFileName=session.trx','--results-directory',$evidence)
Capture 'remediation-tests' 'dotnet' @('test','tests/BoltSharp.Tests/BoltSharp.Tests.csproj','-c','Debug','--no-build','--no-restore','--artifacts-path',$artifacts,'--filter','FullyQualifiedName~CleanupAndTelemetryRemediationTests','--logger','trx;LogFileName=remediation.trx','--results-directory',$evidence)
Capture 'message-tests' 'dotnet' @('test','tests/BoltSharp.Tests/BoltSharp.Tests.csproj','-c','Debug','--no-build','--no-restore','--artifacts-path',$artifacts,'--filter','FullyQualifiedName~BoltSharp.Tests.Messages','--logger','trx;LogFileName=message.trx','--results-directory',$evidence)
Capture 'integration-tests' 'dotnet' @('test','tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj','-c','Debug','--no-build','--no-restore','--artifacts-path',$artifacts,'--logger','trx;LogFileName=integration.trx','--results-directory',$evidence)
Capture 'q1-build' 'dotnet' @('build','docs/reviews/probes/B3-B-Q1/SessionProbe/SessionProbe.csproj','-c','Debug','--artifacts-path',$artifacts)
Capture 'q1-unchanged' 'dotnet' @((Join-Path $artifacts 'bin/SessionProbe/debug/B3BQ1.SessionProbe.dll'),(Join-Path $evidence 'q1-unchanged'))
