# B5-A-Q1 evidence manifest generator.
# SHA-256 over every evidence file's exact working-tree bytes; manifest.json excludes itself.
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
        path = "docs/reviews/evidence/B5-A-Q1/$relative"
        sha256 = $hash
        bytes = $bytes.Length
    })
}
$sha.Dispose()

$manifest = [pscustomobject]@{
    task = 'B5-A-Q1 - Independent TCP/TLS transport qualification (reviewer evidence)'
    role = 'Independent adversarial qualification reviewer'
    reviewed_head = '21e649acae8b0f04c945a9899f314f4b345a9c3a'
    reviewed_tree = 'ec5cb4a8fe52ecf85dbcfe8fb1ff1b8a9561592a'
    reviewer_model = 'GLM 5.3 Flash (provider nvidia, model z-ai/glm-5.3-flash); same OpenCode session as the producer - organizational independence NOT established (see report disclosure)'
    producer_recorded_executor = 'DeepSeek V4.1 Flash (provider nvidia, OpenCode)'
    probe_checks_total = 165
    probe_checks_passed = 162
    probe_checks_inconclusive = 3
    historical_manifest_entries_verified = 352
    regression_total_tests = 1217
    transport_tests = 80
    driver_integration_tests = 19
    disposition = 'SUPPORTED WITH QUALIFICATIONS'
    generated_utc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    hash_convention = 'SHA-256 over exact working-tree bytes; manifest.json itself is excluded from the inventory'
    evidence_files = $entries
}
$manifest | ConvertTo-Json -Depth 5 | Out-File -FilePath (Join-Path $PSScriptRoot 'manifest.json') -Encoding utf8
"manifest written with $($entries.Count) entries"
