# B5-B-TS1-Q1 evidence self-verifier.
# Verifies SHA-256 over Git blob bytes for every manifest entry, the package inventory, and the
# qualified-parent/scope invariants. Read-only: writes only a temporary blob file.
#
# Usage (from any directory):
#   powershell -NoProfile -ExecutionPolicy Bypass -File docs/reviews/evidence/B5-B-TS1-Q1/Verify-Evidence.ps1
#   powershell ... -File ... -Commit <qualification-commit>
#
# Default Commit is HEAD. Run at the qualification commit (or pass its hash from a later checkout).

param(
    [string]$Commit = 'HEAD'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
Set-Location $repo
$manifestPath = Join-Path $PSScriptRoot 'manifest.json'
$evidenceRel = 'docs/reviews/evidence/B5-B-TS1-Q1'
$reportRel = 'docs/reviews/B5-B-TS1-Q1-qualification.md'
$tmp = Join-Path $env:TEMP ("b5b-ts1-q1-verify-{0}.tmp" -f [Guid]::NewGuid().ToString('N'))
$sha = [System.Security.Cryptography.SHA256]::Create()
$failures = New-Object System.Collections.Generic.List[string]

function Get-CommitHash([string]$rev) {
    $h = git rev-parse --verify "$rev^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) { throw "cannot resolve commit: $rev" }
    return $h.Trim()
}
function Get-BlobHash([string]$commit, [string]$path) {
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
    & cmd /c "git cat-file blob $commit`:$path > `"$tmp`"" 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    $bytes = [System.IO.File]::ReadAllBytes($tmp)
    $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant()
    return [pscustomobject]@{ Hash = $hash; Bytes = $bytes.Length }
}

try {
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "missing manifest: $manifestPath" }
    $commitHash = Get-CommitHash $Commit
    $parent = (git rev-parse "$commitHash^" 2>$null).Trim()
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

    Write-Output "B5-B-TS1-Q1 evidence verification"
    Write-Output "=================================="
    Write-Output "Repository: $repo"
    Write-Output "Commit:     $commitHash"
    Write-Output "Parent:     $parent"
    Write-Output "Manifest:   $manifestPath"
    Write-Output ""

    if ($manifest.qualified_head -ne $parent) {
        $failures.Add("qualified_head mismatch: manifest=$($manifest.qualified_head) commit-parent=$parent")
    }
    if ($manifest.qualified_tree -ne (git rev-parse "$parent^{tree}").Trim()) {
        $failures.Add("qualified_tree mismatch: manifest=$($manifest.qualified_tree) parent-tree=$((git rev-parse "$parent^{tree}").Trim())")
    }
    if ($manifest.entry_count -ne @($manifest.entries).Count) {
        $failures.Add("entry_count mismatch: manifest=$($manifest.entry_count) actual=$(@($manifest.entries).Count)")
    }

    $paths = New-Object System.Collections.Generic.List[string]
    $matched = 0
    foreach ($entry in @($manifest.entries)) {
        $p = $entry.path
        $paths.Add($p)
        $actual = Get-BlobHash $commitHash $p
        if ($null -eq $actual) {
            $failures.Add("MISSING BLOB: $p")
            Write-Output "MISSING  $p"
            continue
        }
        if ($actual.Hash -ne $entry.sha256.ToLowerInvariant()) {
            $failures.Add("HASH MISMATCH: $p expected=$($entry.sha256) actual=$($actual.Hash)")
            Write-Output "MISMATCH $p"
            continue
        }
        if ($actual.Bytes -ne [int]$entry.bytes) {
            $failures.Add("BYTE-COUNT MISMATCH: $p expected=$($entry.bytes) actual=$($actual.Bytes)")
            Write-Output "MISMATCH $p (bytes)"
            continue
        }
        $matched++
        Write-Output "OK       $p"
    }

    $duplicates = @($paths | Group-Object | Where-Object { $_.Count -gt 1 })
    foreach ($dup in $duplicates) { $failures.Add("DUPLICATE entry: $($dup.Name) x$($dup.Count)") }

    # Completeness: every evidence file except manifest.json must be listed.
    $onDisk = @(Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object { $_.Name -ne 'manifest.json' } | ForEach-Object { "$evidenceRel/$($_.Name)" })
    $missingFromManifest = @($onDisk | Where-Object { $paths -notcontains $_ })
    $missingOnDisk = @($paths | Where-Object { $_ -like "$evidenceRel/*" -and -not (Test-Path -LiteralPath $_) })
    foreach ($p in $missingFromManifest) { $failures.Add("EVIDENCE FILE NOT IN MANIFEST: $p") }
    foreach ($p in $missingOnDisk) { $failures.Add("MANIFEST ENTRY NOT ON DISK: $p") }

    if ($paths -notcontains $reportRel) { $failures.Add("REPORT NOT IN MANIFEST: $reportRel") }
    if (-not (Test-Path -LiteralPath $reportRel)) { $failures.Add("REPORT MISSING ON DISK: $reportRel") }

    # Scope: the qualification commit may add only the report and the evidence directory.
    $changed = @(git diff-tree --no-commit-id --name-only -r $commitHash)
    $unexpected = @($changed | Where-Object { $_ -ne $reportRel -and -not $_.StartsWith("$evidenceRel/") })
    foreach ($p in $unexpected) { $failures.Add("UNEXPECTED PATH IN QUALIFICATION COMMIT: $p") }
    $deleted = @(git diff-tree --no-commit-id --diff-filter=D --name-only -r $commitHash)
    foreach ($p in $deleted) { $failures.Add("DELETION IN QUALIFICATION COMMIT: $p") }

    Write-Output ""
    Write-Output "entries: $matched/$(@($manifest.entries).Count) matched"
    Write-Output "evidence files on disk: $($onDisk.Count); not listed: $($missingFromManifest.Count); listed but absent: $($missingOnDisk.Count)"
    Write-Output "qualification commit changed paths: $($changed.Count); unexpected: $($unexpected.Count); deletions: $($deleted.Count)"
    if ($failures.Count -eq 0) {
        Write-Output "RESULT: PASS - all entries, inventory and scope checks verified."
        exit 0
    }
    Write-Output ""
    Write-Output "RESULT: FAIL - $($failures.Count) finding(s):"
    $failures | ForEach-Object { Write-Output "  $_" }
    exit 1
}
finally {
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
    $sha.Dispose()
}
