#!/bin/sh
# Q-B5B-09-TLS-E1 WSL2 runner: portable net8.0 assembly built with Windows SDK 10.0.401,
# executed on the installed Linux .NET 10.0.14 runtime via DOTNET_ROLL_FORWARD=Major.
cd /mnt/c/Windows/Temp/opencode/q-b5b-09-tls-e1 || exit 2
DLL=wsl2cand/tests/BoltSharp.Tests/bin/Debug/net8.0/BoltSharp.Tests.dll
OUT=/mnt/c/Windows/Temp/opencode/q-b5b-09-tls-e1/out
export DOTNET_ROLL_FORWARD=Major
export DOTNET_CLI_TELEMETRY_OPTOUT=1
case "$1" in
  mechanism)
    export BOLTSHARP_TLS_E1_MECHANISM=$OUT/wsl2-mechanism.jsonl
    dotnet vstest "$DLL" "--TestCaseFilter:FullyQualifiedName~TlsE1MechanismTests"
    ;;
  campaign)
    export BOLTSHARP_TLS_E1_JOURNAL=$OUT/wsl2-campaign.jsonl
    dotnet vstest "$DLL" "--TestCaseFilter:FullyQualifiedName~TlsE1Tests.FixedCampaign"
    ;;
  *)
    echo "usage: run-wsl2-candidate.sh mechanism|campaign"
    exit 2
    ;;
esac