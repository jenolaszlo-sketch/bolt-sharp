# B5-A-Q1 reviewer verification: B5-A evidence manifest + 11 historical manifests.
# Raw-byte SHA-256 of every declared entry, counted per-manifest against commissioned totals.
$ErrorActionPreference = 'Continue'
Set-Location $PSScriptRoot\..\..\..\..

$sha = [System.Security.Cryptography.SHA256]::Create()
$all = New-Object System.Collections.Generic.List[object]
$summary = New-Object System.Collections.Generic.List[string]

function Test-Manifest {
    param([string]$ManifestPath, [int]$ExpectedTotal)
    $json = Get-Content $ManifestPath -Raw | ConvertFrom-Json
    $sections = @()
    foreach ($name in @('evidence_files', 'probe_source_files', 'report_files', 'changed_source_and_report_files', 'files', 'reports')) {
        $section = $json.PSObject.Properties[$name]
        if ($section -and $null -ne $section.Value) { $sections += ,@($name, @($section.Value)) }
    }
    $declared = ($sections | ForEach-Object { $_[1].Count } | Measure-Object -Sum).Sum
    $matched = 0; $bad = 0
    foreach ($pair in $sections) {
        foreach ($entry in $pair[1]) {
            if ($null -eq $entry.path -or $null -eq $entry.sha256) { $bad++; continue }
            if (-not (Test-Path $entry.path)) { $bad++; $all.Add([pscustomobject]@{ m=$ManifestPath; p=$entry.path; ok=$false; why='missing' }); continue }
            $bytes = [System.IO.File]::ReadAllBytes($entry.path)
            $actual = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','').ToLower()
            if ($actual -eq $entry.sha256.ToLower()) { $matched++ }
            else { $bad++; $all.Add([pscustomobject]@{ m=$ManifestPath; p=$entry.path; ok=$false; why='hash mismatch' }) }
        }
    }
    $summary.Add(("{0} | declared={1} expected={2} matched={3} bad={4}" -f $ManifestPath, $declared, $ExpectedTotal, $matched, $bad))
}

Test-Manifest 'docs/reviews/evidence/B5-A-IMPL-01/manifest.json' 19
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
$sha.Dispose()

$summary | ForEach-Object { $_ }
$all | ConvertTo-Json -Depth 3 | Out-File -FilePath "$PSScriptRoot\..\..\evidence\B5-A-Q1\integrity-failures.json" -Encoding utf8 -ErrorAction SilentlyContinue
$summary | Out-File -FilePath "$PSScriptRoot\..\..\evidence\B5-A-Q1\integrity-summary.txt" -Encoding utf8 -ErrorAction SilentlyContinue
