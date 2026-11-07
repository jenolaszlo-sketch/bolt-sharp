param([Parameter(Mandatory=$true)][string]$Scratch, [string]$Probe = (Join-Path $PSScriptRoot 'probe'))
$ErrorActionPreference = 'Stop'
$assembly = Join-Path $Scratch 'artifacts/bin/BoltSharp/debug/BoltSharp.dll'
$evidence = Join-Path $Scratch 'evidence'
$artifacts = Join-Path $Scratch 'probe-artifacts'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$buildLog = Join-Path $evidence 'q2-build.log'
"COMMAND: dotnet build $Probe/Q2Probe.csproj -c Debug -p:BoltSharpAssembly=$assembly --artifacts-path $artifacts" | Set-Content -LiteralPath $buildLog
dotnet build (Join-Path $Probe 'Q2Probe.csproj') -c Debug "-p:BoltSharpAssembly=$assembly" --artifacts-path $artifacts >> $buildLog 2>&1
$code = $LASTEXITCODE
"EXIT: $code" | Add-Content -LiteralPath $buildLog
Get-Content -LiteralPath $buildLog
if ($code -ne 0) { throw 'Q2 probe build failed' }
$runLog = Join-Path $evidence 'q2-run.log'
$dll = Join-Path $artifacts 'bin/Q2Probe/debug/B3BQ2.Probe.dll'
$results = Join-Path $evidence 'q2-probe-results.json'
"COMMAND: dotnet $dll $results" | Set-Content -LiteralPath $runLog
dotnet $dll $results >> $runLog 2>&1
$code = $LASTEXITCODE
"EXIT: $code" | Add-Content -LiteralPath $runLog
Get-Content -LiteralPath $runLog
