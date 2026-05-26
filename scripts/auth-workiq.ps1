[CmdletBinding()]
param(
    [string]$ApiBaseUrl,
    [switch]$OpenBrowser,
    [int]$MaxWaitSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Status {
    param(
        [string]$Message,
        [ValidateSet("Info", "Success", "Warn", "Error")][string]$Level = "Info"
    )

    $colors = @{
        Info    = "Cyan"
        Success = "Green"
        Warn    = "Yellow"
        Error   = "Red"
    }

    Write-Host "[$Level] $Message" -ForegroundColor $colors[$Level]
}

function Resolve-ApiBaseUrl {
    param([string]$RequestedApiBaseUrl)

    if (-not [string]::IsNullOrWhiteSpace($RequestedApiBaseUrl)) {
        return $RequestedApiBaseUrl.TrimEnd("/")
    }

    if (-not [string]::IsNullOrWhiteSpace($env:MESSAGE_SCREENER_PUBLIC_BASE_URL)) {
        return $env:MESSAGE_SCREENER_PUBLIC_BASE_URL.TrimEnd("/")
    }

    $azd = Get-Command "azd" -ErrorAction SilentlyContinue
    if ($null -ne $azd) {
        $fromAzd = (& azd env get-value MESSAGE_SCREENER_PUBLIC_BASE_URL 2>$null | Out-String).Trim()
        if (-not [string]::IsNullOrWhiteSpace($fromAzd)) {
            return $fromAzd.TrimEnd("/")
        }
    }

    throw "API base URL not found. Provide -ApiBaseUrl, set MESSAGE_SCREENER_PUBLIC_BASE_URL, or run inside an azd environment with MESSAGE_SCREENER_PUBLIC_BASE_URL set."
}

function Read-ProblemDetail {
    param([System.Management.Automation.ErrorRecord]$ErrorRecord)

    if (-not [string]::IsNullOrWhiteSpace($ErrorRecord.ErrorDetails.Message)) {
        $errorDetailsText = $ErrorRecord.ErrorDetails.Message.Trim()
        try {
            $errorDetailsJson = $errorDetailsText | ConvertFrom-Json -ErrorAction Stop
            if ($errorDetailsJson.detail) {
                return "$($errorDetailsJson.title): $($errorDetailsJson.detail)"
            }
        }
        catch {
            return $errorDetailsText
        }

        return $errorDetailsText
    }

    if ($null -eq $ErrorRecord.Exception.Response) {
        return $ErrorRecord.Exception.Message
    }

    try {
        $stream = $ErrorRecord.Exception.Response.GetResponseStream()
        if ($null -eq $stream) {
            return $ErrorRecord.Exception.Message
        }

        $reader = New-Object System.IO.StreamReader($stream)
        $body = $reader.ReadToEnd()
        if ([string]::IsNullOrWhiteSpace($body)) {
            return $ErrorRecord.Exception.Message
        }

        try {
            $json = $body | ConvertFrom-Json -ErrorAction Stop
            if ($json.detail) {
                return "$($json.title): $($json.detail)"
            }

            return $body
        }
        catch {
            return $body
        }
    }
    catch {
        return $ErrorRecord.Exception.Message
    }
}

function Get-HttpStatusCode {
    param([System.Management.Automation.ErrorRecord]$ErrorRecord)

    if ($null -eq $ErrorRecord.Exception.Response) {
        return $null
    }

    try {
        return [int]$ErrorRecord.Exception.Response.StatusCode
    }
    catch {
        return $null
    }
}

function Resolve-AuthEndpointBase {
    param([string]$ResolvedApiBaseUrl)

    $endpointBase = "$ResolvedApiBaseUrl/api/authm365"

    try {
        $null = Invoke-RestMethod -Method Get -Uri "$endpointBase/status"
        Write-Status "Using auth endpoint base: $endpointBase"
        return $endpointBase
    }
    catch {
        $statusCode = Get-HttpStatusCode -ErrorRecord $_
        if ($statusCode -eq 404) {
            throw "Auth endpoint '$endpointBase' returned 404. Ensure the latest API is deployed with auth controller routes enabled."
        }

        $errorText = Read-ProblemDetail -ErrorRecord $_
        throw "Failed to probe auth endpoint '$endpointBase': $errorText"
    }
}

try {
    Write-Status "=== Message Screener M365 Initial Auth ==="

    $resolvedApiBaseUrl = Resolve-ApiBaseUrl -RequestedApiBaseUrl $ApiBaseUrl
    Write-Status "API base URL: $resolvedApiBaseUrl"

    $authEndpointBase = Resolve-AuthEndpointBase -ResolvedApiBaseUrl $resolvedApiBaseUrl
    $status = Invoke-RestMethod -Method Get -Uri "$authEndpointBase/status"
    if ($status.isConfigured) {
        Write-Status "M365 auth is already configured. Nothing to do." -Level "Success"
        exit 0
    }

    Write-Status "Starting authorization code + PKCE flow..."
    try {
        $start = Invoke-RestMethod -Method Post -Uri "$authEndpointBase/start"
    }
    catch {
        $statusCode = Get-HttpStatusCode -ErrorRecord $_
        $errorText = Read-ProblemDetail -ErrorRecord $_

        if ($statusCode -eq 400) {
            throw "Start request failed (400): $errorText. This usually means M365 auth runtime settings are incomplete in the deployed app, or app registration redirect URI does not match the API callback URL."
        }

        throw "Start request failed: $errorText"
    }

    if ([string]::IsNullOrWhiteSpace($start.authorizationUrl)) {
        throw "Start response did not include authorizationUrl."
    }

    Write-Host ""
    Write-Host "Authorization URL: $($start.authorizationUrl)" -ForegroundColor Yellow
    Write-Host ""

    if ($OpenBrowser -or -not $PSBoundParameters.ContainsKey('OpenBrowser')) {
        Write-Status "Opening authorization URL in your browser..."
        Start-Process $start.authorizationUrl
    }
    else {
        Write-Status "Open the URL above in your browser to continue sign-in."
    }

    Write-Status "Complete browser sign-in, then waiting for callback completion..."

    $interval = 5
    $deadline = (Get-Date).ToUniversalTime().AddSeconds($MaxWaitSeconds)

    while ((Get-Date).ToUniversalTime() -lt $deadline) {
        Start-Sleep -Seconds $interval

        try {
            $status = Invoke-RestMethod -Method Get -Uri "$authEndpointBase/status"
            if ($status.isConfigured) {
                Write-Status "M365 authentication completed successfully." -Level "Success"
                Write-Status "WorkIQ can now access your M365 data." -Level "Success"
                exit 0
            }

            Write-Status "Still waiting for authorization callback..."
        }
        catch {
            $errorText = Read-ProblemDetail -ErrorRecord $_
            throw "Status check failed: $errorText"
        }
    }

    throw "Timed out waiting for authorization after $MaxWaitSeconds seconds. Re-run this script to start again."
}
catch {
    Write-Status $_.Exception.Message -Level "Error"
    exit 1
}
