#!/usr/bin/env bash
set -euo pipefail

CONFIG_FILE="${1:-.devcontainer/gitconfig.effective}"

if [[ ! -f "$CONFIG_FILE" ]]; then
  echo "No exported git config file found at $CONFIG_FILE; skipping apply."
  exit 0
fi

while IFS='=' read -r key value; do
  [[ -z "$key" ]] && continue
  [[ "$key" =~ ^# ]] && continue
  if [[ "$value" =~ ^base64: ]]; then
    encoded="${value#base64:}"
    decoded=$(printf '%s' "$encoded" | base64 --decode)
  else
    decoded="$value"
  fi
  git config --global --replace-all "$key" "$decoded"
done < "$CONFIG_FILE"

echo "Applied exported git config to global scope."
