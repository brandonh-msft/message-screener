param(
    [switch]$SkipChatReadWriteAll
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-AzdEnvValueMap {
    $valueMap = @{}

    if ($null -eq (Get-Command azd -ErrorAction SilentlyContinue)) {
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

function FirstNonEmpty {
    param(
        [string[]]$Values
    )

    foreach ($value in $Values) {
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value
        }
    }

    return $null
}

function Invoke-AzCli {
    param(
        [string[]]$Arguments
    )

    $output = & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: az $($Arguments -join ' ')"
    }

    return ($output | Out-String).Trim()
}

function Test-IsPrivilegeError {
    param(
        [string]$Message
    )

    if ([string]::IsNullOrWhiteSpace($Message)) {
        return $false
    }

    return ($Message -match 'Insufficient privileges' -or
            $Message -match 'Authorization_RequestDenied' -or
            $Message -match 'does not have authorization')
}

function Get-GraphAppRoleId {
    param(
        [string]$GraphSpId,
        [string]$RoleValue
    )

    $query = "appRoles[?value=='$RoleValue' && contains(allowedMemberTypes, 'Application')].id | [0]"
    $roleId = Invoke-AzCli -Arguments @('ad', 'sp', 'show', '--id', $GraphSpId, '--query', $query, '-o', 'tsv')
    if ([string]::IsNullOrWhiteSpace($roleId)) {
        throw "Could not resolve Microsoft Graph app role id for $RoleValue"
    }

    return $roleId
}

function Ensure-AppRoleAssignment {
    param(
        [string]$ManagedIdentitySpId,
        [string]$GraphSpId,
        [string]$RoleId,
        [string]$RoleName
    )

    $assignmentsJson = Invoke-AzCli -Arguments @(
        'rest',
        '--method', 'GET',
        '--uri', "https://graph.microsoft.com/v1.0/servicePrincipals/$ManagedIdentitySpId/appRoleAssignments",
        '--output', 'json'
    )

    $assignments = $assignmentsJson | ConvertFrom-Json
    $alreadyAssigned = $false

    if ($null -ne $assignments.value) {
        foreach ($assignment in $assignments.value) {
            if ($assignment.resourceId -eq $GraphSpId -and $assignment.appRoleId -eq $RoleId) {
                $alreadyAssigned = $true
                break
            }
        }
    }

    if ($alreadyAssigned) {
        Write-Host "Graph app role already assigned: $RoleName"
        return
    }

    $bodyObject = @{
        principalId = $ManagedIdentitySpId
        resourceId = $GraphSpId
        appRoleId = $RoleId
    }

    $bodyJson = $bodyObject | ConvertTo-Json -Compress

    Invoke-AzCli -Arguments @(
        'rest',
        '--method', 'POST',
        '--uri', "https://graph.microsoft.com/v1.0/servicePrincipals/$ManagedIdentitySpId/appRoleAssignments",
        '--body', $bodyJson,
        '--output', 'none'
    ) | Out-Null

    Write-Host "Assigned Graph app role: $RoleName"
}

try {
    if ($null -eq (Get-Command az -ErrorAction SilentlyContinue)) {
        throw 'Azure CLI (az) is required but was not found on PATH.'
    }

    $azdEnv = Get-AzdEnvValueMap
    $managedIdentityClientId = FirstNonEmpty -Values @(
        [Environment]::GetEnvironmentVariable('MESSAGE_SCREENER_MANAGED_IDENTITY_CLIENT_ID'),
        [Environment]::GetEnvironmentVariable('MessageScreener__Teams__ManagedIdentityClientId'),
        [string]$azdEnv['MESSAGE_SCREENER_MANAGED_IDENTITY_CLIENT_ID']
    )

    if ([string]::IsNullOrWhiteSpace($managedIdentityClientId)) {
        throw 'Managed identity client id not found. Expected MESSAGE_SCREENER_MANAGED_IDENTITY_CLIENT_ID in azd env outputs.'
    }

    $managedIdentitySpId = Invoke-AzCli -Arguments @(
        'ad', 'sp', 'list',
        '--filter', "appId eq '$managedIdentityClientId'",
        '--query', '[0].id',
        '-o', 'tsv'
    )

    if ([string]::IsNullOrWhiteSpace($managedIdentitySpId)) {
        throw "Could not resolve service principal for managed identity appId $managedIdentityClientId"
    }

    $graphSpId = Invoke-AzCli -Arguments @(
        'ad', 'sp', 'list',
        '--filter', "appId eq '00000003-0000-0000-c000-000000000000'",
        '--query', '[0].id',
        '-o', 'tsv'
    )

    if ([string]::IsNullOrWhiteSpace($graphSpId)) {
        throw 'Could not resolve Microsoft Graph service principal in tenant.'
    }

    $chatReadAllRoleId = Get-GraphAppRoleId -GraphSpId $graphSpId -RoleValue 'Chat.Read.All'
    Ensure-AppRoleAssignment -ManagedIdentitySpId $managedIdentitySpId -GraphSpId $graphSpId -RoleId $chatReadAllRoleId -RoleName 'Chat.Read.All'

    if (-not $SkipChatReadWriteAll) {
        $chatReadWriteAllRoleId = Get-GraphAppRoleId -GraphSpId $graphSpId -RoleValue 'Chat.ReadWrite.All'
        Ensure-AppRoleAssignment -ManagedIdentitySpId $managedIdentitySpId -GraphSpId $graphSpId -RoleId $chatReadWriteAllRoleId -RoleName 'Chat.ReadWrite.All'
    }

    Write-Host 'Graph app permission configuration complete.'
}
catch {
    $errorMessage = $_.Exception.Message
    if (Test-IsPrivilegeError -Message $errorMessage) {
        Write-Warning 'Insufficient Entra permissions to grant Microsoft Graph app roles automatically. Continuing without app-role assignment.'
        Write-Warning 'The bot command path still works, but Graph webhook subscriptions and Graph-based auto replies require an admin grant.'
        exit 0
    }

    throw
}
