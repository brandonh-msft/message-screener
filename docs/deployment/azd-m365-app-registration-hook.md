# Azure Developer CLI: M365 App Registration Hook

**Date:** 2026-05-25  
**Status:** Production-Ready  
**Trigger:** Automatic on `azd provision` (postprovision hook)

## Overview

The `scripts/azd-configure-m365-app.ps1` script is automatically invoked as an azd **postprovision hook** to register an M365 OAuth2 app in Entra ID for WorkIQ MCP integration. This eliminates manual tenant admin tasks during deployment.

## Integration

### Where It Runs

**File:** `azure.yaml` → `hooks.postprovision`

```yaml
hooks:
    postprovision:
        shell: pwsh
        run: >-
            if (Test-Path ../../scripts/azd-configure-m365-app.ps1) {
              ../../scripts/azd-configure-m365-app.ps1
            } else {
              Write-Warning 'Could not locate scripts/azd-configure-m365-app.ps1'
            }
        continueOnError: true
```

**When:** After `azd provision` completes (after Key Vault, Container Apps, and other resources are deployed)

**Safe for re-runs:** Yes. By default, skips if app already exists (`-SkipIfExists` default)

## What It Does

1. **Discovers Context**
   - Gets tenant ID from `az account show`
   - Gets Key Vault URL from `AZURE_KEY_VAULT_ENDPOINT`
   - Constructs M365 OAuth reply URL from `MESSAGE_SCREENER_PUBLIC_BASE_URL` or resource group name

2. **Creates/Updates M365 App**
   - Registers app named "Message Screener WorkIQ" in Entra ID
   - Sets reply URL to `https://<api-url>/api/authm365/callback`
   - Requests Microsoft Graph delegated scope: `Mail.Read` (no-admin profile)

3. **Manages Credentials**
   - Generates client secret with 1-year expiry
  - Writes M365 values into azd environment for the next `azd deploy`
  - `azd deploy` seeds Key Vault secrets: `m365-client-id`, `m365-client-secret`, `m365-tenant-id`
   - Can revoke old secrets if re-registering

4. **Updates azd Environment**
   - Sets `MESSAGE_SCREENER_M365_CLIENT_ID`
   - Sets `MESSAGE_SCREENER_M365_CLIENT_SECRET`
   - Sets `MESSAGE_SCREENER_M365_TENANT_ID`
  - Sets canonical azd env keys consumed by infra parameter mapping

## Usage

### Standard Deployment (Automatic)

```powershell
azd provision
azd deploy
```

The hook runs automatically after provision. Check the output for success message.

### Manual Re-run

```powershell
# Re-register (reuses existing app if it exists)
pwsh ./scripts/azd-configure-m365-app.ps1

# Force new registration (revokes old secret, creates new one)
pwsh ./scripts/azd-configure-m365-app.ps1 -Force

# Reuse existing app (safe, idempotent)
pwsh ./scripts/azd-configure-m365-app.ps1 -SkipIfExists
```

### Debugging

```powershell
# Check if app was registered
az ad app list --filter "displayName eq 'Message Screener WorkIQ'" --query "[].{id:appId,displayName:displayName}"

# View app details in Entra ID portal
https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationsListBlade

# Check what's in Key Vault
az keyvault secret list --vault-name <vault-name> --query "[?starts_with(name, 'm365')]"
```

## Prerequisites

### Permissions Required

- **User Identity:** Signed in to Azure CLI with permission to:
  - Register apps in Entra ID (Application Developer or Application Administrator role)
  - Create secrets in the app
  - Write to Key Vault

- **Key Vault:** Already provisioned (created by `azd provision`)
  - Accessible via `AZURE_KEY_VAULT_ENDPOINT` environment variable
  - Current user must have `Secret Set` permission

### Environment Variables

**Required (set by azd automatically):**
- `AZURE_KEY_VAULT_ENDPOINT` - Key Vault base URL

**Optional but recommended:**
- `MESSAGE_SCREENER_PUBLIC_BASE_URL` - Deployed API URL (for M365 reply URL)
- `AZURE_ENV_NAME` - Used in default messages

**Fallback (if MESSAGE_SCREENER_PUBLIC_BASE_URL not set):**
- Script constructs URL from `AZURE_RESOURCE_GROUP`: `https://messagescreener-<rg>.azurecontainerapps.io`

## Scopes Requested

The M365 app is registered with minimal-privilege scopes:

- `Mail.Read` - Read user's emails (for context analysis)

**Why These?**
- Minimal permissions (read-only, no write/delete)
- Sufficient for email-based WorkIQ context retrieval without tenant admin consent
- User must still grant consent individually via browser auth flow

## Security & Compliance

✅ **Secrets Management**
- Client secret never exposed in logs
- Stored only in Key Vault
- 1-year expiry (auto-renew in next deployment)

✅ **App Permissions**
- No admin consent required (Mail.Read delegated profile, subject to tenant user-consent policy)
- Least-privilege (read-only Graph scope)
- Reply URL hardcoded to prevent open redirect

✅ **Audit Trail**
- App creation logged to Entra ID audit
- Secret creation logged to Key Vault audit
- Script logs to stdout (visible in azd output)

✅ **Error Handling**
- Graceful if app already exists
- Continues deployment if script fails (`continueOnError: true`)
- Clear error messages for debugging

## Idempotency & Re-runs

### First Run
1. No app exists → creates new app ✓
2. Generates client secret ✓
3. Persists values into azd env ✓
4. `azd deploy` seeds Key Vault secrets ✓

### Subsequent Runs (Default)
1. App exists → **reuses existing app** ✓
2. Secret already stored → **skips** ✓
3. Deployment continues ✓

### Force Re-register
```powershell
pwsh ./scripts/azd-configure-m365-app.ps1 -Force
```
1. Revokes all old secrets ✓
2. Creates new secret ✓
3. Updates azd env ✓
4. Next `azd deploy` refreshes Key Vault secrets ✓

## Troubleshooting

### Error: "Unable to determine tenant ID"

**Cause:** Not signed into Azure CLI

**Fix:**
```powershell
az login
az account set --subscription <subscription-id>
```

### Error: "AZURE_KEY_VAULT_ENDPOINT is not set"

**Cause:** Provisioning didn't complete or Key Vault wasn't created

**Fix:**
```powershell
# Verify Key Vault was created
az keyvault list --resource-group rg-<env-name>

# Manually set and retry
$vaultUri = (az keyvault list --resource-group rg-<env-name> --query "[0].properties.vaultUri" -o tsv)
$env:AZURE_KEY_VAULT_ENDPOINT = $vaultUri
pwsh ./scripts/azd-configure-m365-app.ps1
```

### Error: "Failed to create app registration"

**Cause:** Insufficient permissions or app name conflict

**Fix:**
```powershell
# Check permissions (need "Application Developer" or higher)
az ad signed-in-user show --query "userType"

# Check if app already exists
az ad app list --filter "displayName eq 'Message Screener WorkIQ'"

# Request tenant admin to grant app registration permissions if needed
```

## Deployment Flow

```
azd provision
    ↓
[Provision all resources: ACR, Container Apps, Key Vault, etc.]
    ↓
postprovision hook triggered
    ↓
scripts/azd-configure-m365-app.ps1
    ├─ Determine tenant ID
    ├─ Create/locate M365 app
    ├─ Generate client secret
    └─ Update azd env
    ↓
azd deploy
    ↓
[Seed Key Vault secrets from azd env + deploy container with M365Auth config]
    ↓
Owner initiates M365 auth:
POST /api/authm365/start
    → Browser redirect to Entra (PKCE)
    → Callback at /api/authm365/callback
    → Refresh token stored in KV
    → WorkIQ can query M365
```

## Integration with Deployment Config

After the hook completes, `.NET appsettings` are configured via `azd deploy`:

```json
{
  "MessageScreener": {
    "M365Auth": {
      "Enabled": true,
      "ClientId": "${MESSAGE_SCREENER_M365_CLIENT_ID}",
      "ClientSecret": "${MESSAGE_SCREENER_M365_CLIENT_SECRET}",
      "TenantId": "${MESSAGE_SCREENER_M365_TENANT_ID}",
      "KeyVaultUrl": "${AZURE_KEY_VAULT_ENDPOINT}"
    }
  }
}
```

## Next Steps After Deployment

1. **Verify App Registration**
   ```powershell
   az ad app list --filter "displayName eq 'Message Screener WorkIQ'"
   ```

2. **Check Key Vault Secrets**
   ```powershell
   az keyvault secret list --vault-name <vault-name> --query "[?starts_with(name, 'm365')]"
   ```

3. **Owner Authenticates**
   - Run: `pwsh ./scripts/auth-workiq.ps1`
   - Browser completes auth code + PKCE flow
   - Refresh token stored securely

4. **Verify Readiness**
   ```powershell
   curl https://<api-url>/api/authm365/status
   ```

## References

- [Azure Developer CLI](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/)
- [azd Hooks](https://learn.microsoft.com/en-us/azure/developer/azure-developer-cli/azd-reference#hooks)
- [Azure CLI App Registration](https://learn.microsoft.com/en-us/cli/azure/ad/app?view=azure-cli-latest)
- [OAuth 2.0 Authorization Code Flow with PKCE](https://datatracker.ietf.org/doc/html/rfc7636)
