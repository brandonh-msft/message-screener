#!/usr/bin/env bash
set -euo pipefail

template_path=".devcontainer/devcontainer.template.jsonc"
cache_path=".devcontainer/cache-mounts.generated.json"
output_path=".devcontainer/devcontainer.json"

if [[ ! -f "$template_path" ]]; then
  echo "Missing template: $template_path" >&2
  exit 1
fi

if [[ ! -f "$cache_path" ]]; then
  echo "Missing cache config: $cache_path" >&2
  exit 1
fi

tmp_ps1="$(mktemp)"
trap 'rm -f "$tmp_ps1"' EXIT

cat > "$tmp_ps1" <<'PS1'
param(
  [Parameter(Mandatory = $true)][string]$TemplatePath,
  [Parameter(Mandatory = $true)][string]$CachePath,
  [Parameter(Mandatory = $true)][string]$OutputPath
)

$template = Get-Content -Path $TemplatePath -Raw | ConvertFrom-Json -AsHashtable
$cache = Get-Content -Path $CachePath -Raw | ConvertFrom-Json -AsHashtable

if (-not $template.ContainsKey('mounts') -or $null -eq $template.mounts) {
  $template.mounts = @()
}
if (-not $cache.ContainsKey('mounts') -or $null -eq $cache.mounts) {
  $cache.mounts = @()
}

$template.mounts = @(
  $template.mounts + $cache.mounts |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -Unique
)

if (-not $template.ContainsKey('containerEnv') -or $null -eq $template.containerEnv) {
  $template.containerEnv = @{}
}
if ($cache.ContainsKey('containerEnv') -and $null -ne $cache.containerEnv) {
  foreach ($entry in $cache.containerEnv.GetEnumerator()) {
    $template.containerEnv[$entry.Key] = $entry.Value
  }
}

$template | ConvertTo-Json -Depth 100 | Set-Content -Path $OutputPath -Encoding utf8
Write-Host "Merged devcontainer config written to $OutputPath"
PS1

pwsh -NoProfile -File "$tmp_ps1" -TemplatePath "$template_path" -CachePath "$cache_path" -OutputPath "$output_path"
