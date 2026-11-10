# B5-B-F01-R1-Q1 — post-commit self-check: verify the committed evidence bytes against the
# committed manifest. Read-only.
param()
$ErrorActionPreference = 'Stop'
$sha = [System.Security.Cryptography.SHA256]::Create()
$fail = 0
$checked = 0
foreach ($line in (Get-Content 'docs/reviews/evidence/B5-B-F01-R1-Q1/manifest.json')) {
    if ($line -match '"path": "([^"]+)", "sha256": "([a-f0-9]{64})", "bytes": (\d+)') {
        $p = $Matches[1]
        $h = $Matches[2]
        $b = [int]$Matches[3]
        if (Test-Path $p) {
            $bytes = [System.IO.File]::ReadAllBytes($p)
            $actual = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant()
            $checked++
            if ($actual -ne $h) {
                $fail++
                Write-Output "MISMATCH: $p"
            }
            if ($bytes.Length -ne $b) {
                $fail++
                Write-Output "SIZE MISMATCH: $p"
            }
        } else {
            $fail++
            Write-Output "MISSING: $p"
        }
    }
}
Write-Output "post-commit manifest self-check: $checked entries checked, $fail failures"
