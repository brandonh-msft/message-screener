<#
.SYNOPSIS
Registers an M365 OAuth2 app in Entra ID for WorkIQ MCP integration.

.DESCRIPTION
Automated M365 app registration for Message Screener, run as part of azd preprovision.
Handles app creation and secret generation with SFI defaults.

.PARAMETER SkipIfExists
If true, skips registration if app already exists. Default: $true (safe for re-runs).

.PARAMETER Force
Force re-register even if app exists. Creates new secret in Key Vault.

.EXAMPLE
.\azd-configure-m365-app.ps1

.NOTES
Requires:
- Azure CLI (az)
- User signed in with sufficient permissions (app registration admin)
- Azure Key Vault is provisioned by infra
- azd env variable persistence for M365 values
#>

[CmdletBinding()]
param(
    [switch]$SkipIfExists = $true,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Configuration
$appDisplayName = "Message Screener WorkIQ"
$requiredScopes = @(
    "https://graph.microsoft.com/Mail.Read"
)

function Write-Status {
    param([string]$Message, [ValidateSet("Info", "Success", "Warn", "Error")][string]$Level = "Info")
    $colors = @{
        Info    = "Cyan"
        Success = "Green"
        Warn    = "Yellow"
        Error   = "Red"
    }
    Write-Host "[$Level] $Message" -ForegroundColor $colors[$Level]
}

function Get-ExistingApp {
    Write-Status "Checking for existing app registration: $appDisplayName"
    
    $app = az ad app list --filter "displayName eq '$appDisplayName'" --query "[0]" 2>$null | ConvertFrom-Json -ErrorAction SilentlyContinue
    
    if ($app -and $app.appId) {
        Write-Status "Found existing app: $($app.appId)" -Level "Warn"
        return $app
    }
    
    Write-Status "No existing app found."
    return $null
}

function New-M365App {
    param([string]$ReplyUrl)
    
    Write-Status "Creating M365 app registration: $appDisplayName"

    $requiredResourceAccessFile = Join-Path $env:TEMP ("message-screener-required-resource-accesses-{0}.json" -f [guid]::NewGuid().ToString("N"))
    @"
[
  {
    "resourceAppId": "00000003-0000-0000-c000-000000000000",
    "resourceAccess": [
      {
        "id": "810c84f8-796c-4033-b4c3-c6f6f52c0fda",
        "type": "Scope"
      }
    ]
  }
]
"@ | Set-Content -Path $requiredResourceAccessFile -Encoding utf8

    try {
        # Create app with web platform and required scopes
        $app = az ad app create `
            --display-name $appDisplayName `
            --web-redirect-uris $ReplyUrl `
            --required-resource-accesses "@$requiredResourceAccessFile" 2>&1 | ConvertFrom-Json -ErrorAction Stop
    }
    finally {
        Remove-Item -Path $requiredResourceAccessFile -Force -ErrorAction SilentlyContinue
    }
    
    if (-not $app -or -not $app.appId) {
        throw "Failed to create app registration."
    }
    
    Write-Status "App created successfully: $($app.appId)" -Level "Success"
    return $app
}

function Update-M365AppConfig {
    param(
        [string]$AppId,
        [string]$ReplyUrl
    )

    Write-Status "Updating app redirect URI and delegated scopes (Mail.Read no-admin profile)."

    $requiredResourceAccessFile = Join-Path $env:TEMP ("message-screener-required-resource-accesses-{0}.json" -f [guid]::NewGuid().ToString("N"))
    @"
[
  {
    "resourceAppId": "00000003-0000-0000-c000-000000000000",
    "resourceAccess": [
      {
        "id": "810c84f8-796c-4033-b4c3-c6f6f52c0fda",
        "type": "Scope"
      }
    ]
  }
]
"@ | Set-Content -Path $requiredResourceAccessFile -Encoding utf8

    try {
        $updateResult = & az ad app update `
            --id $AppId `
            --web-redirect-uris $ReplyUrl `
            --required-resource-accesses "@$requiredResourceAccessFile" `
            --only-show-errors 2>&1
    }
    finally {
        Remove-Item -Path $requiredResourceAccessFile -Force -ErrorAction SilentlyContinue
    }

    if ($LASTEXITCODE -ne 0) {
        $errorText = ($updateResult | Out-String).Trim()
        throw "Failed to update app registration. Azure CLI returned: $errorText"
    }
}

function New-ClientSecret {
    param(
        [string]$AppId,
        [int]$ExpiryDays = 365
    )
    
    Write-Status "Creating client secret for app: $AppId"

    $fallbackDays = @(180, 90, 30)
    $expiryAttempts = @($ExpiryDays) + $fallbackDays | Where-Object { $_ -gt 0 } | Select-Object -Unique
    $lastErrorText = $null

    foreach ($attemptDays in $expiryAttempts) {
        $endDate = (Get-Date).ToUniversalTime().AddDays($attemptDays).ToString("yyyy-MM-ddTHH:mm:ssZ")
        Write-Status "Trying client secret expiry of $attemptDays day(s) (end date: $endDate)"

        $secretPassword = & az ad app credential reset `
            --id $AppId `
            --display-name "Message Screener WorkIQ Auth" `
            --end-date $endDate `
            --query password `
            --output tsv `
            --only-show-errors 2>&1

        if ($LASTEXITCODE -eq 0) {
            $secretPasswordText = ($secretPassword | Out-String).Trim()
            if ([string]::IsNullOrWhiteSpace($secretPasswordText)) {
                throw "Failed to create client secret. Azure CLI returned an empty password."
            }

            Write-Status "Client secret created with $attemptDays day(s) expiry." -Level "Success"
            return [pscustomobject]@{ password = $secretPasswordText }
        }

        $lastErrorText = ($secretPassword | Out-String).Trim()
        if ($lastErrorText -match "Credential lifetime exceeds the max value allowed as per assigned policy") {
            Write-Status "Tenant policy rejected $attemptDays day(s) expiry. Retrying with a shorter lifetime." -Level "Warn"
            continue
        }

        throw "Failed to create client secret. Azure CLI returned: $lastErrorText"
    }

    throw "Failed to create client secret. Tried expiry values ($($expiryAttempts -join ', ') days), but tenant policy rejected all attempts. Last Azure CLI error: $lastErrorText"
}

function Update-AzdEnvironment {
    param(
        [string]$ClientId,
        [string]$ClientSecret,
        [string]$TenantId
    )
    
    Write-Status "Updating azd environment variables"

    azd env set MESSAGE_SCREENER_M365_CLIENT_ID $ClientId 2>&1 | Out-Null
    azd env set MESSAGE_SCREENER_M365_CLIENT_SECRET $ClientSecret 2>&1 | Out-Null
    azd env set MESSAGE_SCREENER_M365_TENANT_ID $TenantId 2>&1 | Out-Null

    # Greenfield cleanup: clear previously used non-idiomatic aliases.
    azd env set m365ClientId "" 2>&1 | Out-Null
    azd env set m365ClientSecret "" 2>&1 | Out-Null
    azd env set m365TenantId "" 2>&1 | Out-Null
    
    Write-Status "Environment variables updated." -Level "Success"
}

function Revoke-OldSecrets {
    param([string]$AppId)
    
    Write-Status "Revoking old client secrets for clean slate"
    
    # Get all credentials and revoke them
    $creds = az ad app credential list --id $AppId 2>&1 | ConvertFrom-Json -ErrorAction SilentlyContinue
    
    if ($creds -and $creds.Count -gt 0) {
        foreach ($cred in $creds) {
            if ($cred.keyId) {
                az ad app credential delete --id $AppId --key-id $cred.keyId 2>&1 | Out-Null
                Write-Status "Revoked credential: $($cred.keyId)"
            }
        }
    }
}

# Main flow
try {
    Write-Status "=== Message Screener M365 App Registration ==="
    
    $tenantId = (az account show --query tenantId -o tsv 2>$null).Trim()
    if ([string]::IsNullOrWhiteSpace($tenantId)) {
        throw "Unable to determine tenant ID. Ensure 'az account show' works."
    }
    Write-Status "Tenant ID: $tenantId"
    
    # Determine reply URL from azd environment.
    # Authorization code + PKCE requires an exact callback URI match in app registration.
    $apiBase = $env:MESSAGE_SCREENER_PUBLIC_BASE_URL
    if ([string]::IsNullOrWhiteSpace($apiBase)) {
        $apiBase = (& azd env get-value MESSAGE_SCREENER_PUBLIC_BASE_URL 2>$null | Out-String).Trim()
    }
    if ([string]::IsNullOrWhiteSpace($apiBase)) {
        $apiBase = (& azd env get-value SERVICE_API_URI 2>$null | Out-String).Trim()
    }
    if ([string]::IsNullOrWhiteSpace($apiBase)) {
        $apiBase = (& azd env get-value MESSAGE_SCREENER_API_ENDPOINT 2>$null | Out-String).Trim()
    }
    if ([string]::IsNullOrWhiteSpace($apiBase)) {
        throw "Unable to resolve API base URL for redirect URI. Set one of: MESSAGE_SCREENER_PUBLIC_BASE_URL, SERVICE_API_URI, or MESSAGE_SCREENER_API_ENDPOINT."
    }
    $replyUrl = "$apiBase/api/authm365/callback"
    Write-Status "Reply URL: $replyUrl"
    
    # Check for existing app
    $existingApp = Get-ExistingApp
    
    $app = $null
    if ($existingApp) {
        if ($Force) {
            Write-Status "Force flag enabled, re-registering app." -Level "Warn"
            # Revoke old secrets and create new one
            Revoke-OldSecrets -AppId $existingApp.appId
            $app = $existingApp
        }
        elseif ($SkipIfExists) {
            Write-Status "App exists and SkipIfExists=true, using existing app." -Level "Warn"
            $app = $existingApp
        }
        else {
            throw "App already exists. Use -Force to re-register or -SkipIfExists to reuse."
        }
    }
    else {
        $app = New-M365App -ReplyUrl $replyUrl
    }

    # Always enforce latest redirect URI and delegated scope set, even for existing apps.
    Update-M365AppConfig -AppId $app.appId -ReplyUrl $replyUrl
    
    # Create/update client secret
    $secret = New-ClientSecret -AppId $app.appId
    
    # Update azd environment
    Update-AzdEnvironment -ClientId $app.appId -ClientSecret $secret.password -TenantId $tenantId
    
    Write-Status "=== M365 App Registration Complete ===" -Level "Success"
    Write-Host ""
    Write-Host "Next steps:"
    Write-Host "1. Verify app in Entra ID: https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationsListBlade"
    Write-Host "2. Set M365Auth.Enabled=true in appsettings (or via configuration)"
    Write-Host "3. Owner can authenticate at: $apiBase/api/authm365/start"
    Write-Host ""
}
catch {
    Write-Status "Error: $_" -Level "Error"
    exit 1
}
