param(
    [string]$Repo = 'D:\projects\bolt-sharp',
    [Parameter(Mandatory = $true)][string]$Scratch,
    [string]$ExpectedHead = 'b9356c3fadb484b9b3a39d807205283155886184',
    [string]$Q2ProbeDir = '',
    [string]$Q1ProbeDir = '',
    [string]$Q3ProbeDir = ''
)
# B3-B-Q3 independent requalification evidence capture (reviewer-owned). Runs the full regression
# suite, the unchanged Q1/Q2 historical probes and the independent Q3 probe against the reviewed
# revision. Native exit codes are logged verbatim; expected non-zero probe exits (Q1/Q2 exit 4)
# are evidence, not failures. The Q3 probe must exit 0.
$ErrorActionPreference = 'Stop'
$evidence = Join-Path $Scratch 'evidence'
$artifacts = Join-Path $Scratch 'artifacts'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
Set-Location -LiteralPath $Repo
if ([string]::IsNullOrWhiteSpace($Q2ProbeDir)) { $Q2ProbeDir = Join-Path $Repo 'docs/reviews/evidence/B3-B-Q2/probe' }
if ([string]::IsNullOrWhiteSpace($Q1ProbeDir)) { $Q1ProbeDir = Join-Path $Repo 'docs/reviews/probes/B3-B-Q1/SessionProbe' }
if ([string]::IsNullOrWhiteSpace($Q3ProbeDir)) { $Q3ProbeDir = Join-Path $Repo 'docs/reviews/probes/B3-B-Q3/IndependentProbe' }

function Capture([string]$Name, [string]$Command, [string[]]$Arguments) {
    # Start-Process redirection preserves native stdout/stderr text under PowerShell 5.1 and 7.
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
    throw "Baseline moved: expected $ExpectedHead, found $head; check out the reviewed revision"
}

[ordered]@{
    head = $head
    tree = $tree
    branch = (git branch --show-current)
    worktrees = @(git worktree list --porcelain)
    status = @(git status --porcelain=v1 --untracked-files=all)
    reviewer_untracked_note = 'untracked entries are the additive reviewer artifacts (Q3 probe and evidence) created by this requalification; production and historical paths are unchanged'
    change_isolation_q2_to_head = @(git diff --name-status '4ae87b4f944a5c16cbd90a734b1ebfddc9fe0236' HEAD)
    guarded_paths_diff_exit = $null
} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'baseline.json')
git diff '4ae87b4f944a5c16cbd90a734b1ebfddc9fe0236' HEAD -- src tests | Set-Content (Join-Path $evidence 'r2-source-delta.patch')
dotnet --info | Set-Content (Join-Path $evidence 'environment.txt')

# ---- Historical evidence integrity (in place; never rewritten) ----
$manifestPaths = @(
    'docs/reviews/evidence/B3-B-IMPL-01/manifest.json',
    'docs/reviews/evidence/B3-B-Q1/manifest.json',
    'docs/reviews/evidence/B3-B-R1-IMPL-01/manifest.json',
    'docs/reviews/evidence/B3-B-Q2/manifest.json',
    'docs/reviews/evidence/B3-B-R2-IMPL-01/manifest.json',
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

# ---- Reviewer input snapshot (current reviewed sources; informational hashes) ----
$reviewedInputs = @(
    'src/BoltSharp/Sessions/BoltConnectionSession.cs',
    'src/BoltSharp/Sessions/BoltSessionOptions.cs',
    'src/BoltSharp/Sessions/BoltSessionResult.cs',
    'src/BoltSharp/Sessions/BoltConnectionState.cs',
    'tests/BoltSharp.Tests/Sessions/ResourceLifecycleRemediationTests.cs',
    'tests/BoltSharp.Tests/Sessions/CleanupAndTelemetryRemediationTests.cs',
    'tests/BoltSharp.Tests/Sessions/SessionTestHarness.cs'
)
$reviewedInputs | ForEach-Object {
    [pscustomobject]@{ path = $_; sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $Repo $_)).Hash.ToLowerInvariant() }
} | ConvertTo-Json | Set-Content (Join-Path $evidence 'reviewed-inputs.json')
(Get-Content -LiteralPath (Join-Path $Repo 'src/BoltSharp/Sessions/BoltConnectionSession.cs') |
    ForEach-Object -Begin { $n = 0 } -Process { $n++; "{0}: {1}" -f $n, $_ }) |
    Set-Content (Join-Path $evidence 'reviewed-session-source.txt')

# ---- Regression suites ----
Capture 'core-build' 'dotnet' @('build', 'BoltSharp.slnx', '-c', 'Debug', '--no-incremental', '--artifacts-path', $artifacts)
Capture 'core-tests' 'dotnet' @('test', 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--logger', 'trx;LogFileName=core.trx', '--results-directory', $evidence)
Capture 'session-tests' 'dotnet' @('test', 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--filter', 'FullyQualifiedName~BoltSharp.Tests.Sessions', '--logger', 'trx;LogFileName=session.trx', '--results-directory', $evidence)
Capture 'remediation-tests' 'dotnet' @('test', 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--filter', 'FullyQualifiedName~CleanupAndTelemetryRemediationTests', '--logger', 'trx;LogFileName=remediation.trx', '--results-directory', $evidence)
Capture 'r2-tests' 'dotnet' @('test', 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--filter', 'FullyQualifiedName~ResourceLifecycleRemediationTests', '--logger', 'trx;LogFileName=r2-lifecycle.trx', '--results-directory', $evidence)
Capture 'message-tests' 'dotnet' @('test', 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--filter', 'FullyQualifiedName~BoltSharp.Tests.Messages', '--logger', 'trx;LogFileName=message.trx', '--results-directory', $evidence)
Capture 'integration-build' 'dotnet' @('build', 'tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj', '-c', 'Debug', '--no-incremental', '--artifacts-path', $artifacts)
Capture 'integration-tests' 'dotnet' @('test', 'tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--logger', 'trx;LogFileName=integration.trx', '--results-directory', $evidence)

# ---- Historical probes (unchanged) and the independent Q3 probe ----
$assembly = Join-Path $artifacts 'bin/BoltSharp/debug/BoltSharp.dll'
Capture 'q1-probe-build' 'dotnet' @('build', (Join-Path $Q1ProbeDir 'SessionProbe.csproj'), '-c', 'Debug', '--artifacts-path', $artifacts)
Capture 'q1-probe' 'dotnet' @((Join-Path $artifacts 'bin/SessionProbe/debug/B3BQ1.SessionProbe.dll'), (Join-Path $evidence 'q1-probe-after'))
Capture 'q2-probe-build' 'dotnet' @('build', (Join-Path $Q2ProbeDir 'Q2Probe.csproj'), '-c', 'Debug', "-p:BoltSharpAssembly=$assembly", '--artifacts-path', (Join-Path $Scratch 'q2-probe-artifacts'))
Capture 'q2-probe' 'dotnet' @((Join-Path $Scratch 'q2-probe-artifacts/bin/Q2Probe/debug/B3BQ2.Probe.dll'), (Join-Path $evidence 'q2-probe-after.json'))
Capture 'q3-probe-build' 'dotnet' @('build', (Join-Path $Q3ProbeDir 'IndependentProbe.csproj'), '-c', 'Debug', '--artifacts-path', $artifacts)
$q3Dll = Join-Path $artifacts 'bin/IndependentProbe/debug/B3BQ3.IndependentProbe.dll'
Capture 'q3-probe' 'dotnet' @($q3Dll,
    (Join-Path $evidence 'q3-probe-results.json'),
    (Join-Path $Repo 'docs/reviews/evidence/B3-B-Q2/q2-probe-results.json'),
    (Join-Path $evidence 'q2-probe-after.json'))

Write-Output 'Q3 evidence capture complete.'
