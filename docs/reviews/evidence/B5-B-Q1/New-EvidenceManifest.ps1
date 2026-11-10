# B5-B-Q1 reviewer evidence manifest generator.
# SHA-256 over the exact working-tree bytes of every file in this directory (recursively),
# excluding this manifest itself. Convention matches the earlier evidence packages.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$sha = [System.Security.Cryptography.SHA256]::Create()
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
$files = Get-ChildItem -Recurse -File | Where-Object { $_.Name -ne 'manifest.json' } | Sort-Object FullName

$entries = foreach ($file in $files) {
    $relative = [System.IO.Path]::GetFullPath($file.FullName).Substring($repoRoot.Length).Replace('\', '/').TrimStart('/')
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLower()
    [pscustomobject]@{ path = $relative; sha256 = $hash; bytes = $bytes.Length }
}

$document = [pscustomobject]@{
    task = 'B5-B-Q1 - independent official Neo4j .NET driver interoperability qualification (reviewer evidence)'
    role = 'Independent reviewer'
    base_head = '646c572912de9a2ca415ec3548a99159cb2f4905'
    base_tree = '7670702e59933d84a685d09a0e44744a2dce34ec'
    driver_package = 'Neo4j.Driver 6.4.0 (exact pin, test-only)'
    reviewer_probes = 53
    historical_and_producer_manifest_entries_verified = 442
    credential_sentinel_scan = '0 leaks over all evidence files (plain, hex, UTF-16-hex; reviewer and producer sentinels)'
    generated_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    hash_convention = 'SHA-256 over exact working-tree bytes (raw checkout form); manifest.json itself is excluded from the inventory'
    evidence_files = $entries
}

$json = $document | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'manifest.json'), $json, (New-Object System.Text.UTF8Encoding($false)))
$sha.Dispose()
"wrote manifest.json with $($entries.Count) hashed entries"
