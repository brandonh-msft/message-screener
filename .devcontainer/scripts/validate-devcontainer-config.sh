#!/usr/bin/env bash
set -euo pipefail

config_path=".devcontainer/devcontainer.json"
errors=0

fail() {
  echo "[FAIL] $1" >&2
  errors=$((errors + 1))
}

pass() {
  echo "[PASS] $1"
}

if [[ ! -f "$config_path" ]]; then
  fail "Missing $config_path"
  exit 1
fi

if ! pwsh -NoProfile -Command "Get-Content -Raw '$config_path' | ConvertFrom-Json | Out-Null" >/dev/null 2>&1; then
  fail "devcontainer.json is not valid JSON"
else
  pass "devcontainer.json is valid JSON"
fi

has_feature() {
  local feature_key="$1"
  pwsh -NoProfile -Command "\$cfg = Get-Content -Raw '$config_path' | ConvertFrom-Json -AsHashtable; if (\$cfg.features.ContainsKey('$feature_key')) { exit 0 } else { exit 1 }" >/dev/null 2>&1
}

if has_feature "ghcr.io/devcontainers/features/github-cli:1"; then
  pass "GitHub CLI feature present"
else
  fail "Missing GitHub CLI feature"
fi

if has_feature "ghcr.io/devcontainers/features/powershell:1"; then
  pass "PowerShell feature present"
else
  fail "Missing PowerShell feature"
fi

if pwsh -NoProfile -Command "\$cfg = Get-Content -Raw '$config_path' | ConvertFrom-Json -AsHashtable; \$cmd = [string]\$cfg.postCreateCommand; if (\$cmd -match 'bootstrap-ghcp-digital-twin.sh') { exit 0 } else { exit 1 }" >/dev/null 2>&1; then
  pass "postCreateCommand includes Digital Twin bootstrap"
else
  fail "postCreateCommand missing bootstrap-ghcp-digital-twin.sh"
fi

if pwsh -NoProfile -Command "\$cfg = Get-Content -Raw '$config_path' | ConvertFrom-Json -AsHashtable; if (\$cfg.containerEnv.POWERSHELL_DISTRIBUTION_CHANNEL -eq 'DevContainers') { exit 0 } else { exit 1 }" >/dev/null 2>&1; then
  pass "POWERSHELL_DISTRIBUTION_CHANNEL configured"
else
  fail "Missing POWERSHELL_DISTRIBUTION_CHANNEL"
fi

if [[ ! -f ".devcontainer/scripts/bootstrap-ghcp-digital-twin.sh" ]]; then fail "Missing bootstrap-ghcp-digital-twin.sh"; else pass "Bootstrap script exists"; fi
if [[ ! -f ".devcontainer/scripts/ensure-shell-aliases.sh" ]]; then fail "Missing ensure-shell-aliases.sh"; else pass "Alias script exists"; fi
if [[ ! -f ".devcontainer/scripts/apply-global-git-config.sh" ]]; then fail "Missing apply-global-git-config.sh"; else pass "Git apply script exists"; fi
if [[ ! -f ".devcontainer/scripts/generate-cache-mount-config.sh" ]]; then fail "Missing generate-cache-mount-config.sh"; else pass "Cache generation script exists"; fi
if [[ ! -f ".devcontainer/scripts/merge-devcontainer-config.sh" ]]; then fail "Missing merge-devcontainer-config.sh"; else pass "Merge script exists"; fi

if [[ ! -f ".devcontainer/gitconfig.effective" ]]; then
  fail "Missing .devcontainer/gitconfig.effective (run export-effective-git-config.sh on host)"
else
  pass "gitconfig.effective exists"
fi

if [[ $errors -gt 0 ]]; then
  echo "Validation failed with $errors issue(s)." >&2
  exit 1
fi

echo "Validation passed."
