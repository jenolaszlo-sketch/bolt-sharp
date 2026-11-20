#!/bin/sh
# Q-B5B-09-TLS-E1 WSL2 runner for the production characterization fixture.
# Portable net8.0 assembly built with Windows SDK 10.0.401, executed on the installed
# Linux .NET 10.0.14 runtime via DOTNET_ROLL_FORWARD=Major. The production HEAD carries
# the typed disabled-linger initialization, so no platform adapter is applied.
cd /mnt/c/Windows/Temp/opencode/q-b5b-09-tls-e1 || exit 2
DLL=production/tests/BoltSharp.Tests/bin/Debug/net8.0/BoltSharp.Tests.dll
OUT=/mnt/c/Windows/Temp/opencode/q-b5b-09-tls-e1/out
export DOTNET_ROLL_FORWARD=Major
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export BOLTSHARP_TLS_E1_PRODUCTION=$OUT/wsl2-production.jsonl
dotnet vstest "$DLL" "--TestCaseFilter:FullyQualifiedName~ProductionTlsCloseTests"