# B5-A-IMPL-01 evidence verification: historical manifests + B3 owner decisions.
# Raw-byte SHA-256 verification of every entry in each historical evidence manifest,
# plus byte-identity checks of the three B3 owner decision documents.
$ErrorActionPreference = 'Continue'
Set-Location $PSScriptRoot\..\..\..\..

$sha = [System.Security.Cryptography.SHA256]::Create()
$reports = New-Object System.Collections.Generic.List[string]
$results = New-Object System.Collections.Generic.List[object]

function Test-Manifest {
    param([string]$ManifestPath, [int]$ExpectedCount)

    $reports.Add("=== $ManifestPath ===")
    if (-not (Test-Path $ManifestPath)) {
        $reports.Add("MISSING manifest")
        return
    }

    $json = Get-Content $ManifestPath -Raw | ConvertFrom-Json
    # The commission's per-manifest counts span ALL hashed sections. Known section names:
    # evidence_files, probe_source_files, report_files, changed_source_and_report_files,
    # files/reports (B0-B-DRV-INT-01 variant), plus manifest.json itself in the commission count.
    $sections = @()
    foreach ($name in @('evidence_files', 'probe_source_files', 'report_files', 'changed_source_and_report_files', 'files', 'reports')) {
        $section = $json.PSObject.Properties[$name]
        if ($section -and $null -ne $section.Value) { $sections += ,@($name, @($section.Value)) }
    }
    $declared = ($sections | ForEach-Object { $_[1].Count } | Measure-Object -Sum).Sum
    $reports.Add(("declared hashed entries: {0} (commission expectation: {1})" -f $declared, $ExpectedCount))

    $matched = 0
    $mismatched = 0
    foreach ($sectionPair in $sections) {
        $sectionName = $sectionPair[0]
        foreach ($entry in $sectionPair[1]) {
            $path = $entry.path
            $expected = $entry.sha256
            if ($null -eq $path -or $null -eq $expected) {
                $reports.Add("MALFORMED entry in $sectionName (missing path or sha256)")
                $mismatched++
                continue
            }
            if (-not (Test-Path $path)) {
                $reports.Add("MISSING FILE: $path (expected $expected)")
                $mismatched++
                $results.Add([pscustomobject]@{ manifest = $ManifestPath; section = $sectionName; path = $path; expected = $expected; actual = ''; existing = $false; matched = $false })
                continue
            }
            $bytes = [System.IO.File]::ReadAllBytes($path)
            $actual = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLower()
            $ok = $actual -eq ($expected.ToLower())
            if ($ok) { $matched++ } else { $mismatched++ }
            $results.Add([pscustomobject]@{ manifest = $ManifestPath; section = $sectionName; path = $path; expected = $expected; actual = $actual; existing = $true; matched = $ok })
            if (-not $ok) { $reports.Add("MISMATCH: $path expected=$expected actual=$actual") }
        }
    }
    $reports.Add(("verified: {0} matched, {1} not matched" -f $matched, $mismatched))
    $reports.Add('')
}

# Commission section 25 expectations (including manifest.json itself where applicable).
Test-Manifest 'docs/reviews/evidence/B3-C-Q1/manifest.json' 29
Test-Manifest 'docs/reviews/evidence/B3-C-IMPL-01/manifest.json' 26
Test-Manifest 'docs/reviews/evidence/B3-B-Q3/manifest.json' 33
Test-Manifest 'docs/reviews/evidence/B3-B-R2-IMPL-01/manifest.json' 29
Test-Manifest 'docs/reviews/evidence/B3-B-Q2/manifest.json' 38
Test-Manifest 'docs/reviews/evidence/B3-B-Q1/manifest.json' 12
Test-Manifest 'docs/reviews/evidence/B3-A-Q1/manifest.json' 14
Test-Manifest 'docs/reviews/evidence/B3-A-R1-IMPL-01/manifest.json' 13
Test-Manifest 'docs/reviews/evidence/B3-A-Q2/manifest.json' 15
Test-Manifest 'docs/reviews/evidence/B0-B-DRV-INT-01/manifest.json' 24
Test-Manifest 'docs/reviews/evidence/B0-B-Q1/manifest.json' 119

$reports.Add('=== B3 owner decision byte-identity (working tree vs committed blob at HEAD) ===')
$ownerDocs = @(
    'docs/decisions/B3-A-OWNER-01-owner-acceptance.md',
    'docs/decisions/B3-B-OWNER-01-owner-acceptance.md',
    'docs/decisions/B3-C-OWNER-01-owner-acceptance.md'
)
foreach ($doc in $ownerDocs) {
    if (-not (Test-Path $doc)) {
        $reports.Add("MISSING owner decision: $doc")
        continue
    }
    $worktreeHash = git hash-object $doc
    $headEntry = git ls-tree HEAD -- $doc
    $headHash = if ($headEntry -match '([0-9a-f]{40})') { $Matches[1] } else { 'ABSENT' }
    $identical = $worktreeHash -eq $headHash
    $reports.Add("$doc")
    $reports.Add("  head blob:    $headHash")
    $reports.Add("  working tree: $worktreeHash")
    $reports.Add("  byte-identical to HEAD: $identical")
}

$reports | Out-File -FilePath "$PSScriptRoot\logs\07-historical-manifests.txt" -Encoding utf8
$results | ConvertTo-Json -Depth 4 | Out-File -FilePath "$PSScriptRoot\historical-integrity.json" -Encoding utf8

$total = $results.Count
$matchedTotal = ($results | Where-Object { $_.matched }).Count
"total entries: $total ; matched: $matchedTotal ; unmatched: $($total - $matchedTotal)"
$sha.Dispose()
