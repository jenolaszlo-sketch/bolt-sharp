# B5-B-Q1 independent reviewer verification: SHA-256 re-verification of the producer B5-B
# evidence manifest (expected 62/62), all thirteen historical evidence manifests
# (expected 380/380), and byte-identity of the four accepted owner decision documents.
# Written independently for B5-B-Q1; hashes are recomputed from working-tree bytes.
$ErrorActionPreference = 'Continue'
Set-Location $PSScriptRoot\..\..\..\..

$sha = [System.Security.Cryptography.SHA256]::Create()
$lines = New-Object System.Collections.Generic.List[string]
$all = New-Object System.Collections.Generic.List[object]

function Test-Section {
    param([string]$ManifestPath, [int]$ExpectedCount)

    $lines.Add("=== $ManifestPath (expected $ExpectedCount) ===")
    if (-not (Test-Path $ManifestPath)) {
        $lines.Add("RESULT: MISSING MANIFEST")
        return
    }

    $json = Get-Content $ManifestPath -Raw | ConvertFrom-Json
    $sections = @()
    foreach ($name in @('evidence_files', 'probe_source_files', 'report_files', 'changed_source_and_report_files', 'files', 'reports')) {
        $prop = $json.PSObject.Properties[$name]
        if ($prop -and $null -ne $prop.Value) { $sections += ,@($name, @($prop.Value)) }
    }
    $declared = ($sections | ForEach-Object { $_[1].Count } | Measure-Object -Sum).Sum
    $matched = 0
    $unmatched = 0
    foreach ($pair in $sections) {
        foreach ($entry in $pair[1]) {
            $path = $entry.path
            $expected = $entry.sha256
            $record = [pscustomobject]@{ manifest = $ManifestPath; path = $path; matched = $false }
            if ($null -eq $path -or $null -eq $expected -or -not (Test-Path $path)) {
                $lines.Add("UNMATCHED: $path (missing or malformed)")
                $unmatched++
                $all.Add($record)
                continue
            }
            $bytes = [System.IO.File]::ReadAllBytes((Join-Path (Get-Location) $path))
            $actual = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLower()
            $ok = $actual -eq $expected.ToLower()
            if ($ok) { $matched++ } else { $unmatched++; $lines.Add("MISMATCH: $path expected=$expected actual=$actual") }
            $record.matched = $ok
            $all.Add($record)
        }
    }
    $lines.Add(("declared: {0}; matched: {1}; unmatched: {2}" -f $declared, $matched, $unmatched))
    $lines.Add('')
}

Test-Section 'docs/reviews/evidence/B5-B-IMPL-01/manifest.json' 62

Test-Section 'docs/reviews/evidence/B5-A-Q1/manifest.json' 10
Test-Section 'docs/reviews/evidence/B5-A-IMPL-01/manifest.json' 18
Test-Section 'docs/reviews/evidence/B3-C-Q1/manifest.json' 29
Test-Section 'docs/reviews/evidence/B3-C-IMPL-01/manifest.json' 26
Test-Section 'docs/reviews/evidence/B3-B-Q3/manifest.json' 33
Test-Section 'docs/reviews/evidence/B3-B-R2-IMPL-01/manifest.json' 29
Test-Section 'docs/reviews/evidence/B3-B-Q2/manifest.json' 38
Test-Section 'docs/reviews/evidence/B3-B-Q1/manifest.json' 12
Test-Section 'docs/reviews/evidence/B3-A-Q1/manifest.json' 14
Test-Section 'docs/reviews/evidence/B3-A-R1-IMPL-01/manifest.json' 13
Test-Section 'docs/reviews/evidence/B3-A-Q2/manifest.json' 15
Test-Section 'docs/reviews/evidence/B0-B-DRV-INT-01/manifest.json' 24
Test-Section 'docs/reviews/evidence/B0-B-Q1/manifest.json' 119

$lines.Add('=== accepted owner decision byte-identity (working tree vs HEAD blob) ===')
foreach ($doc in @(
    'docs/decisions/B3-A-OWNER-01-owner-acceptance.md',
    'docs/decisions/B3-B-OWNER-01-owner-acceptance.md',
    'docs/decisions/B3-C-OWNER-01-owner-acceptance.md',
    'docs/decisions/B5-A-OWNER-01-owner-acceptance.md')) {
    $worktreeHash = git hash-object $doc
    $headEntry = git ls-tree HEAD -- $doc
    $headHash = if ($headEntry -match '([0-9a-f]{40})') { $Matches[1] } else { 'ABSENT' }
    $lines.Add($doc + ": head=" + $headHash + " worktree=" + $worktreeHash + " identical=" + ($worktreeHash -eq $headHash))
}

$total = $all.Count
$matchedTotal = ($all | Where-Object { $_.matched }).Count
$lines.Add('')
$lines.Add("TOTAL: $total entries; matched: $matchedTotal; unmatched: $($total - $matchedTotal)")
$lines | Out-File -FilePath "$PSScriptRoot\01-manifest-verification.txt" -Encoding utf8
$all | ConvertTo-Json -Depth 3 | Out-File -FilePath "$PSScriptRoot\01-manifest-verification-detail.json" -Encoding utf8
$sha.Dispose()
"TOTAL: $total; matched: $matchedTotal; unmatched: $($total - $matchedTotal)"
