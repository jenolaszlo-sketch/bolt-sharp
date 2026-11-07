param(
    [string]$Repo = 'D:\projects\bolt-sharp',
    [Parameter(Mandatory = $true)][string]$Scratch,
    [string]$ExpectedHead = '',
    [string]$Q2ProbeDir = '',
    [string]$Q1ProbeDir = ''
)
# B3-B-R2-IMPL-01 evidence capture. Builds, tests and the two unchanged historical reviewer
# probes run against the R2 implementation revision. All native exit codes are logged; the
# Q1/Q2 probe wrappers' PowerShell exit status is not the assertion result (Q1 expects exit 4
# with the stale X04 expectation; Q2 expects exit 4 with the superseded pre-R2 expectations
# analysed in the R2 report). Repository state is never modified.
$ErrorActionPreference = 'Stop'
$evidence = Join-Path $Scratch 'evidence'
$artifacts = Join-Path $Scratch 'artifacts'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
Set-Location -LiteralPath $Repo
if ([string]::IsNullOrWhiteSpace($Q2ProbeDir)) { $Q2ProbeDir = Join-Path $Repo 'docs/reviews/evidence/B3-B-Q2/probe' }
if ([string]::IsNullOrWhiteSpace($Q1ProbeDir)) { $Q1ProbeDir = Join-Path $Repo 'docs/reviews/probes/B3-B-Q1/SessionProbe' }

function Capture([string]$Name, [string]$Command, [string[]]$Arguments) {
    # Start-Process with file redirection preserves the native stdout/stderr text verbatim under
    # both Windows PowerShell 5.1 and PowerShell 7 (the Q1 probe writes its summary to stderr and
    # exits 4; that is expected evidence, not a terminating error).
    $log = Join-Path $evidence ($Name + '.log')
    $out = Join-Path $evidence ($Name + '.out.tmp')
    $err = Join-Path $evidence ($Name + '.err.tmp')
    Remove-Item -LiteralPath $out, $err -Force -ErrorAction SilentlyContinue
    "COMMAND: $Command $($Arguments -join ' ')" | Set-Content -LiteralPath $log
    $process = Start-Process -FilePath $Command -ArgumentList $Arguments -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $out -RedirectStandardError $err
    if (Test-Path -LiteralPath $out) {
        [System.IO.File]::AppendAllText($log, [System.IO.File]::ReadAllText($out))
        Remove-Item -LiteralPath $out -Force
    }
    if (Test-Path -LiteralPath $err) {
        [System.IO.File]::AppendAllText($log, [System.IO.File]::ReadAllText($err))
        Remove-Item -LiteralPath $err -Force
    }
    $code = $process.ExitCode
    "EXIT: $code" | Add-Content -LiteralPath $log
    Write-Output "$Name exit=$code"
    Get-Content -LiteralPath $log | Select-Object -Last 6
}

$head = git rev-parse HEAD
$tree = git rev-parse 'HEAD^{tree}'
if ($ExpectedHead -and $head -ne $ExpectedHead) {
    throw "Baseline moved: expected $ExpectedHead, found $head; check out the R2 implementation revision"
}

[ordered]@{
    head = $head
    tree = $tree
    branch = (git branch --show-current)
    worktrees = @(git worktree list --porcelain)
    status = @(git status --porcelain=v1 --untracked-files=all)
    r2_delta_stat = @(git diff --stat '4ae87b4f944a5c16cbd90a734b1ebfddc9fe0236' -- src tests)
    r2_delta_name_status = @(git diff --name-status '4ae87b4f944a5c16cbd90a734b1ebfddc9fe0236' -- src tests)
} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'baseline.json')
git diff '4ae87b4f944a5c16cbd90a734b1ebfddc9fe0236' -- src tests | Set-Content (Join-Path $evidence 'r2-implementation-delta.patch')
dotnet --info | Set-Content (Join-Path $evidence 'environment.txt')

# ---- Historical evidence integrity (all manifests verified in place; never rewritten) ----
# B3-A-IMPL-01 is included for the documented informational delta: its five non-matching entries
# are exactly the B3-A-R1-remediated message sources/tests, an explanation already recorded by the
# B3-A owner acceptance and the B3-B-Q1 report.
$manifestPaths = @(
    'docs/reviews/evidence/B3-B-IMPL-01/manifest.json',
    'docs/reviews/evidence/B3-B-Q1/manifest.json',
    'docs/reviews/evidence/B3-B-R1-IMPL-01/manifest.json',
    'docs/reviews/evidence/B3-B-Q2/manifest.json',
    'docs/reviews/evidence/B3-A-IMPL-01/manifest.json',
    'docs/reviews/evidence/B3-A-Q1/manifest.json',
    'docs/reviews/evidence/B3-A-R1-IMPL-01/manifest.json',
    'docs/reviews/evidence/B3-A-Q2/manifest.json',
    'docs/reviews/evidence/B0-B-DRV-INT-01/manifest.json',
    'docs/reviews/evidence/B0-B-Q1/manifest.json'
)
$checks = foreach ($m in $manifestPaths) {
    $manifest = Get-Content -Raw $m | ConvertFrom-Json
    foreach ($property in $manifest.PSObject.Properties) {
        foreach ($entry in @($property.Value)) {
            if ($entry.path -and $entry.sha256) {
                $full = Join-Path $Repo $entry.path
                $exists = Test-Path -LiteralPath $full
                $actual = if ($exists) { (Get-FileHash -Algorithm SHA256 -LiteralPath $full).Hash.ToLowerInvariant() } else { $null }
                [pscustomobject]@{
                    manifest = $m
                    path = $entry.path
                    expected = $entry.sha256
                    actual = $actual
                    existing = $exists
                    matched = ($actual -eq $entry.sha256)
                }
            }
        }
    }
}
$checks | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'historical-integrity.json')
$summary = $checks | Group-Object manifest | ForEach-Object {
    '{0}: {1}/{2} match' -f $_.Name, (@($_.Group | Where-Object { $_.matched }).Count), $_.Count
}
$summary | Set-Content (Join-Path $evidence 'historical-integrity-summary.txt')
$summary | Write-Output

# ---- Builds and tests ----
Capture 'core-build' 'dotnet' @('build', 'BoltSharp.slnx', '-c', 'Debug', '--no-incremental', '--artifacts-path', $artifacts)
Capture 'core-tests' 'dotnet' @('test', 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--logger', 'trx;LogFileName=core.trx', '--results-directory', $evidence)
Capture 'session-tests' 'dotnet' @('test', 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--filter', 'FullyQualifiedName~BoltSharp.Tests.Sessions', '--logger', 'trx;LogFileName=session.trx', '--results-directory', $evidence)
Capture 'remediation-tests' 'dotnet' @('test', 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--filter', 'FullyQualifiedName~CleanupAndTelemetryRemediationTests', '--logger', 'trx;LogFileName=remediation.trx', '--results-directory', $evidence)
Capture 'r2-tests' 'dotnet' @('test', 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--filter', 'FullyQualifiedName~ResourceLifecycleRemediationTests', '--logger', 'trx;LogFileName=r2-lifecycle.trx', '--results-directory', $evidence)
Capture 'message-tests' 'dotnet' @('test', 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--filter', 'FullyQualifiedName~BoltSharp.Tests.Messages', '--logger', 'trx;LogFileName=message.trx', '--results-directory', $evidence)
Capture 'integration-build' 'dotnet' @('build', 'tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj', '-c', 'Debug', '--no-incremental', '--artifacts-path', $artifacts)
Capture 'integration-tests' 'dotnet' @('test', 'tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--logger', 'trx;LogFileName=integration.trx', '--results-directory', $evidence)

# ---- Unchanged historical reviewer probes against R2 ----
$assembly = Join-Path $artifacts 'bin/BoltSharp/debug/BoltSharp.dll'
Capture 'q1-probe-build' 'dotnet' @('build', (Join-Path $Q1ProbeDir 'SessionProbe.csproj'), '-c', 'Debug', '--artifacts-path', $artifacts)
Capture 'q1-probe' 'dotnet' @((Join-Path $artifacts 'bin/SessionProbe/debug/B3BQ1.SessionProbe.dll'), (Join-Path $evidence 'q1-probe-after'))
Capture 'q2-probe-build' 'dotnet' @('build', (Join-Path $Q2ProbeDir 'Q2Probe.csproj'), '-c', 'Debug', "-p:BoltSharpAssembly=$assembly", '--artifacts-path', (Join-Path $Scratch 'q2-probe-artifacts'))
Capture 'q2-probe-after' 'dotnet' @((Join-Path $Scratch 'q2-probe-artifacts/bin/Q2Probe/debug/B3BQ2.Probe.dll'), (Join-Path $evidence 'q2-probe-after.json'))

# ---- Q2 probe before/after reconciliation (machine readable) ----
# "before" is the unmodified Q2 reviewer result retained in the Q2 evidence; "after" is the same
# unchanged probe executed against R2. The historical probe is never edited.
$before = Get-Content -Raw 'docs/reviews/evidence/B3-B-Q2/q2-probe-results.json' | ConvertFrom-Json
$after = Get-Content -Raw (Join-Path $evidence 'q2-probe-after.json') | ConvertFrom-Json
$afterMap = @{}
$after.results | ForEach-Object { $afterMap[$_.Id] = $_ }
$recon = foreach ($check in $before.results) {
    $afterCheck = $afterMap[$check.Id]
    [pscustomobject]@{
        id = $check.Id
        before_pass = $check.Pass
        after_pass = $afterCheck.Pass
        transition = if ($check.Pass -and $afterCheck.Pass) { 'pass-pass' }
                     elseif (-not $check.Pass -and -not $afterCheck.Pass) { 'fail-fail' }
                     elseif (-not $check.Pass) { 'resolved' }
                     else { 'superseded' }
        before_actual = $check.Actual
        after_actual = $afterCheck.Actual
    }
}
$recon | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $evidence 'q2-probe-before-after.json')

Write-Output 'R2 evidence capture complete.'
