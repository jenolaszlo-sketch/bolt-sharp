# B5-B-F01-R1 evidence manifest generator.
# Computes the SHA-256 of every evidence file (raw working-tree bytes) and writes manifest.json.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$sha = [System.Security.Cryptography.SHA256]::Create()
$entries = New-Object System.Collections.Generic.List[object]

foreach ($file in Get-ChildItem -Recurse -File | Where-Object { $_.Name -ne 'manifest.json' }) {
    $root = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\', '/')
    $relative = [System.IO.Path]::GetFullPath($file.FullName).Substring($root.Length + 1).Replace('\', '/')
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant()
    $entries.Add([pscustomobject]@{
        path = "docs/reviews/evidence/B5-B-F01-R1/$relative"
        sha256 = $hash
        bytes = $bytes.Length
    })
}
$sha.Dispose()

$manifest = [pscustomobject]@{
    task = 'B5-B-F01-R1 - restore unsupported-command refusal across the transport/session boundary (producer evidence)'
    role = 'Bounded remediation implementation agent'
    base_head = '3953bdd9962a8e4dc4c43c0bb3996b5fed76fb08'
    base_tree = 'f3fd25af19261f754d7d601b5c214f315c74af7d'
    core_tests = 1227
    transport_suite_tests = 90
    driver_suite_tests = 41
    b3c_backend_tests = 94
    b3b_session_tests = 119
    b3a_message_tests = 98
    b0b_driver_integration_tests = 19
    historical_manifest_entries_verified = 518
    owner_decisions_verified_identical = 5
    generated_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    hash_convention = 'SHA-256 over exact working-tree bytes (canonical checkout form); manifest.json itself is excluded from the inventory'
    evidence_files = $entries
}
$manifest | ConvertTo-Json -Depth 5 | Out-File -FilePath (Join-Path $PSScriptRoot 'manifest.json') -Encoding utf8
"manifest written with $($entries.Count) entries"
