# B5-B-IMPL-01 evidence manifest generator.
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
        path = "docs/reviews/evidence/B5-B-IMPL-01/$relative"
        sha256 = $hash
        bytes = $bytes.Length
    })
}
$sha.Dispose()

$manifest = [pscustomobject]@{
    task = 'B5-B-IMPL-01 - official Neo4j .NET driver interoperability (producer evidence)'
    role = 'Implementation agent'
    base_head = '2bebd3ee77b12c190b2e48b763deb4444cc94e5d'
    base_tree = '346611f5ebcd878432c924e50f0ff9d255b6822b'
    driver_package = 'Neo4j.Driver 6.4.0 (exact pin, test-only)'
    b5b_interop_tests = 40
    full_regression_tests = 1217
    transport_suite_tests = 80
    b3c_backend_tests = 94
    b3b_session_tests = 119
    b3a_message_tests = 98
    b0b_driver_integration_tests = 19
    build_warnings = 2
    build_warnings_detail = 'the two pre-existing xUnit2012 advisories at tests/BoltSharp.Tests/Messages/BoltRequestDecodeTests.cs:71-72'
    historical_manifest_entries_verified = 352
    b5a_manifests_verified = '18 (producer) + 10 (Q1)'
    owner_decisions_verified_identical = 4
    generated_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    hash_convention = 'SHA-256 over exact working-tree bytes (canonical checkout form); manifest.json itself is excluded from the inventory'
    evidence_files = $entries
}
$manifest | ConvertTo-Json -Depth 5 | Out-File -FilePath (Join-Path $PSScriptRoot 'manifest.json') -Encoding utf8
"manifest written with $($entries.Count) entries"
