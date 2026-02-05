#!/usr/bin/env bash
set -euo pipefail

API_BASE_URL="${1:-}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$REPO_ROOT"

echo "[1/3] dotnet test backend/BivLauncher.sln"
dotnet test backend/BivLauncher.sln

echo "[2/3] dotnet build launcher/BivLauncher.Launcher.sln"
dotnet build launcher/BivLauncher.Launcher.sln

echo "[3/3] npm run build (admin)"
(
  cd admin
  npm run build
)

if [[ -n "$API_BASE_URL" ]]; then
  HEALTH_URL="${API_BASE_URL%/}/health"
  echo "[api] GET $HEALTH_URL"
  curl --fail --silent --show-error "$HEALTH_URL" >/dev/null
fi

echo "Smoke checks passed."
