#!/bin/bash
set -euo pipefail

# Q-B5B-09-TLS-LINUX-QUAL-01-R1 local run script
# Usage: ./run_r1.sh [results-dir] [run-id] [commit] [tree] [mode]

RESULTS_DIR="${1:-$(mktemp -d -t r1-results-XXXXXX)}"
RUN_ID="${2:-$(date +%s)}"
COMMIT="${3:-$(git rev-parse HEAD)}"
TREE="${4:-$(git rev-parse HEAD^{tree})}"
MODE="${5:-full}"

echo "R1 Campaign: Q-B5B-09-TLS-LINUX-QUAL-01-R1"
echo "Results: $RESULTS_DIR"
echo "Run ID: $RUN_ID"
echo "Commit: $COMMIT"
echo "Tree: $TREE"
echo "Mode: $MODE"

mkdir -p "$RESULTS_DIR"

# Build the harness
cd "$(dirname "$0")/harness"
dotnet build R1Harness.csproj -c Release

# Run the campaign
dotnet run --project R1Harness.csproj -c Release -- \
  --results "$RESULTS_DIR" \
  --run-id "$RUN_ID" \
  --commit "$COMMIT" \
  --tree "$TREE" \
  --mode "$MODE" \
  --python python3 \
  --peer-dir "$(dirname "$0")/peer"

EXIT_CODE=$?

if [ $EXIT_CODE -eq 0 ]; then
    echo "Campaign completed successfully"
    echo "Running verifier..."
    cd "$(dirname "$0")/verifier"
    python3 verify_r1.py \
      --results "$RESULTS_DIR" \
      --commit "$COMMIT" \
      --tree "$TREE" \
      --mode "$MODE"
    VERIFIER_EXIT=$?
    if [ $VERIFIER_EXIT -eq 0 ]; then
        echo "VERIFIER: PASS"
    else
        echo "VERIFIER: FAIL"
        exit 1
    fi
else
    echo "Campaign failed with exit code $EXIT_CODE"
    exit $EXIT_CODE
fi