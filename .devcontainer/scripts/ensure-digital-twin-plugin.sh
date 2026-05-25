#!/usr/bin/env bash
set -euo pipefail

if [[ -x /usr/local/bin/copilot ]]; then
  COPILOT_BIN="/usr/local/bin/copilot"
elif command -v copilot >/dev/null 2>&1; then
  COPILOT_BIN="$(command -v copilot)"
else
  echo "ERROR: copilot CLI is required to install Digital Twin plugin." >&2
  exit 1
fi

PLUGIN_SOURCE="https://git.bc3.tech/bc3tech/digital-twin.git"

if "$COPILOT_BIN" plugin list | grep -qi 'digital-twin'; then
  "$COPILOT_BIN" plugin update digital-twin || true
else
  "$COPILOT_BIN" plugin install "$PLUGIN_SOURCE"
fi
