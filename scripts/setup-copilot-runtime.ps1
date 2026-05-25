param(
    [switch]$Force,
    [string]$CopilotAgent = "message-screener-researcher",
    [string]$McpConfigPath = ".mcp.json",
    [string[]]$SkillDirectories = @(".github/skills", "src/MessageScreener.Api/config/skills"),
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

if (-not $Force -and (Test-Path $resolvedOutputSettingsPath)) {
    Write-Host "Copilot runtime settings sample already exists at $resolvedOutputSettingsPath"
    Write-Host "Use -Force to overwrite it."
    return
}

$outputDirectory = Split-Path -Parent $resolvedOutputSettingsPath
if (-not (Test-Path $outputDirectory)) {
    New-Item -Path $outputDirectory -ItemType Directory -Force | Out-Null
}

$runtimeSettings = [ordered]@{
    MessageScreener = [ordered]@{
        Copilot = [ordered]@{
            GitHubToken = ""
            Model = "gpt-4.1"
            Agent = $CopilotAgent
            ConfigDirectory = "."
            EnableConfigDiscovery = $true
            SystemPromptPath = (Resolve-Path -Relative $resolvedSystemPromptPath).TrimStart('.','\\','/')
            SkillDirectories = @()
            MessageMode = "interactive"
            ResponseTimeoutSeconds = 45
        }
    }
}

foreach ($resolvedSkillDirectory in $resolvedSkillDirectories) {
    $runtimeSettings.MessageScreener.Copilot.SkillDirectories += (Resolve-Path -Relative $resolvedSkillDirectory).TrimStart('.','\\','/')
}

$runtimeSettingsJson = $runtimeSettings | ConvertTo-Json -Depth 10
Set-Content -Path $resolvedOutputSettingsPath -Value $runtimeSettingsJson -Encoding utf8

Write-Host "Copilot runtime hook ready."
Write-Host "MCP config: $resolvedMcpConfigPath"
Write-Host "Skill directories:"
foreach ($resolvedSkillDirectory in $resolvedSkillDirectories) {
    Write-Host "- $resolvedSkillDirectory"
}
Write-Host "Settings sample written: $resolvedOutputSettingsPath"
Write-Host "Next steps:"
Write-Host "1) Set MESSAGE_SCREENER_GITHUB_TOKEN in azd environment."
Write-Host "2) Optionally copy values from copilot.runtime.settings.sample.json into appsettings or env vars."
Write-Host "3) Add custom MCP servers in .mcp.json and custom skills under .github/skills or src/MessageScreener.Api/config/skills."
