Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-TeamsManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputDirectory,
        [Parameter(Mandatory = $true)]
        [string]$BaseUrl,
        [Parameter(Mandatory = $true)]
        [string]$TeamsAppId,
        [Parameter(Mandatory = $true)]
        [string]$BotId,
        [string]$AppName = 'Message Screen',
        [string]$DeveloperName = 'Brandon Hurlburt',
        [string]$WebsiteUrl = 'https://github.com',
        [string]$PrivacyUrl = 'https://github.com/privacy',
        [string]$TermsOfUseUrl = 'https://docs.github.com/site-policy/github-terms/github-terms-of-service'
    )

    $trimmedBaseUrl = $BaseUrl.TrimEnd('/')
    $manifestOutputDirectory = Join-Path $OutputDirectory 'teamsapp'
    $manifestPath = Join-Path $manifestOutputDirectory 'manifest.json'

    if (-not (Test-Path $manifestOutputDirectory)) {
        New-Item -Path $manifestOutputDirectory -ItemType Directory | Out-Null
    }

    $manifest = [ordered]@{
        '$schema' = 'https://developer.microsoft.com/json-schemas/teams/v1.20/MicrosoftTeams.schema.json'
        manifestVersion = '1.20'
        version = '1.0.0'
        id = $TeamsAppId
        developer = [ordered]@{
            name = $DeveloperName
            websiteUrl = $WebsiteUrl
            privacyUrl = $PrivacyUrl
            termsOfUseUrl = $TermsOfUseUrl
        }
        name = [ordered]@{
            short = $AppName
            full = "$AppName for Teams"
        }
        description = [ordered]@{
            short = 'Message screening assistant for Teams conversations.'
            full = 'Message Screener helps owners review and approve AI-assisted responses before sending.'
        }
        icons = [ordered]@{
            color = 'color.png'
            outline = 'outline.png'
        }
        accentColor = '#005A9C'
        bots = @(
            [ordered]@{
                botId = $BotId
                scopes = @('personal', 'groupChat', 'team')
                supportsFiles = $false
                isNotificationOnly = $false
                commandLists = @(
                    [ordered]@{
                        scopes = @('personal', 'groupChat')
                        commands = @(
                            [ordered]@{
                                title = 'help'
                                description = 'Get help with Message Screener.'
                            }
                        )
                    }
                )
            }
        )
        composeExtensions = @(
            [ordered]@{
                botId = $BotId
                canUpdateConfiguration = $false
                commands = @(
                    [ordered]@{
                        id = 'forwardToMessageScreener'
                        type = 'action'
                        title = 'Forward to Message Screener'
                        description = 'Forward this message to Message Screener for review and draft generation.'
                        context = @('message')
                        fetchTask = $true
                    }
                )
            }
        )
        validDomains = @(
            ([Uri]$trimmedBaseUrl).Host
        )
        webApplicationInfo = [ordered]@{
            id = $BotId
            resource = "api://$BotId"
        }
    }

    $manifestJson = $manifest | ConvertTo-Json -Depth 20
    Set-Content -Path $manifestPath -Value $manifestJson -Encoding utf8

    Write-Host "Generated Teams manifest at $manifestPath"
    Write-Host 'Packaging will include img/color.png and img/outline.png.'

    return $manifestPath
}

function New-TeamsAppPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputDirectory,
        [Parameter(Mandatory = $true)]
        [string]$ManifestPath,
        [Parameter(Mandatory = $true)]
        [string]$ColorIconPath,
        [Parameter(Mandatory = $true)]
        [string]$OutlineIconPath,
        [Parameter(Mandatory = $true)]
        [string]$PackageName
    )

    if (-not (Test-Path $ManifestPath)) {
        throw "Manifest not found: $ManifestPath"
    }

    if (-not (Test-Path $ColorIconPath)) {
        throw "Required Teams icon not found: $ColorIconPath"
    }

    if (-not (Test-Path $OutlineIconPath)) {
        throw "Required Teams icon not found: $OutlineIconPath"
    }

    $stagingDirectory = Join-Path $OutputDirectory 'teamsapp-package'
    if (Test-Path $stagingDirectory) {
        Remove-Item -Path $stagingDirectory -Recurse -Force
    }

    New-Item -Path $stagingDirectory -ItemType Directory | Out-Null

    Copy-Item -Path $ManifestPath -Destination (Join-Path $stagingDirectory 'manifest.json') -Force
    Copy-Item -Path $ColorIconPath -Destination (Join-Path $stagingDirectory 'color.png') -Force
    Copy-Item -Path $OutlineIconPath -Destination (Join-Path $stagingDirectory 'outline.png') -Force

    $packagePath = Join-Path $OutputDirectory $PackageName
    if (Test-Path $packagePath) {
        Remove-Item -Path $packagePath -Force
    }

    Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $packagePath -CompressionLevel Optimal

    return $packagePath
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

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$azdEnvValues = Get-AzdEnvValueMap

$envName = Get-FirstNonEmptyValueFromSources -CandidateNames @('AZURE_ENV_NAME', 'AZD_ENV_NAME') -AzdEnvValues $azdEnvValues
if ([string]::IsNullOrWhiteSpace($envName)) {
    $envName = 'local'
}

$baseUrl = Get-FirstNonEmptyValueFromSources -CandidateNames @(
    'MESSAGE_SCREENER_PUBLIC_BASE_URL',
    'MESSAGE_SCREENER_API_ENDPOINT',
    'SERVICE_MESSAGE_SCREENER_API_ENDPOINT',
    'SERVICE_API_ENDPOINT'
) -AzdEnvValues $azdEnvValues

$teamsAppId = Get-FirstNonEmptyValueFromSources -CandidateNames @(
    'MESSAGE_SCREENER_TEAMS_APP_ID',
    'TEAMS_APP_ID'
) -AzdEnvValues $azdEnvValues

$botId = Get-FirstNonEmptyValueFromSources -CandidateNames @(
    'MESSAGE_SCREENER_TEAMS_BOT_ID',
    'TEAMS_BOT_ID',
    'AZURE_CLIENT_ID'
) -AzdEnvValues $azdEnvValues

if ([string]::IsNullOrWhiteSpace($baseUrl) -or [string]::IsNullOrWhiteSpace($teamsAppId) -or [string]::IsNullOrWhiteSpace($botId)) {
    Write-Host 'Skipping Teams manifest generation.'
    Write-Host 'Required env vars: base URL (MESSAGE_SCREENER_PUBLIC_BASE_URL), Teams app id (MESSAGE_SCREENER_TEAMS_APP_ID), bot id (MESSAGE_SCREENER_TEAMS_BOT_ID).'
    exit 0
}

$outputDirectory = Join-Path $repoRoot "dist/$envName"
$manifestPath = New-TeamsManifest `
    -OutputDirectory $outputDirectory `
    -BaseUrl $baseUrl `
    -TeamsAppId $teamsAppId `
    -BotId $botId

$colorIconPath = Join-Path $repoRoot 'img/color.png'
$outlineIconPath = Join-Path $repoRoot 'img/outline.png'

$packagePath = New-TeamsAppPackage `
    -OutputDirectory $outputDirectory `
    -ManifestPath $manifestPath `
    -ColorIconPath $colorIconPath `
    -OutlineIconPath $outlineIconPath `
    -PackageName 'message-screener-teamsapp.zip'

Write-Host "Teams manifest generation completed for azd environment '$envName'."
Write-Host "Teams app package location: $packagePath"
Write-Warning "Teams app package location: $packagePath"
