#!/bin/sh
# Q-B5B-09-TLS-E1 WSL2 production-characterization repeat runner (fresh journal; transcript).
cd /mnt/c/Windows/Temp/opencode/q-b5b-09-tls-e1 || exit 2
DLL=production/tests/BoltSharp.Tests/bin/Debug/net8.0/BoltSharp.Tests.dll
OUT=/mnt/c/Windows/Temp/opencode/q-b5b-09-tls-e1/out
export DOTNET_ROLL_FORWARD=Major
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export BOLTSHARP_TLS_E1_PRODUCTION=$OUT/wsl2-production-repeat.jsonl
dotnet vstest "$DLL" "--TestCaseFilter:FullyQualifiedName~ProductionTlsCloseTests"