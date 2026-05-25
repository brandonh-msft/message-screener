#!/usr/bin/env bash
set -euo pipefail

if [[ -x /usr/local/bin/copilot ]]; then
  COPILOT_BIN="/usr/local/bin/copilot"
elif command -v copilot >/dev/null 2>&1; then
  COPILOT_BIN="$(command -v copilot)"
else
  echo "ERROR: copilot CLI is required to install WorkIQ plugin." >&2
  exit 1
fi

if "$COPILOT_BIN" plugin list | grep -qi '^workiq\b'; then
  "$COPILOT_BIN" plugin update workiq || true
else
  "$COPILOT_BIN" plugin install workiq@copilot-plugins
fi
