#!/usr/bin/env bash
set -euo pipefail

bash .devcontainer/scripts/ensure-gh-copilot-cli.sh
bash .devcontainer/scripts/ensure-digital-twin-plugin.sh
