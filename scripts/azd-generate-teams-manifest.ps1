param(
    [ValidateSet('postprovision', 'postdeploy')]
    [string]$Hook = 'postdeploy'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-FirstNonEmptyValue {
    param([string[]]$CandidateNames)

    foreach ($candidateName in $CandidateNames) {
        $candidateValue = [Environment]::GetEnvironmentVariable($candidateName)
        if (-not [string]::IsNullOrWhiteSpace($candidateValue)) {
            return $candidateValue
        }
    }

    return $null
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$envName = Get-FirstNonEmptyValue -CandidateNames @('AZURE_ENV_NAME', 'AZD_ENV_NAME')
if ([string]::IsNullOrWhiteSpace($envName)) {
    $envName = 'local'
}

$baseUrl = Get-FirstNonEmptyValue -CandidateNames @(
    'MESSAGE_SCREENER_PUBLIC_BASE_URL',
    'MESSAGE_SCREENER_API_ENDPOINT',
    'SERVICE_MESSAGE_SCREENER_API_ENDPOINT',
    'SERVICE_API_ENDPOINT'
)

$teamsAppId = Get-FirstNonEmptyValue -CandidateNames @(
    'MESSAGE_SCREENER_TEAMS_APP_ID',
    'TEAMS_APP_ID'
)

$botId = Get-FirstNonEmptyValue -CandidateNames @(
    'MESSAGE_SCREENER_TEAMS_BOT_ID',
    'TEAMS_BOT_ID',
    'AZURE_CLIENT_ID'
)

if ([string]::IsNullOrWhiteSpace($baseUrl) -or [string]::IsNullOrWhiteSpace($teamsAppId) -or [string]::IsNullOrWhiteSpace($botId)) {
    Write-Host "[$Hook] Skipping Teams manifest generation."
    Write-Host "[$Hook] Required env vars: base URL (MESSAGE_SCREENER_PUBLIC_BASE_URL), Teams app id (MESSAGE_SCREENER_TEAMS_APP_ID), bot id (MESSAGE_SCREENER_TEAMS_BOT_ID)."
    exit 0
}

$outputDirectory = Join-Path $repoRoot ".message-screener/deploy/$envName"
$generatorScript = Join-Path $PSScriptRoot 'generate-teams-manifest.ps1'

& $generatorScript `
    -OutputDirectory $outputDirectory `
    -BaseUrl $baseUrl `
    -TeamsAppId $teamsAppId `
    -BotId $botId

Write-Host "[$Hook] Teams manifest generation completed for azd environment '$envName'."
