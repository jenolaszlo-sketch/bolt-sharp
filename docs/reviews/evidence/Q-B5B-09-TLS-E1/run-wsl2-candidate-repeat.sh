#!/bin/sh
# Q-B5B-09-TLS-E1 WSL2 repeatability runner (fresh journal; console transcript captured).
# Portable net8.0 assembly built with Windows SDK 10.0.401 on the isolated candidate
# (23d58da + INV-02 prototype + INV-03 refinements + real-write observation + gate link
# observation + INV-02 typed-linger WSL2 listener adapter), executed on the installed Linux
# .NET 10.0.14 runtime via DOTNET_ROLL_FORWARD=Major.
cd /mnt/c/Windows/Temp/opencode/q-b5b-09-tls-e1 || exit 2
DLL=wsl2cand/tests/BoltSharp.Tests/bin/Debug/net8.0/BoltSharp.Tests.dll
OUT=/mnt/c/Windows/Temp/opencode/q-b5b-09-tls-e1/out
export DOTNET_ROLL_FORWARD=Major
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export BOLTSHARP_TLS_E1_JOURNAL=$OUT/wsl2-campaign-repeat.jsonl
dotnet vstest "$DLL" "--TestCaseFilter:FullyQualifiedName~TlsE1Tests.FixedCampaign"