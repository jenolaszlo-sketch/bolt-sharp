# B5-A-IMPL-01 evidence manifest generator.
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
        path = "docs/reviews/evidence/B5-A-IMPL-01/$relative"
        sha256 = $hash
        bytes = $bytes.Length
    })
}
$sha.Dispose()

$manifest = [pscustomobject]@{
    task = 'B5-A-IMPL-01 - TCP/TLS transport and Bolt 5.8 network integration (producer evidence)'
    role = 'Implementation agent'
    base_head = '853580659cc57ad959c026800c1e17de44cdddc1'
    base_tree = 'a00ab7b189608da4ebf5a61ead6557e402d0bac4'
    implementation_commits = @(
        '943774c7388ad510090cdd6bf1664cd6e53ffb6b',
        '07bcc29ddba139a7a644e76d11a8b047e5b4bb2a',
        '11e5c85d22a7b3832e6721c72c39c2cf96502c68'
    )
    transport_suite_tests = 80
    full_regression_tests = 1217
    driver_integration_tests = 19
    build_warnings = 2
    build_warnings_detail = 'the two pre-existing xUnit2012 advisories at tests/BoltSharp.Tests/Messages/BoltRequestDecodeTests.cs:71-72'
    historical_manifest_entries_verified = 352
    generated_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    hash_convention = 'SHA-256 over exact working-tree bytes (canonical checkout form); manifest.json itself is excluded from the inventory'
    evidence_files = $entries
}
$manifest | ConvertTo-Json -Depth 5 | Out-File -FilePath (Join-Path $PSScriptRoot 'manifest.json') -Encoding utf8
"manifest written with $($entries.Count) entries"
