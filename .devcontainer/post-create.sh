#!/usr/bin/env bash
set -euo pipefail

if ! command -v azd >/dev/null 2>&1; then
  echo "Installing Azure Developer CLI (azd)..."
  curl -fsSL https://aka.ms/install-azd.sh | bash
  export PATH="$HOME/.azd/bin:$PATH"
fi

if ! command -v azd >/dev/null 2>&1; then
  echo "ERROR: azd is required but is not available on PATH after installation." >&2
  exit 1
fi

if ! command -v copilot >/dev/null 2>&1; then
  echo "Installing GitHub Copilot CLI..."
  npm install --global @github/copilot
fi

if ! command -v copilot >/dev/null 2>&1; then
  echo "ERROR: GitHub Copilot CLI is required but is not available on PATH after installation." >&2
  exit 1
fi

echo "Toolchain versions:"
dotnet --version
az version | head -n 1 || true
gh --version | head -n 1 || true
azd version || true
copilot --version || true
pwsh --version

echo "Restoring solution dependencies..."
dotnet restore MessageScreener.slnx

echo "Post-create bootstrap complete."
echo "Next: run 'pwsh ./scripts/setup.ps1' to generate your local communication twin profile."
