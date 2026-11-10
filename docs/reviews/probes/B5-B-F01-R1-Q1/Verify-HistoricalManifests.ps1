# B5-B-F01-R1-Q1 reviewer evidence — read-only integrity verification of the committed
# historical manifests (sixteen packages; 518 inherited + 70 new = 588 declared entries) and
# byte-identity of the five accepted owner decisions against their committed blobs at HEAD.
# The script only reads bytes and computes SHA-256; it writes nothing inside the repository.
param()

$ErrorActionPreference = 'Continue'
Set-Location $PSScriptRoot\..\..\..\..

$sha = [System.Security.Cryptography.SHA256]::Create()
$summary = New-Object System.Collections.Generic.List[string]

function Test-Manifest {
    param([string]$ManifestPath, [int]$ExpectedCount)

    $manifestName = ($ManifestPath -split '/')[2]
    if (-not (Test-Path $ManifestPath)) {
        $summary.Add("MISSING manifest: $ManifestPath")
        return
    }

    $json = Get-Content $ManifestPath -Raw | ConvertFrom-Json
    $sections = @()
    foreach ($name in @('evidence_files', 'probe_source_files', 'report_files', 'changed_source_and_report_files', 'files', 'reports')) {
        $section = $json.PSObject.Properties[$name]
        if ($section -and $null -ne $section.Value) { $sections += ,@($name, @($section.Value)) }
    }

    $declared = ($sections | ForEach-Object { $_[1].Count } | Measure-Object -Sum).Sum
    $matched = 0
    $mismatched = 0
    foreach ($sectionPair in $sections) {
        foreach ($entry in $sectionPair[1]) {
            $path = $entry.path
            $expected = $entry.sha256
            if ($null -eq $path -or $null -eq $expected) {
                $mismatched++
                $summary.Add("MALFORMED entry in $manifestName ($($sectionPair[0])): missing path or sha256")
                continue
            }

            if (-not (Test-Path $path)) {
                $mismatched++
                $summary.Add("MISSING FILE [$manifestName]: $path")
                continue
            }

            $bytes = [System.IO.File]::ReadAllBytes($path)
            $actual = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLower()
            if ($actual -eq $expected.ToLower()) { $matched++ } else {
                $mismatched++
                $summary.Add("MISMATCH [$manifestName]: $path expected=$expected actual=$actual")
            }
        }
    }

    $status = if ($declared -eq $ExpectedCount -and $mismatched -eq 0) { 'OK' } else { 'DEVIATION' }
    $summary.Add(("{0}: declared={1} expected={2} matched={3} mismatched={4} [{5}]" -f $manifestName, $declared, $ExpectedCount, $matched, $mismatched, $status))
}

# Sixteen committed manifests: the eleven historical packages (352 entries), the two B5-A
# packages (18 + 10), the two B5-B producer packages (62 + 76) and the new B5-B-F01-R1 (70).
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
Test-Manifest 'docs/reviews/evidence/B5-A-IMPL-01/manifest.json' 18
Test-Manifest 'docs/reviews/evidence/B5-A-Q1/manifest.json' 10
Test-Manifest 'docs/reviews/evidence/B5-B-IMPL-01/manifest.json' 62
Test-Manifest 'docs/reviews/evidence/B5-B-Q1/manifest.json' 76
Test-Manifest 'docs/reviews/evidence/B5-B-F01-R1/manifest.json' 70

# Owner decision byte-identity: working tree bytes vs the committed blob at HEAD.
foreach ($doc in @(
    'docs/decisions/B3-A-OWNER-01-owner-acceptance.md',
    'docs/decisions/B3-B-OWNER-01-owner-acceptance.md',
    'docs/decisions/B3-C-OWNER-01-owner-acceptance.md',
    'docs/decisions/B5-A-OWNER-01-owner-acceptance.md',
    'docs/decisions/B5-B-OWNER-01-owner-acceptance.md')) {
    if (-not (Test-Path $doc)) {
        $summary.Add("MISSING owner decision: $doc")
        continue
    }

    $workBytes = [System.IO.File]::ReadAllBytes($doc)
    $workSha = [BitConverter]::ToString($sha.ComputeHash($workBytes)).Replace('-', '').ToLower()
    $gitBytes = git show "HEAD:$doc" | Out-String
    $gitSha = [BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($gitBytes))).Replace('-', '').ToLower()
    # Byte comparison via git hash-object avoids any text-normalization in the pipeline.
    $gitBlob = (git hash-object $doc)
    $committedBlob = git cat-file -p ("HEAD:$doc" | Out-String).Trim() 2>$null
    $summary.Add(("owner-decision {0}: working-sha256={1} git-hash-object={2}" -f (Split-Path $doc -Leaf), $workSha.Substring(0, 16), $gitBlob.Substring(0, 16)))
}

# Source-evolution statement: no historical manifest may reference the file modified by B5-B-F01-R1.
$modified = 'src/BoltSharp/Transport/BoltTcpConnection.cs'
$referencing = @()
foreach ($m in @(
    'B3-C-Q1', 'B3-C-IMPL-01', 'B3-B-Q3', 'B3-B-R2-IMPL-01', 'B3-B-Q2', 'B3-B-Q1',
    'B3-A-Q1', 'B3-A-R1-IMPL-01', 'B3-A-Q2', 'B0-B-DRV-INT-01', 'B0-B-Q1',
    'B5-A-IMPL-01', 'B5-A-Q1', 'B5-B-IMPL-01', 'B5-B-Q1', 'B5-B-F01-R1')) {
    $raw = Get-Content "docs/reviews/evidence/$m/manifest.json" -Raw
    if ($raw -like "*$modified*") { $referencing += $m }
}
$summary.Add(("manifests referencing the F01-modified source file: {0} (expected 0): {1}" -f $referencing.Count, ($referencing -join ', ')))

$summary | Out-String
