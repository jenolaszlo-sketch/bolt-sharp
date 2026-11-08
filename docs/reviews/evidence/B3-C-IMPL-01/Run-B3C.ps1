param(
    [string]$Repo = 'D:\projects\bolt-sharp',
    [Parameter(Mandatory = $true)][string]$Scratch,
    [string]$ExpectedHead = '37fe40633624a62bc4c6bd6162de2c884e1f0188'
)
# B3-C-IMPL-01 evidence capture. Builds and runs the full regression suite plus the new B3-C
# backend tests against the reviewed implementation revision, verifies historical evidence
# manifests and owner-decision byte identity, and records change isolation. Native exit codes are
# logged verbatim; all suites are expected to exit 0. No repository file is modified.
$ErrorActionPreference = 'Stop'
$evidence = Join-Path $Scratch 'evidence'
$artifacts = Join-Path $Scratch 'artifacts'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
Set-Location -LiteralPath $Repo

function Capture([string]$Name, [string]$Command, [string[]]$Arguments) {
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
    Get-Content -LiteralPath $log | Select-Object -Last 4
}

$head = git rev-parse HEAD
$tree = git rev-parse 'HEAD^{tree}'
if ($ExpectedHead -and $head -ne $ExpectedHead) {
    throw "Baseline moved: expected $ExpectedHead, found $head; check out the B3-C implementation revision"
}

[ordered]@{
    head = $head
    tree = $tree
    branch = (git branch --show-current)
    worktrees = @(git worktree list --porcelain)
    status = @(git status --porcelain=v1 --untracked-files=all)
    change_isolation_since_owner_acceptance = @(git diff --name-status 'aae3d0e986c3cef1dd4e0e49356cffafd06094d4' HEAD)
} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'baseline.json')
dotnet --info | Set-Content (Join-Path $evidence 'environment.txt')

# ---- Historical evidence integrity (in place; never rewritten) ----
$manifestPaths = @(
    'docs/reviews/evidence/B3-B-Q3/manifest.json',
    'docs/reviews/evidence/B3-B-R2-IMPL-01/manifest.json',
    'docs/reviews/evidence/B3-B-Q2/manifest.json',
    'docs/reviews/evidence/B3-B-Q1/manifest.json',
    'docs/reviews/evidence/B3-B-IMPL-01/manifest.json',
    'docs/reviews/evidence/B3-A-Q1/manifest.json',
    'docs/reviews/evidence/B3-A-R1-IMPL-01/manifest.json',
    'docs/reviews/evidence/B3-A-Q2/manifest.json',
    'docs/reviews/evidence/B3-A-IMPL-01/manifest.json',
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
$checks | Group-Object manifest | ForEach-Object {
    '{0}: {1}/{2} match' -f $_.Name, (@($_.Group | Where-Object { $_.matched }).Count), $_.Count
} | Set-Content (Join-Path $evidence 'historical-integrity-summary.txt')
Get-Content (Join-Path $evidence 'historical-integrity-summary.txt') | Write-Output

# ---- Owner decisions must remain byte-identical ----
git diff --quiet 'aae3d0e986c3cef1dd4e0e49356cffafd06094d4' HEAD -- `
    docs/decisions/B3-A-OWNER-01-owner-acceptance.md docs/decisions/B3-B-OWNER-01-owner-acceptance.md
"owner-decisions diff exit=$LASTEXITCODE (0 = byte-identical)" | Set-Content (Join-Path $evidence 'owner-decisions-diff.txt')

# ---- Reviewer inputs (informational hashes of the new/changed B3-C sources) ----
$reviewedInputs = @(
    'src/BoltSharp/Backend/ReferenceGraph.cs',
    'src/BoltSharp/Backend/ReferenceQueryEvaluator.cs',
    'src/BoltSharp/Backend/ReferenceOperationProvider.cs',
    'src/BoltSharp/Backend/ReferenceResultOperation.cs'
) + (Get-ChildItem -Path 'tests/BoltSharp.Tests/Backend' -Filter '*.cs' -File | ForEach-Object { "tests/BoltSharp.Tests/Backend/$($_.Name)" })
$reviewedInputs | Sort-Object | ForEach-Object {
    [pscustomobject]@{ path = $_; sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $Repo $_)).Hash.ToLowerInvariant() }
} | ConvertTo-Json | Set-Content (Join-Path $evidence 'reviewed-inputs.json')

# ---- Builds, formatting check and test suites ----
Capture 'core-build' 'dotnet' @('build', 'BoltSharp.slnx', '-c', 'Debug', '--no-incremental', '--artifacts-path', $artifacts)
Capture 'format-check' 'dotnet' @('format', 'BoltSharp.slnx', '--verify-no-changes', '--include', 'src/BoltSharp/Backend', 'tests/BoltSharp.Tests/Backend')
Capture 'core-tests' 'dotnet' @('test', 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--logger', 'trx;LogFileName=core.trx', '--results-directory', $evidence)
Capture 'backend-tests' 'dotnet' @('test', 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--filter', 'FullyQualifiedName~BoltSharp.Tests.Backend', '--logger', 'trx;LogFileName=b3c-backend.trx', '--results-directory', $evidence)
Capture 'session-tests' 'dotnet' @('test', 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--filter', 'FullyQualifiedName~BoltSharp.Tests.Sessions', '--logger', 'trx;LogFileName=session.trx', '--results-directory', $evidence)
Capture 'r1-remediation-tests' 'dotnet' @('test', 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--filter', 'FullyQualifiedName~CleanupAndTelemetryRemediationTests', '--logger', 'trx;LogFileName=r1-remediation.trx', '--results-directory', $evidence)
Capture 'r2-remediation-tests' 'dotnet' @('test', 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--filter', 'FullyQualifiedName~ResourceLifecycleRemediationTests', '--logger', 'trx;LogFileName=r2-remediation.trx', '--results-directory', $evidence)
Capture 'message-tests' 'dotnet' @('test', 'tests/BoltSharp.Tests/BoltSharp.Tests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--filter', 'FullyQualifiedName~BoltSharp.Tests.Messages', '--logger', 'trx;LogFileName=message.trx', '--results-directory', $evidence)
Capture 'integration-build' 'dotnet' @('build', 'tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj', '-c', 'Debug', '--no-incremental', '--artifacts-path', $artifacts)
Capture 'integration-tests' 'dotnet' @('test', 'tests/BoltSharp.IntegrationTests/BoltSharp.IntegrationTests.csproj', '-c', 'Debug', '--no-build', '--no-restore', '--artifacts-path', $artifacts, '--logger', 'trx;LogFileName=integration.trx', '--results-directory', $evidence)

Write-Output 'B3-C evidence capture complete.'
