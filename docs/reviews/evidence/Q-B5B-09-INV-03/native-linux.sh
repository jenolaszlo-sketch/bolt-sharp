#!/usr/bin/env bash
set -euo pipefail

# Run on an already authorized native Linux .NET 8 host.
# Arguments: committed repository and a new, durable results directory.
repo=$(realpath "$1")
mkdir -p "$2"
out=$(realpath "$2")
work=$(mktemp -d)
parent=23d58da4e0c0e10aa46a31f05ef6a0c31ad3f2fa
prior="$repo/docs/reviews/evidence/Q-B5B-09-INV-02"
here="$repo/docs/reviews/evidence/Q-B5B-09-INV-03"
core=tests/BoltSharp.Tests/BoltSharp.Tests.csproj
driver=tests/BoltSharp.DriverInteropTests/BoltSharp.DriverInteropTests.csproj
export DOTNET_ROLL_FORWARD=Minor
unset DOTNET_ROLL_FORWARD_ON_NO_CANDIDATE_FX

{
    uname -sr
    dotnet --info
    printf 'temporary source %s\n' "$work"
} | tee "$out/environment.txt"
if [[ $(uname -s) != Linux || $(uname -r) == *[Mm]icrosoft* ]]; then
    printf '%s\n' 'This runner requires native Linux; use the separate WSL2 procedure for WSL2.' >&2
    exit 2
fi
dotnet --list-runtimes | grep -Eq '^Microsoft\.NETCore\.App 8\.0\.'
git -C "$repo" -c core.autocrlf=false archive --format=tar --output="$work/source.tar" "$parent"

run_test() {
    local variant="$1" label="$2" project="$3" filter="$4" exit_code=0
    dotnet test "$project" --filter "$filter" \
        --logger "trx;LogFileName=$variant-$label.trx" \
        --results-directory "$out" >"$out/$variant-$label.log" 2>&1 || exit_code=$?
    printf 'test_exit=%s\n' "$exit_code" >>"$out/$variant-$label.log"
    printf '%s %s %s\n' "$variant" "$label" "$exit_code"
}

for mode in exact adapted; do
    for flavor in baseline candidate; do
        variant="$mode-$flavor"
        mkdir "$work/$variant"
        (
            cd "$work/$variant"
            tar -xf "$work/source.tar"
            if [[ "$flavor" == candidate ]]; then
                git -c core.autocrlf=false apply "$prior/prototype.patch"
                git -c core.autocrlf=false apply "$here/refinement.patch"
                git -c core.autocrlf=false apply "$here/real-write-observation.patch"
                cp "$here/Inv03QualificationTests.cs.txt" \
                    tests/BoltSharp.Tests/Transport/Inv03QualificationTests.cs
            fi
            if [[ "$mode" == adapted ]]; then
                git -c core.autocrlf=false apply "$prior/wsl-listener-adapter.patch"
            fi
            cp "$prior/Inv02TerminalTests.cs.txt" tests/BoltSharp.Tests/Transport/Inv02TerminalTests.cs
            cp "$prior/Inv02DriverTests.cs.txt" tests/BoltSharp.DriverInteropTests/Scenarios/Inv02DriverTests.cs
            export INV02_EXPECT_CANDIDATE=0
            if [[ "$flavor" == candidate ]]; then
                export INV02_EXPECT_CANDIDATE=1
                run_test "$variant" gates "$core" 'FullyQualifiedName~Inv03QualificationTests'
            fi
            run_test "$variant" transport "$core" 'FullyQualifiedName~Inv02TerminalTests|FullyQualifiedName~UnsupportedCommandRefusalTests|FullyQualifiedName~ConnectionLifecycleTests'
            run_test "$variant" driver "$driver" 'FullyQualifiedName~Inv02DriverTests|FullyQualifiedName~B5B10_05'
        )
    done
done | tee "$out/dispositions.txt"
printf 'Review every recorded test exit. Disposable source: %s\n' "$work"
