#!/usr/bin/env bash
set -euo pipefail

allow_missing=false
output_path=""

for arg in "$@"; do
  case "$arg" in
    --allow-missing)
      allow_missing=true
      ;;
    *)
      output_path="$arg"
      ;;
  esac
done

if [[ -z "$output_path" ]]; then
  echo "Usage: $0 [--allow-missing] <output-json-path>" >&2
  exit 1
fi

required_vars=(
  CARGO_HOME
  COPILOT_HOME
  COPILOT_CACHE_HOME
  npm_config_cache
  NUGET_PACKAGES
  PIP_CACHE_DIR
  VCPKG_DEFAULT_BINARY_CACHE
  XDG_DATA_HOME
)

resolve_env_var() {
  local var_name="$1"

  # First prefer the current process environment if available.
  if [[ -n "${!var_name:-}" ]]; then
    printf '%s' "${!var_name}"
    return
  fi

  # On Windows hosts, bash-launched scripts can miss variables that are visible
  # in PowerShell user/machine scopes. Fall back to pwsh when available.
  if command -v pwsh >/dev/null 2>&1; then
    local resolved
    resolved=$(pwsh -NoProfile -Command "\
\$name = '$var_name'; \
\$value = [Environment]::GetEnvironmentVariable(\$name, 'Process'); \
if ([string]::IsNullOrWhiteSpace(\$value)) { \
  \$value = [Environment]::GetEnvironmentVariable(\$name, 'User'); \
} \
if ([string]::IsNullOrWhiteSpace(\$value)) { \
  \$value = [Environment]::GetEnvironmentVariable(\$name, 'Machine'); \
} \
if (-not [string]::IsNullOrWhiteSpace(\$value)) { \
  Write-Output \$value; \
}" 2>/dev/null | tr -d '\r')

    if [[ -n "${resolved:-}" ]]; then
      printf '%s' "$resolved"
      return
    fi
  fi

  printf ''
}

missing=()
for var_name in "${required_vars[@]}"; do
  if [[ -z "$(resolve_env_var "$var_name")" ]]; then
    missing+=("$var_name")
  fi
done

if [[ ${#missing[@]} -gt 0 && "$allow_missing" != true ]]; then
  echo "Missing required environment variables: ${missing[*]}" >&2
  echo "Use --allow-missing to generate a partial config." >&2
  exit 1
fi

mkdir -p "$(dirname "$output_path")"

mounts=()
container_env_entries=()

add_mount_and_env() {
  local host_var="$1"
  local env_name="$2"
  local target_path="$3"
  local resolved_value

  resolved_value="$(resolve_env_var "$host_var")"
  if [[ -z "${resolved_value:-}" ]]; then
    return
  fi

  mounts+=("source=\${localEnv:${host_var}},target=${target_path},type=bind,consistency=cached")
  container_env_entries+=("\"${env_name}\": \"${target_path}\"")
}

add_mount_and_env CARGO_HOME CARGO_HOME /opt/host-caches/cargo
add_mount_and_env COPILOT_HOME COPILOT_HOME /opt/host-caches/copilot-home
add_mount_and_env npm_config_cache npm_config_cache /opt/host-caches/npm
add_mount_and_env NUGET_PACKAGES NUGET_PACKAGES /opt/host-caches/nuget
add_mount_and_env PIP_CACHE_DIR PIP_CACHE_DIR /opt/host-caches/pip
add_mount_and_env VCPKG_DEFAULT_BINARY_CACHE VCPKG_DEFAULT_BINARY_CACHE /opt/host-caches/vcpkg
add_mount_and_env XDG_DATA_HOME XDG_DATA_HOME /opt/host-caches/xdg-data

copilot_cache_home="$(resolve_env_var "COPILOT_CACHE_HOME")"
copilot_home="$(resolve_env_var "COPILOT_HOME")"

if [[ -n "${copilot_cache_home:-}" ]]; then
  if [[ -n "${copilot_home:-}" && "${copilot_cache_home}" == "${copilot_home}"* ]]; then
    container_env_entries+=("\"COPILOT_CACHE_HOME\": \"/opt/host-caches/copilot-home/cache\"")
  else
    mounts+=("source=\${localEnv:COPILOT_CACHE_HOME},target=/opt/host-caches/copilot-cache,type=bind,consistency=cached")
    container_env_entries+=("\"COPILOT_CACHE_HOME\": \"/opt/host-caches/copilot-cache\"")
  fi
fi

{
  echo "{"
  echo "  \"mounts\": ["
  for i in "${!mounts[@]}"; do
    sep=","; [[ $i -eq $((${#mounts[@]} - 1)) ]] && sep=""
    echo "    \"${mounts[$i]}\"${sep}"
  done
  echo "  ],"
  echo "  \"containerEnv\": {"
  for i in "${!container_env_entries[@]}"; do
    sep=","; [[ $i -eq $((${#container_env_entries[@]} - 1)) ]] && sep=""
    echo "    ${container_env_entries[$i]}${sep}"
  done
  echo "  }"
  echo "}"
} > "$output_path"

echo "Generated cache mount config: $output_path"
