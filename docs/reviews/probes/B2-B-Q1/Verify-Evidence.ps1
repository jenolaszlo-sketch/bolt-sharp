$ErrorActionPreference = 'Stop'
$reviewRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
Set-Location -LiteralPath $reviewRoot
$evidence = Join-Path $reviewRoot 'docs/reviews/evidence/B2-B-Q1'
$implementation = 'ca36d36ee1b840b444180432e355832d56f5384f'
$baseline = 'a46c916e7f6e72e315504e2d284921d406ee6c74'
git merge-base --is-ancestor $baseline $implementation
if ($LASTEXITCODE -ne 0) { throw 'Baseline ancestry failed' }
$parent = git rev-parse "$implementation^"
if ($parent -ne $baseline) { throw 'Unexpected implementation parent' }
$diff = @(git diff --name-status $baseline $implementation)
if ($diff.Count -ne 23 -or @($diff | Where-Object { $_ -notmatch '^A\s' }).Count) { throw 'Additive change-set check failed' }
$diff | Set-Content -LiteralPath (Join-Path $evidence 'implementation-diff.txt')
$excludedDiff = @(git diff --name-only $baseline $implementation -- . ':!src/BoltSharp/Handshake' ':!tests/BoltSharp.Tests/Handshake' ':!docs/reviews/B2-B-bolt-handshake.md' ':!docs/reviews/B2-B-Q1-qualification-handoff.md')
if ($excludedDiff.Count) { throw 'Historical tree changed at implementation' }
git diff --exit-code $implementation -- src tests Directory.Build.props BoltSharp.slnx
if ($LASTEXITCODE -ne 0) { throw 'Production, tests, or project modified during review' }
$historicalDiff = @(git diff --name-only $implementation -- docs ':!docs/reviews/probes/B2-B-Q1' ':!docs/reviews/evidence/B2-B-Q1' ':!docs/reviews/B2-B-Q1-independent-qualification.md')
if ($historicalDiff.Count) { throw 'Historical documentation changed' }
$tracked = @(git ls-tree -r $implementation)
$tracked | Set-Content -LiteralPath (Join-Path $evidence 'reviewed-tree.txt')
$summary = [ordered]@{
    implementation=$implementation; tree=(git rev-parse "$implementation^{tree}")
    baseline=$baseline; baselineTree=(git rev-parse "$baseline^{tree}"); directParent=$parent
    sourceTree=(git rev-parse "${implementation}:src"); testsTree=(git rev-parse "${implementation}:tests")
    branch=(git branch --show-current); localRemoteState=(git status --short --branch | Select-Object -First 1)
    remoteTrackingCommit=(git rev-parse refs/remotes/origin/main)
    cleanAtEntry=$true; cleanAtEntryEvidence='Initial elevated git status --porcelain=v1 returned no output before reviewer changes.'
    implementationAddedPaths=$diff.Count; historicalImplementationDiffEmpty=$true
    reviewedSourceTestsProjectsAndHistoricalDocsUnchanged=$true
    records=@()
}
foreach ($name in @('solution','previous-reviewer','reviewer')) {
    [xml]$trx = Get-Content -LiteralPath (Join-Path $evidence ($name+'.trx'))
    $rows = @($trx.TestRun.Results.UnitTestResult)
    $groups = if ($name -eq 'solution') { @($rows | Group-Object {
        if ($_.testName -like '*Tests.Handshake*') { 'B2-B' }
        elseif ($_.testName -like '*B2Q1FramingProbes*') { 'B2-A-Q1' }
        elseif ($_.testName -like '*Tests.Framing*') { 'B2-A' }
        else { 'B1' }
    } | ForEach-Object { @{group=$_.Name; count=$_.Count} }) } else { @() }
    $summary.records += [ordered]@{
        run=$name; assembly=@($trx.TestRun.TestDefinitions.UnitTest.storage | Sort-Object -Unique)
        counters=@{passed=@($rows | Where-Object outcome -eq 'Passed').Count; failed=@($rows | Where-Object outcome -eq 'Failed').Count; skipped=@($rows | Where-Object outcome -eq 'NotExecuted').Count}
        discovered=$rows.Count; groups=$groups
        failed=@($rows | Where-Object outcome -ne 'Passed' | ForEach-Object { $_.testName })
    }
}
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $evidence 'summary.json')
$hashes = Get-ChildItem -File -LiteralPath $evidence | Where-Object Name -ne 'sha256.json' | ForEach-Object {
    @{file=$_.Name; bytes=$_.Length; sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash}
}
$hashes | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'sha256.json')
$summary | ConvertTo-Json -Depth 10
