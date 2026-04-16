#!/usr/bin/env bash
set -euo pipefail

PROJECT_PATH="/Users/bigrorz/UnityProjects/MultipleStyleRendering"

APP_NAME="MultipleStyleRendering"
PLAYER_EXEC="$PROJECT_PATH/Build/UnityProfiling.app/Contents/MacOS/$APP_NAME"

FRAMES="${1:-1000}"
WARMUP="${2:-500}"

if [[ ! -f "$PLAYER_EXEC" ]]; then
  echo "ERROR: Build not found at:"
  echo "$PLAYER_EXEC"
  exit 1
fi

echo "==> Running benchmarks..."

"$PLAYER_EXEC" \
  -logFile - \
  -runTests \
  -frames "$FRAMES" \
  -warmup "$WARMUP"