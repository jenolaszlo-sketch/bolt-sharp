# B5-B-F01-R1-Q1 — generate the SHA-256 evidence manifest for this evidence directory.
# Records the hash of every file's raw working-tree bytes (the repository's canonical checkout
# form; this directory carries `* -text`). manifest.json itself is not self-hashed.
param()
$ErrorActionPreference = 'Stop'
$dir = Join-Path $PSScriptRoot '..\..\evidence\B5-B-F01-R1-Q1'
$sha = [System.Security.Cryptography.SHA256]::Create()
$files = Get-ChildItem $dir -File | Where-Object { $_.Name -ne 'manifest.json' } | Sort-Object Name
$lines = @(
    '{',
    '  "task": "B5-B-F01-R1-Q1 - independent transport/session boundary remediation qualification",',
    '  "reviewer_model": "GLM-5.3-Flash (nvidia/z-ai/glm-5.3-flash)",',
    '  "hash_convention": "SHA-256 of each file''s raw working-tree bytes",',
    '  "evidence_files": ['
)
$i = 0
foreach ($f in $files) {
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant()
    $comma = if ($i -lt $files.Count - 1) { ',' } else { '' }
    $lines += ('    {{ "path": "{0}", "sha256": "{1}", "bytes": {2} }}{3}' -f ("docs/reviews/evidence/B5-B-F01-R1-Q1/" + $f.Name), $hash, $bytes.Length, $comma)
    $i++
}
$lines += '  ]'
$lines += '}'
Set-Content -Path (Join-Path $dir 'manifest.json') -Value ($lines -join "`n") -Encoding ASCII
Write-Output "manifest written: $($files.Count) entries"
