param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,
    [Parameter(Mandatory = $true)]
    [string]$TeamsAppId,
    [Parameter(Mandatory = $true)]
    [string]$BotId,
    [string]$AppName = "Message Screen",
    [string]$DeveloperName = "Brandon Hurlburt",
    [string]$WebsiteUrl = "https://github.com",
    [string]$PrivacyUrl = "https://github.com/privacy",
    [string]$TermsOfUseUrl = "https://docs.github.com/site-policy/github-terms/github-terms-of-service"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$trimmedBaseUrl = $BaseUrl.TrimEnd('/')
$manifestOutputDirectory = Join-Path $OutputDirectory "teamsapp"
$manifestPath = Join-Path $manifestOutputDirectory "manifest.json"

if (-not (Test-Path $manifestOutputDirectory)) {
    New-Item -Path $manifestOutputDirectory -ItemType Directory | Out-Null
}

$manifest = [ordered]@{
    '`$schema' = 'https://developer.microsoft.com/json-schemas/teams/v1.20/MicrosoftTeams.schema.json'
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
Write-Host "Note: Add color.png and outline.png to the same teamsapp folder before packaging the app."
