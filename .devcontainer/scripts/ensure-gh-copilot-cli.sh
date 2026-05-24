#!/usr/bin/env bash
set -euo pipefail

if ! command -v gh >/dev/null 2>&1; then
  echo "ERROR: gh is required but not found." >&2
  exit 1
fi

if ! gh extension list | grep -q 'github/gh-copilot'; then
  gh extension install github/gh-copilot
else
  gh extension upgrade github/gh-copilot || true
fi

if ! command -v copilot >/dev/null 2>&1; then
  npm install --global @github/copilot
fi

if ! command -v copilot >/dev/null 2>&1; then
  echo "ERROR: copilot CLI is required but not found after install." >&2
  exit 1
fi

# Ensure gh copilot extension is operational.
gh copilot --help >/dev/null
copilot --help >/dev/null
