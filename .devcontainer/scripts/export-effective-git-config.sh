#!/usr/bin/env bash
set -euo pipefail

REPO_PATH="${1:-$(pwd)}"
OUTPUT_PATH="${2:-.devcontainer/gitconfig.effective}"

mkdir -p "$(dirname "$OUTPUT_PATH")"
if [[ -d "$OUTPUT_PATH" ]]; then
  echo "ERROR: $OUTPUT_PATH is a directory; expected a file path." >&2
  echo "Remove the directory and rerun export." >&2
  exit 1
fi
: > "$OUTPUT_PATH"

while IFS= read -r key; do
  case "$key" in
    credential.*|gpg.*|user.signingkey|include.*)
      continue
      ;;
  esac

  while IFS= read -r value; do
    encoded=$(printf '%s' "$value" | base64 | tr -d '\n')
    printf '%s=%s\n' "$key" "base64:$encoded" >> "$OUTPUT_PATH"
  done < <(git -C "$REPO_PATH" config --get-all "$key" || true)
done < <(git -C "$REPO_PATH" config --list --name-only | sort -u)

echo "Exported git config to $OUTPUT_PATH"
