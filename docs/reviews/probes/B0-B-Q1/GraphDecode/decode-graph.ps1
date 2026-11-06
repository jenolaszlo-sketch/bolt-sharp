<#
.SYNOPSIS
  Independent B0-B-Q1 decoder for the graph RECORD captured in producer evidence.

.DESCRIPTION
  Reads the exact hex recorded in the producer's t04-graph-values.json, decodes only the
  PackStream 1.0 markers needed for this record (null/boolean/integer/string/list/map/tiny
  structure), and compares the decoded values against the producer fixture source expectations.
  This script does not use BoltSharp or any BoltSharp codec.

.NOTES
  Reviewer-only. Input evidence is treated as immutable.
#>
[CmdletBinding()]
param(
    [string]$EvidencePath = "docs/reviews/evidence/B0-B-Q1/producer-reproduction/t04-graph-values.json",
    [string]$OutputPath = "docs/reviews/evidence/B0-B-Q1/graph-independent-decode.json"
)

$ErrorActionPreference = "Stop"

$document = Get-Content -LiteralPath $EvidencePath -Raw | ConvertFrom-Json
$recordFrame = $document.frames | Where-Object { $_.name -eq "RECORD" } | Select-Object -First 1
if (-not $recordFrame) {
    throw "No RECORD frame found in $EvidencePath"
}

$hex = ($recordFrame.hex -replace '\s', '')
$bytes = [byte[]]::new($hex.Length / 2)
for ($i = 0; $i -lt $bytes.Length; $i++) {
    $bytes[$i] = [Convert]::ToByte($hex.Substring($i * 2, 2), 16)
}

$script:position = 0

function Read-U8 {
    if ($script:position -ge $bytes.Length) { throw "Truncated at offset $script:position" }
    $value = $bytes[$script:position]
    $script:position++
    return $value
}

function Read-BEInt([int]$count, [bool]$signed) {
    $raw = [byte[]]::new($count)
    for ($i = 0; $i -lt $count; $i++) { $raw[$i] = Read-U8 }
    $unsigned = [long]0
    foreach ($b in $raw) { $unsigned = ($unsigned * 256) + $b }
    if ($signed) {
        $bits = 8 * $count
        $signBit = [long][math]::Pow(2, $bits - 1)
        if ($unsigned -ge $signBit) {
            return $unsigned - [long][math]::Pow(2, $bits)
        }
    }
    return $unsigned
}

function Read-Utf8([int]$length) {
    $raw = [byte[]]::new($length)
    for ($i = 0; $i -lt $length; $i++) { $raw[$i] = Read-U8 }
    return [System.Text.Encoding]::UTF8.GetString($raw)
}

function Read-Value {
    $marker = Read-U8

    if ($marker -le 0x7F) { return [long]$marker }
    if ($marker -ge 0x80 -and $marker -le 0x8F) { return Read-Utf8 ($marker -band 0x0F) }
    if ($marker -ge 0x90 -and $marker -le 0x9F) {
        $items = [System.Collections.Generic.List[object]]::new()
        for ($i = 0; $i -lt ($marker -band 0x0F); $i++) { $items.Add((Read-Value)) }
        return ,$items.ToArray()
    }
    if ($marker -ge 0xA0 -and $marker -le 0xAF) {
        $map = [ordered]@{}
        for ($i = 0; $i -lt ($marker -band 0x0F); $i++) {
            $key = Read-Value
            $map[$key] = Read-Value
        }
        return $map
    }
    if ($marker -ge 0xB0 -and $marker -le 0xBF) {
        $fieldCount = $marker -band 0x0F
        $tag = Read-U8
        $fields = [System.Collections.Generic.List[object]]::new()
        for ($i = 0; $i -lt $fieldCount; $i++) { $fields.Add((Read-Value)) }
        return [ordered]@{ marker = ('0x{0:X2}' -f $marker); tag = ('0x{0:X2}' -f $tag); field_count = $fieldCount; fields = $fields.ToArray() }
    }

    switch ($marker) {
        0xC0 { return $null }
        0xC2 { return $false }
        0xC3 { return $true }
        0xC8 { return [long](Read-BEInt 1 $true) }
        0xC9 { return [long](Read-BEInt 2 $true) }
        0xCA { return [long](Read-BEInt 4 $true) }
        0xCB { return [long](Read-BEInt 8 $true) }
        0xD0 { return Read-Utf8 (Read-U8) }
        0xD1 { return Read-Utf8 (([int](Read-U8) -shl 8) -bor (Read-U8)) }
        0xD4 {
            $length = Read-U8
            $items = [System.Collections.Generic.List[object]]::new()
            for ($i = 0; $i -lt $length; $i++) { $items.Add((Read-Value)) }
            return ,$items.ToArray()
        }
        0xD5 {
            $length = (([int](Read-U8) -shl 8) -bor (Read-U8))
            $items = [System.Collections.Generic.List[object]]::new()
            for ($i = 0; $i -lt $length; $i++) { $items.Add((Read-Value)) }
            return ,$items.ToArray()
        }
        0xD8 {
            $length = Read-U8
            $map = [ordered]@{}
            for ($i = 0; $i -lt $length; $i++) { $map[(Read-Value)] = Read-Value }
            return $map
        }
        0xD9 {
            $length = (([int](Read-U8) -shl 8) -bor (Read-U8))
            $map = [ordered]@{}
            for ($i = 0; $i -lt $length; $i++) { $map[(Read-Value)] = Read-Value }
            return $map
        }
        default { throw ("Unsupported marker 0x{0:X2} at offset {1}" -f $marker, ($script:position - 1)) }
    }
}

$value = Read-Value
$trailing = $bytes.Length - $script:position
if ($trailing -ne 0) { throw "Decoder did not consume the full frame; $trailing trailing byte(s)" }

if ($value.tag -ne '0x71') { throw "Expected RECORD tag 0x71, got $($value.tag)" }
if ($value.field_count -ne 1) { throw "Expected RECORD struct with 1 field, got $($value.field_count)" }

$record = $value.fields[0]
if ($record.Count -ne 3) { throw "Expected 3 record values, got $($record.Count)" }

$nodeA = $record[0]
$rel = $record[1]
$nodeB = $record[2]

$checks = @()
function Add-Check([string]$name, $expected, $actual) {
    $script:checks += [ordered]@{
        name = $name
        expected = $expected
        actual = $actual
        pass = (($expected -eq $actual) -or (($expected -is [array]) -and (($expected -join '|') -eq ($actual -join '|'))))
    }
}

Add-Check "record value count" 3 $record.Count
Add-Check "node A struct tag" "0x4E" $nodeA.tag
Add-Check "node A struct field count" 4 $nodeA.field_count
Add-Check "node A numeric id" 1 $nodeA.fields[0]
Add-Check "node A labels" @("Person") @($nodeA.fields[1])
Add-Check "node A name" "Alice" $nodeA.fields[2]["name"]
Add-Check "node A element id" "n:1" $nodeA.fields[3]
Add-Check "relationship struct tag" "0x52" $rel.tag
Add-Check "relationship struct field count" 8 $rel.field_count
Add-Check "relationship numeric id" 1 $rel.fields[0]
Add-Check "relationship start node id" 1 $rel.fields[1]
Add-Check "relationship end node id" 2 $rel.fields[2]
Add-Check "relationship type" "KNOWS" $rel.fields[3]
Add-Check "relationship since" 2024 $rel.fields[4]["since"]
Add-Check "relationship element id" "r:1" $rel.fields[5]
Add-Check "relationship start element id" "n:1" $rel.fields[6]
Add-Check "relationship end element id" "n:2" $rel.fields[7]
Add-Check "node B struct tag" "0x4E" $nodeB.tag
Add-Check "node B struct field count" 4 $nodeB.field_count
Add-Check "node B numeric id" 2 $nodeB.fields[0]
Add-Check "node B labels" @("Person") @($nodeB.fields[1])
Add-Check "node B name" "Bob" $nodeB.fields[2]["name"]
Add-Check "node B element id" "n:2" $nodeB.fields[3]

$result = [ordered]@{
    probe = "B0-B-Q1 independent graph decode"
    source_evidence = $EvidencePath
    source_frame_sequence = $recordFrame.sequence
    source_frame_hex = $recordFrame.hex
    decoder = "docs/reviews/probes/B0-B-Q1/GraphDecode/decode-graph.ps1 (standalone PackStream subset)"
    decoded = [ordered]@{
        struct_tag = $value.tag
        struct_fields = $value.field_count
        values = $record
    }
    expected_fixture = [ordered]@{
        node_a = "Node(id=1, elementId=n:1, labels=[Person], name=Alice)"
        relationship = "Relationship(id=1, start=1, end=2, type=KNOWS, elementId=r:1, startElementId=n:1, endElementId=n:2, since=2024)"
        node_b = "Node(id=2, elementId=n:2, labels=[Person], name=Bob)"
    }
    checks = $checks
    all_checks_pass = -not ($checks | Where-Object { -not $_.pass })
}

$json = $result | ConvertTo-Json -Depth 32
$directory = Split-Path -Parent $OutputPath
if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
[System.IO.File]::WriteAllText((Resolve-Path -LiteralPath $directory).Path + '\' + (Split-Path -Leaf $OutputPath), $json, [System.Text.UTF8Encoding]::new($false))
Write-Output $json

if (-not $result.all_checks_pass) { exit 5 }
