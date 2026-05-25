#!/usr/bin/env bash
set -euo pipefail

bash .devcontainer/scripts/ensure-copilot-cli.sh
bash .devcontainer/scripts/ensure-workiq-plugin.sh
bash .devcontainer/scripts/ensure-digital-twin-plugin.sh
bash .devcontainer/scripts/ensure-shell-aliases.sh

echo ""
echo "Post-create action: run 'gh auth login' in this container if you have not authenticated yet."
