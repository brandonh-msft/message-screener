#!/usr/bin/env bash
set -euo pipefail

config_path=".devcontainer/devcontainer.json"
errors=0
warnings=0

fail() {
  echo "[FAIL] $1" >&2
  errors=$((errors + 1))
}

pass() {
  echo "[PASS] $1"
}

warn() {
  echo "[WARN] $1"
  warnings=$((warnings + 1))
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

if pwsh -NoProfile -Command "\$cfg = Get-Content -Raw '$config_path' | ConvertFrom-Json -AsHashtable; if (\$cfg.ContainsKey('image') -or \$cfg.ContainsKey('build')) { exit 0 } else { exit 1 }" >/dev/null 2>&1; then
  pass "devcontainer has image or build definition"
else
  fail "Missing both image and build definitions"
fi

if pwsh -NoProfile -Command "\$cfg = Get-Content -Raw '$config_path' | ConvertFrom-Json -AsHashtable; if (\$cfg.ContainsKey('image') -and -not [string]::IsNullOrWhiteSpace([string]\$cfg.image)) { Write-Output \$cfg.image }" >/tmp/devcontainer-image.txt 2>/dev/null; then
  image_ref=$(cat /tmp/devcontainer-image.txt 2>/dev/null || true)
  if [[ -n "${image_ref:-}" ]]; then
    if command -v docker >/dev/null 2>&1; then
      if docker manifest inspect "$image_ref" >/dev/null 2>&1; then
        pass "image reference is resolvable: $image_ref"
      else
        fail "image reference is not resolvable: $image_ref"
      fi
    elif command -v podman >/dev/null 2>&1; then
      if podman manifest inspect "$image_ref" >/dev/null 2>&1; then
        pass "image reference is resolvable: $image_ref"
      else
        fail "image reference is not resolvable: $image_ref"
      fi
    else
      warn "Skipping image resolvability check (docker/podman not found)"
    fi
  fi
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

if pwsh -NoProfile -Command "\$cfg = Get-Content -Raw '$config_path' | ConvertFrom-Json -AsHashtable; if (\$cfg.ContainsKey('postCreateCommand')) { exit 0 } else { exit 1 }" >/dev/null 2>&1; then
  pass "postCreateCommand exists"
else
  fail "Missing postCreateCommand"
fi

if pwsh -NoProfile -Command "\$cfg = Get-Content -Raw '$config_path' | ConvertFrom-Json -AsHashtable; \$cmd = [string]\$cfg.postCreateCommand; if (\$cmd -match 'bootstrap-copilot-digital-twin.sh|ensure-copilot-cli.sh') { exit 0 } else { exit 1 }" >/dev/null 2>&1; then
  pass "postCreateCommand includes Copilot bootstrap"
else
  fail "postCreateCommand missing Copilot bootstrap"
fi

if pwsh -NoProfile -Command "\$cfg = Get-Content -Raw '$config_path' | ConvertFrom-Json -AsHashtable; \$cmd = [string]\$cfg.postCreateCommand; if (\$cmd -match 'bootstrap-copilot-digital-twin.sh|ensure-shell-aliases.sh') { exit 0 } else { exit 1 }" >/dev/null 2>&1; then
  pass "postCreateCommand includes shell alias bootstrap"
else
  fail "postCreateCommand missing shell alias bootstrap"
fi

if pwsh -NoProfile -Command "\$cfg = Get-Content -Raw '$config_path' | ConvertFrom-Json -AsHashtable; \$cmd = [string]\$cfg.postStartCommand; if (\$cmd -match 'apply-global-git-config.sh') { exit 0 } else { exit 1 }" >/dev/null 2>&1; then
  pass "postStartCommand applies global git config"
else
  fail "postStartCommand missing git config apply"
fi

if pwsh -NoProfile -Command "\$cfg = Get-Content -Raw '$config_path' | ConvertFrom-Json -AsHashtable; if (\$cfg.containerEnv.POWERSHELL_DISTRIBUTION_CHANNEL -eq 'DevContainers') { exit 0 } else { exit 1 }" >/dev/null 2>&1; then
  pass "POWERSHELL_DISTRIBUTION_CHANNEL configured"
else
  fail "Missing POWERSHELL_DISTRIBUTION_CHANNEL"
fi

if pwsh -NoProfile -Command "\$cfg = Get-Content -Raw '$config_path' | ConvertFrom-Json -AsHashtable; \$settings = \$cfg.customizations.vscode.settings; if (\$settings.'terminal.integrated.defaultProfile.linux' -eq 'pwsh') { exit 0 } else { exit 1 }" >/dev/null 2>&1; then
  pass "VS Code terminal default profile is pwsh"
else
  fail "terminal.integrated.defaultProfile.linux is not pwsh"
fi

if pwsh -NoProfile -Command "\$cfg = Get-Content -Raw '$config_path' | ConvertFrom-Json -AsHashtable; \$mounts = @(); if (\$cfg.ContainsKey('mounts') -and \$null -ne \$cfg.mounts) { \$mounts = @([string[]]\$cfg.mounts) }; if (\$mounts.Count -gt 0) { exit 0 } else { exit 1 }" >/dev/null 2>&1; then
  pass "mounts section exists"
else
  fail "Missing mounts section"
fi

if pwsh -NoProfile -Command "\$cfg = Get-Content -Raw '$config_path' | ConvertFrom-Json -AsHashtable; \$mounts = @(); if (\$cfg.ContainsKey('mounts') -and \$null -ne \$cfg.mounts) { \$mounts = @([string[]]\$cfg.mounts) }; if (\$mounts -match 'gitconfig.effective') { exit 0 } else { exit 1 }" >/dev/null 2>&1; then
  pass "gitconfig.effective mount present"
else
  fail "Missing gitconfig.effective mount"
fi

if pwsh -NoProfile -Command "\$cfg = Get-Content -Raw '$config_path' | ConvertFrom-Json -AsHashtable; \$mounts = @(); if (\$cfg.ContainsKey('mounts') -and \$null -ne \$cfg.mounts) { \$mounts = @([string[]]\$cfg.mounts) }; if (\$mounts -match '/opt/host-caches/') { exit 0 } else { exit 1 }" >/dev/null 2>&1; then
  pass "cache mounts present"
else
  warn "Cache mounts are not configured on this host (set cache env vars, then rerun generate-cache-mount-config.sh and merge-devcontainer-config.sh)"
fi

required_cache_envs=(
  CARGO_HOME
  COPILOT_HOME
  COPILOT_CACHE_HOME
  npm_config_cache
  NUGET_PACKAGES
  PIP_CACHE_DIR
  VCPKG_DEFAULT_BINARY_CACHE
  XDG_DATA_HOME
)

for env_name in "${required_cache_envs[@]}"; do
  if pwsh -NoProfile -Command "\$cfg = Get-Content -Raw '$config_path' | ConvertFrom-Json -AsHashtable; if (\$cfg.containerEnv.ContainsKey('$env_name')) { exit 0 } else { exit 1 }" >/dev/null 2>&1; then
    pass "containerEnv includes $env_name"
  else
    warn "containerEnv missing $env_name (set host env var and regenerate cache mounts)"
  fi
done

if [[ ! -f ".devcontainer/scripts/bootstrap-copilot-digital-twin.sh" ]]; then fail "Missing bootstrap-copilot-digital-twin.sh"; else pass "Bootstrap script exists"; fi
if [[ ! -f ".devcontainer/scripts/ensure-shell-aliases.sh" ]]; then fail "Missing ensure-shell-aliases.sh"; else pass "Alias script exists"; fi
if [[ ! -f ".devcontainer/scripts/apply-global-git-config.sh" ]]; then fail "Missing apply-global-git-config.sh"; else pass "Git apply script exists"; fi
if [[ ! -f ".devcontainer/scripts/export-effective-git-config.sh" ]]; then fail "Missing export-effective-git-config.sh"; else pass "Git export script exists"; fi
if [[ ! -f ".devcontainer/scripts/generate-cache-mount-config.sh" ]]; then fail "Missing generate-cache-mount-config.sh"; else pass "Cache generation script exists"; fi
if [[ ! -f ".devcontainer/scripts/merge-devcontainer-config.sh" ]]; then fail "Missing merge-devcontainer-config.sh"; else pass "Merge script exists"; fi
if [[ ! -f ".devcontainer/scripts/ensure-copilot-cli.sh" ]]; then fail "Missing ensure-copilot-cli.sh"; else pass "Copilot bootstrap script exists"; fi
if [[ ! -f ".devcontainer/scripts/ensure-workiq-plugin.sh" ]]; then fail "Missing ensure-workiq-plugin.sh"; else pass "WorkIQ plugin bootstrap script exists"; fi
if [[ ! -f ".devcontainer/scripts/ensure-digital-twin-plugin.sh" ]]; then fail "Missing ensure-digital-twin-plugin.sh"; else pass "Digital Twin plugin script exists"; fi

if [[ ! -f ".devcontainer/gitconfig.effective" ]]; then
  fail "Missing .devcontainer/gitconfig.effective (run export-effective-git-config.sh on host)"
else
  pass "gitconfig.effective exists"
fi

if [[ $errors -gt 0 ]]; then
  echo "Validation failed with $errors issue(s)." >&2
  exit 1
fi

if [[ $warnings -gt 0 ]]; then
  echo "Validation passed with $warnings warning(s)."
else
  echo "Validation passed."
fi
