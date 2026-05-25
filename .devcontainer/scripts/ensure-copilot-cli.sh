#!/usr/bin/env bash
set -euo pipefail

is_usable_copilot() {
  if [[ -x /usr/local/bin/copilot ]]; then
    COPILOT_BIN="/usr/local/bin/copilot"
  elif command -v copilot >/dev/null 2>&1; then
    COPILOT_BIN="$(command -v copilot)"
  else
    return 1
  fi

  if grep -Eq '/\.vscode-server(-insiders)?/.*/github\.copilot-chat/copilotCli/copilot$' <<<"$COPILOT_BIN"; then
    return 1
  fi

  local out
  out="$("$COPILOT_BIN" --version 2>&1 || true)"
  if [[ -z "$out" ]]; then
    return 1
  fi

  # VS Code shim prints this when the real CLI is not installed.
  if grep -qi "Cannot find GitHub Copilot CLI" <<<"$out"; then
    return 1
  fi

  return 0
}

if ! command -v gh >/dev/null 2>&1; then
  echo "ERROR: gh is required but not found." >&2
  exit 1
fi

if ! is_usable_copilot; then
  echo "ERROR: copilot CLI is not usable. Ensure devcontainer feature ghcr.io/devcontainers/features/copilot-cli:1 is present and rebuild container." >&2
  exit 1
fi

# Ensure standalone copilot CLI is operational.
"$COPILOT_BIN" --help >/dev/null
