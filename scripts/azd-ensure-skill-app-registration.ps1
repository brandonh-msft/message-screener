param(
    [switch]$UpdateHomepageOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-AzdEnvValueMap {
    $valueMap = @{}

    $azdCommand = Get-Command azd -ErrorAction SilentlyContinue
    if ($null -eq $azdCommand) {
        return $valueMap
    }

    try {
        $lines = azd env get-values 2>$null
    }
    catch {
        return $valueMap
    }

    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line) -or -not $line.Contains('=')) {
            continue
        }

        $parts = $line.Split('=', 2)
        $name = $parts[0].Trim()
        $rawValue = $parts[1].Trim()
        if ([string]::IsNullOrWhiteSpace($name)) {
            continue
        }

        if ($rawValue.StartsWith('"') -and $rawValue.EndsWith('"') -and $rawValue.Length -ge 2) {
            $rawValue = $rawValue.Substring(1, $rawValue.Length - 2)
        }

        $valueMap[$name] = $rawValue
    }

    return $valueMap
}

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

function Get-FirstNonEmptyValueFromSources {
    param(
        [string[]]$CandidateNames,
        [hashtable]$AzdEnvValues
    )

    $directValue = Get-FirstNonEmptyValue -CandidateNames $CandidateNames
    if (-not [string]::IsNullOrWhiteSpace($directValue)) {
        return $directValue
    }

    foreach ($candidateName in $CandidateNames) {
        if ($AzdEnvValues.ContainsKey($candidateName)) {
            $candidateValue = [string]$AzdEnvValues[$candidateName]
            if (-not [string]::IsNullOrWhiteSpace($candidateValue)) {
                return $candidateValue
            }
        }
    }

    return $null
}

$azCli = Get-Command az -ErrorAction SilentlyContinue
if ($null -eq $azCli) {
    throw 'Azure CLI (az) is required to manage skill app registration.'
}

$azdCli = Get-Command azd -ErrorAction SilentlyContinue
if ($null -eq $azdCli) {
    throw 'Azure Developer CLI (azd) is required to persist environment values.'
}

$azdEnvValues = Get-AzdEnvValueMap
$envName = Get-FirstNonEmptyValueFromSources -CandidateNames @('AZURE_ENV_NAME', 'AZD_ENV_NAME') -AzdEnvValues $azdEnvValues
if ([string]::IsNullOrWhiteSpace($envName)) {
    $envName = 'local'
}

$skillAppId = Get-FirstNonEmptyValueFromSources -CandidateNames @(
    'MESSAGE_SCREENER_SKILL_APP_ID',
    'MessageScreener__Skill__AppId'
) -AzdEnvValues $azdEnvValues

if (-not $UpdateHomepageOnly -and [string]::IsNullOrWhiteSpace($skillAppId)) {
    $displayName = "message-screener-skill-$envName"
    $existingAppId = az ad app list --display-name $displayName --query '[0].appId' -o tsv 2>$null
    if ([string]::IsNullOrWhiteSpace($existingAppId)) {
        Write-Host "Creating Microsoft Entra app registration '$displayName' for Copilot Studio skill usage."
        $skillAppId = az ad app create `
            --display-name $displayName `
            --sign-in-audience AzureADMyOrg `
            --query appId `
            -o tsv
    }
    else {
        $skillAppId = $existingAppId
    }

    if ([string]::IsNullOrWhiteSpace($skillAppId)) {
        throw 'Failed to resolve skill app registration App ID.'
    }

    $existingSpId = az ad sp list --filter "appId eq '$skillAppId'" --query '[0].id' -o tsv 2>$null
    if ([string]::IsNullOrWhiteSpace($existingSpId)) {
        Write-Host "Creating service principal for skill App ID $skillAppId."
        az ad sp create --id $skillAppId --query id -o tsv | Out-Null
    }

    azd env set MESSAGE_SCREENER_SKILL_APP_ID $skillAppId | Out-Null
    azd env set MessageScreener__Skill__AppId $skillAppId | Out-Null
    Write-Host "Stored MESSAGE_SCREENER_SKILL_APP_ID in azd environment: $skillAppId"
}

$baseUrl = Get-FirstNonEmptyValueFromSources -CandidateNames @(
    'MESSAGE_SCREENER_PUBLIC_BASE_URL',
    'MESSAGE_SCREENER_API_ENDPOINT',
    'SERVICE_MESSAGE_SCREENER_API_ENDPOINT',
    'SERVICE_API_ENDPOINT'
) -AzdEnvValues $azdEnvValues

if (-not [string]::IsNullOrWhiteSpace($baseUrl)) {
    azd env set MessageScreener__Skill__PublicBaseUrl $baseUrl | Out-Null
}

if (-not [string]::IsNullOrWhiteSpace($skillAppId) -and -not [string]::IsNullOrWhiteSpace($baseUrl)) {
    $manifestUrl = "$($baseUrl.TrimEnd('/'))/manifest/message-screener-communication-twin-skill-1.0.json"
    Write-Host "Updating Entra app homepage URL to skill manifest: $manifestUrl"
    $identifierUri = "api://$skillAppId"
    az ad app update --id $skillAppId --web-home-page-url $manifestUrl --identifier-uris $identifierUri | Out-Null
    Write-Host "Ensured identifier URI for skill app registration: $identifierUri"
}
elseif ($UpdateHomepageOnly) {
    Write-Warning 'Skipped homepage update because skill app ID or public base URL was not available yet.'
}
