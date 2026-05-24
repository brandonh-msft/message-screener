#!/usr/bin/env bash
set -euo pipefail

BASH_RC="$HOME/.bashrc"
BASH_BLOCK_START="# >>> message-screener aliases >>>"
BASH_BLOCK_END="# <<< message-screener aliases <<<"

if [[ ! -f "$BASH_RC" ]]; then
  touch "$BASH_RC"
fi

sed -i "/$BASH_BLOCK_START/,/$BASH_BLOCK_END/d" "$BASH_RC"
cat >> "$BASH_RC" <<'EOF'
# >>> message-screener aliases >>>
alias g='git'
alias tf='terraform'
alias ghcp='gh copilot'
# <<< message-screener aliases <<<
EOF

PS_PROFILE_PATH=$(pwsh -NoProfile -Command '$PROFILE.CurrentUserAllHosts')
PS_PROFILE_DIR=$(dirname "$PS_PROFILE_PATH")
mkdir -p "$PS_PROFILE_DIR"
if [[ ! -f "$PS_PROFILE_PATH" ]]; then
  touch "$PS_PROFILE_PATH"
fi

pwsh -NoProfile -Command @'
param([string]$ProfilePath)
$start = "# >>> message-screener aliases >>>"
$end = "# <<< message-screener aliases <<<"
$content = Get-Content -Path $ProfilePath -Raw
$pattern = [regex]::Escape($start) + '.*?' + [regex]::Escape($end) + "\r?\n?"
$content = [regex]::Replace($content, $pattern, '', 'Singleline')
$block = @"
# >>> message-screener aliases >>>
Set-Alias -Name g -Value git
function tf { terraform @args }
function ghcp { gh copilot @args }
# <<< message-screener aliases <<<
"@
Set-Content -Path $ProfilePath -Value (($content.TrimEnd()) + "`r`n" + $block + "`r`n") -Encoding utf8
'@ -ProfilePath "$PS_PROFILE_PATH"
