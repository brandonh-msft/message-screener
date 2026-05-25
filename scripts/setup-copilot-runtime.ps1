param(
    [switch]$Force,
    [string]$CopilotAgent = "message-screener-researcher",
    [string]$McpConfigPath = ".mcp.json",
    [string]$RuntimeMcpConfigPath = "src/MessageScreener.Api/config/.mcp.json",
    [string]$RuntimeGitHubTokenEnvVar = "MESSAGE_SCREENER_GITHUB_TOKEN",
    [string[]]$SkillDirectories = @("src/MessageScreener.Api/config/copilot-runtime/skills"),
    [string]$SystemPromptPath = "src/MessageScreener.Api/config/copilot-reply.system.prompt.md",
    [string]$OutputSettingsPath = "src/MessageScreener.Api/config/copilot.runtime.settings.sample.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

$resolvedMcpConfigPath = if ([System.IO.Path]::IsPathRooted($McpConfigPath)) {
    $McpConfigPath
}
else {
    Join-Path $repoRoot $McpConfigPath
}

$resolvedRuntimeMcpConfigPath = if ([System.IO.Path]::IsPathRooted($RuntimeMcpConfigPath)) {
    $RuntimeMcpConfigPath
}
else {
    Join-Path $repoRoot $RuntimeMcpConfigPath
}

$resolvedOutputSettingsPath = if ([System.IO.Path]::IsPathRooted($OutputSettingsPath)) {
    $OutputSettingsPath
}
else {
    Join-Path $repoRoot $OutputSettingsPath
}

$resolvedSystemPromptPath = if ([System.IO.Path]::IsPathRooted($SystemPromptPath)) {
    $SystemPromptPath
}
else {
    Join-Path $repoRoot $SystemPromptPath
}

$resolvedSkillDirectories = @()
foreach ($skillDirectory in $SkillDirectories) {
    $resolvedPath = if ([System.IO.Path]::IsPathRooted($skillDirectory)) {
        $skillDirectory
    }
    else {
        Join-Path $repoRoot $skillDirectory
    }

    if (-not (Test-Path $resolvedPath)) {
        New-Item -Path $resolvedPath -ItemType Directory -Force | Out-Null
    }

    $resolvedSkillDirectories += $resolvedPath
}

if (-not (Test-Path $resolvedSystemPromptPath)) {
    throw "System prompt path does not exist: $resolvedSystemPromptPath"
}

if (-not (Test-Path $resolvedMcpConfigPath)) {
    throw "MCP config file does not exist: $resolvedMcpConfigPath"
}

if (-not $Force -and (Test-Path $resolvedRuntimeMcpConfigPath)) {
    Write-Host "Runtime MCP config already exists at $resolvedRuntimeMcpConfigPath"
}
else {
    $runtimeMcpDirectory = Split-Path -Parent $resolvedRuntimeMcpConfigPath
    if (-not (Test-Path $runtimeMcpDirectory)) {
        New-Item -Path $runtimeMcpDirectory -ItemType Directory -Force | Out-Null
    }

    Copy-Item -Path $resolvedMcpConfigPath -Destination $resolvedRuntimeMcpConfigPath -Force

    # Keep runtime MCP auth env distinct from developer seed config.
    $runtimeMcpConfig = Get-Content -Path $resolvedRuntimeMcpConfigPath -Raw | ConvertFrom-Json -AsHashtable
    if ($runtimeMcpConfig.ContainsKey("servers") -and
        $runtimeMcpConfig.servers.ContainsKey("github") -and
        $runtimeMcpConfig.servers.github.ContainsKey("env")) {
        $runtimeMcpConfig.servers.github.env["GITHUB_TOKEN"] = '${env:' + $RuntimeGitHubTokenEnvVar + '}'
        $runtimeMcpConfig | ConvertTo-Json -Depth 30 | Set-Content -Path $resolvedRuntimeMcpConfigPath -Encoding utf8
    }
}

if (-not $Force -and (Test-Path $resolvedOutputSettingsPath)) {
    Write-Host "Copilot runtime settings sample already exists at $resolvedOutputSettingsPath"
    Write-Host "Use -Force to overwrite it."
    return
}

$outputDirectory = Split-Path -Parent $resolvedOutputSettingsPath
if (-not (Test-Path $outputDirectory)) {
    New-Item -Path $outputDirectory -ItemType Directory -Force | Out-Null
}

function Normalize-RelativePath {
    param(
        [string]$Path
    )

    return ($Path -replace '\\', '/')
}

function To-RuntimeRelativePath {
    param(
        [string]$Path
    )

    $normalized = Normalize-RelativePath $Path
    if ($normalized.StartsWith("src/MessageScreener.Api/", [StringComparison]::OrdinalIgnoreCase)) {
        return $normalized.Substring("src/MessageScreener.Api/".Length)
    }

    return $normalized.TrimStart('/')
}

$runtimeSettings = [ordered]@{
    MessageScreener = [ordered]@{
        Copilot = [ordered]@{
            GitHubToken = ""
            Model = "gpt-4.1"
            Agent = $CopilotAgent
            ConfigDirectory = "config"
            EnableConfigDiscovery = $true
            SystemPromptPath = To-RuntimeRelativePath (Normalize-RelativePath (Resolve-Path -Relative $resolvedSystemPromptPath)).TrimStart('.', '/')
            SkillDirectories = @()
            MessageMode = "interactive"
            ResponseTimeoutSeconds = 45
        }
    }
}

foreach ($resolvedSkillDirectory in $resolvedSkillDirectories) {
    $runtimeSettings.MessageScreener.Copilot.SkillDirectories += To-RuntimeRelativePath (Normalize-RelativePath (Resolve-Path -Relative $resolvedSkillDirectory)).TrimStart('.', '/')
}

$runtimeSettingsJson = $runtimeSettings | ConvertTo-Json -Depth 10
Set-Content -Path $resolvedOutputSettingsPath -Value $runtimeSettingsJson -Encoding utf8

Write-Host "Copilot runtime hook ready."
Write-Host "MCP config: $resolvedMcpConfigPath"
Write-Host "Runtime MCP config: $resolvedRuntimeMcpConfigPath"
Write-Host "Skill directories:"
foreach ($resolvedSkillDirectory in $resolvedSkillDirectories) {
    Write-Host "- $resolvedSkillDirectory"
}
Write-Host "Settings sample written: $resolvedOutputSettingsPath"
Write-Host "Next steps:"
Write-Host "1) Set MESSAGE_SCREENER_GITHUB_TOKEN in azd environment."
Write-Host "2) Optionally copy values from copilot.runtime.settings.sample.json into appsettings or env vars."
Write-Host "3) Add custom MCP servers in .mcp.json, then re-run setup to refresh src/MessageScreener.Api/config/.mcp.json."
