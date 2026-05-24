#!/usr/bin/env bash
set -euo pipefail

if ! command -v copilot >/dev/null 2>&1; then
  echo "ERROR: copilot CLI is required to install Digital Twin plugin." >&2
  exit 1
fi

PLUGIN_SOURCE="https://git.bc3.tech/bc3tech/digital-twin.git"

if copilot plugin list | grep -qi 'digital-twin'; then
  copilot plugin update digital-twin || true
else
  copilot plugin install "$PLUGIN_SOURCE"
fi
