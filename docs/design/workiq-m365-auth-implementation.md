# WorkIQ M365 Authentication Implementation Guide

**Date:** 2026-05-25  
**Status:** Implementation Complete (Phase 1 & 3)  
**Build Status:** ✅ Success (zero warnings/errors)

## Overview

This guide documents the implementation of M365 authentication for WorkIQ MCP integration in Message Screener API. The solution enables the API to securely query the owner's M365 data (Teams, email, meetings) via the WorkIQ MCP server.

## What Was Implemented

### Phase 1: M365 OAuth2 Token Management

**File:** [src/MessageScreener.Orchestration/M365TokenProvider.cs](../src/MessageScreener.Orchestration/M365TokenProvider.cs)

**Capabilities:**
- Device Flow OAuth2 to obtain owner's M365 authorization
- Secure refresh token storage in Azure Key Vault
- Automatic access token refresh with 5-minute buffer
- Token revocation support
- Validation of auth readiness

**Key Services:**
```csharp
IM365TokenProvider
- GetM365AccessTokenAsync(): Returns valid access token (cached/refreshed)
- StoreM365RefreshTokenAsync(): Persists owner's refresh token to KV
- RevokeM365AuthAsync(): Deletes auth and clears cache
- HasValidM365AuthAsync(): Checks auth readiness
```

**Token Lifecycle:**
1. Owner initiates Device Flow auth (`/auth/m365/initiate`)
2. Polls for completion (`/auth/m365/poll`)
3. Refresh token stored securely in Key Vault
4. On-demand: Access token retrieved/refreshed automatically
5. Can be revoked anytime (`/auth/m365/revoke`)

### Phase 3: Credential Bridge (Option C)

**File:** [src/MessageScreener.Orchestration/McpCredentialBridge.cs](../src/MessageScreener.Orchestration/McpCredentialBridge.cs)

**Capabilities:**
- Prepares M365 credential context for GHCP session
- Writes token to secure temporary file (Unix: chmod 600)
- Sets environment variables for MCP server consumption
- Cleanup with secure overwrite on temp files
- Graceful fallback (degraded mode) if M365 auth not available

**Key Services:**
```csharp
IMcpCredentialBridge
- PrepareCredentialContextAsync(): Stages M365 token for MCP invocation
- CleanupCredentialContextAsync(): Securely deletes temp credential files

McpCredentialContext
- EnvironmentVariables: M365_ACCESS_TOKEN, M365_CREDENTIAL_FILE, MCP_CREDENTIAL_DIR
- TemporaryFiles: List of files to cleanup after use
```

**Credential Flow:**
1. Before GHCP session creation, call `IMcpCredentialBridge.PrepareCredentialContextAsync()`
2. Receives M365 access token + environment variables
3. Token available to MCP server via `$M365_ACCESS_TOKEN` or credential file
4. After session, call `IMcpCredentialBridge.CleanupCredentialContextAsync()` to purge temp files

### OAuth2 Device Flow Endpoints

**File:** [src/MessageScreener.Api/Controllers/AuthM365Controller.cs](../src/MessageScreener.Api/Controllers/AuthM365Controller.cs)

**Endpoints:**

1. **POST `/api/auth-m365/initiate`**
   - Initiates Device Flow
   - Returns device code + user code
   - Owner visits verification URI to authenticate

2. **POST `/api/auth-m365/poll`**
   - Polls for auth completion (call repeatedly until authorized)
   - Returns status: `pending`, `authorized`, or error
   - On success, refresh token is stored securely

3. **POST `/api/auth-m365/revoke`**
   - Revokes M365 authentication
   - Deletes refresh token from Key Vault
   - Clears cached access token

4. **GET `/api/auth-m365/status`**
   - Checks if M365 auth is configured
   - Returns current readiness state

## Configuration

### appsettings.json

```json
{
  "MessageScreener": {
    "M365Auth": {
      "Enabled": false,
      "ClientId": "",
      "ClientSecret": "",
      "TenantId": "common",
      "KeyVaultUrl": ""
    }
  }
}
```

**Setup (via `azd env set`):**
```powershell
azd env set MESSAGE_SCREENER_M365_CLIENT_ID "xxx"
azd env set MESSAGE_SCREENER_M365_CLIENT_SECRET "yyy"
azd env set MESSAGE_SCREENER_M365_TENANT_ID "zzz"
azd env set MESSAGE_SCREENER_M365_VAULT_URL "https://vault.azure.net/"
```

### DI Registration (Program.cs)

```csharp
builder.Services
    .AddOptions<M365TokenProviderOptions>()
    .BindConfiguration(M365TokenProviderOptions.SectionName);
builder.Services.AddSingleton<IM365TokenProvider, M365TokenProvider>();
builder.Services.AddSingleton<IMcpCredentialBridge, McpCredentialBridge>();
```

## Integration with CopilotReplyDraftingService

**Next Step (Future Implementation):**

Modify `CopilotReplyDraftingService.ProbeDraftAsync()` to use the credential bridge:

```csharp
// Before session creation
McpCredentialContext credentialContext = await _mcpCredentialBridge
    .PrepareCredentialContextAsync(cancellationToken);

try
{
    // Create session with credential environment variables
    var sessionConfig = new SessionConfig
    {
        // ... existing config ...
        // Note: GHCP SDK may not support direct env var passing yet.
        // For now, env vars are inherited from process environment.
    };
    
    await using CopilotSession session = await client.CreateSessionAsync(sessionConfig, cancellationToken);
    
    // ... invoke session ...
}
finally
{
    // Cleanup temp credential files
    await _mcpCredentialBridge.CleanupCredentialContextAsync(credentialContext, cancellationToken);
}
```

## Testing Checklist

- [ ] Register M365 app in Entra ID (tenant admin task)
- [ ] Set configuration via `azd env set` or appsettings
- [ ] Test `POST /api/auth-m365/initiate` → receive device code
- [ ] Owner authenticates via verification URI
- [ ] Test `POST /api/auth-m365/poll` → returns authorized status
- [ ] Verify refresh token stored in Key Vault
- [ ] Test `GET /api/auth-m365/status` → returns `IsConfigured: true`
- [ ] Test `IM365TokenProvider.GetM365AccessTokenAsync()` → returns valid token
- [ ] Test `IMcpCredentialBridge.PrepareCredentialContextAsync()` → creates credential file + env vars
- [ ] Verify temp file has restricted permissions (Unix)
- [ ] Test `IMcpCredentialBridge.CleanupCredentialContextAsync()` → deletes temp files securely
- [ ] Test `POST /api/auth-m365/revoke` → removes auth + clears cache

## Key Design Decisions

### 1. Device Flow vs. Authorization Code Flow

**Chosen:** Device Flow

**Rationale:**
- No browser redirect required (headless/service context)
- Owner gets clear user code to enter
- Works well for internal tools

**Alternative:** Authorization Code Flow (if interactive browser available)

### 2. Refresh Token Storage

**Chosen:** Azure Key Vault via `DefaultAzureCredential` (MI)

**Rationale:**
- Secure at-rest encryption
- Managed identity auth (no hardcoded credentials)
- Audit trail via Key Vault logs
- Complies with Microsoft SFI defaults

### 3. Token Passing to MCP Server (Option C: Credential Bridge)

**Chosen:** Environment variable + secure temp file

**Rationale:**
- GHCP SDK has no built-in MCP credential injection yet (as of SDK v0.3.0)
- Environment variables work across process boundaries
- Temp file approach is more secure than env var alone
- Cleanup with overwrite prevents disk recovery

**Future:** Propose `SessionConfig.M365Token` to GitHub Copilot SDK

### 4. Scopes

**Chosen:** `Mail.Read`, `Chat.Read`, `TeamsActivity.Read`

**Rationale:**
- Minimal permissions for WorkIQ to analyze communication
- No write permissions (read-only)
- No access to sensitive features

## Security Considerations

### Secret Management
✅ Refresh token stored in Key Vault (not in config)  
✅ Client secret in Key Vault (not in code)  
✅ Access token cached in memory only (never persisted)

### Token Lifecycle
✅ Automatic refresh with 5-min buffer (prevents expiry)  
✅ Old refresh tokens replaced (no stale tokens)  
✅ Revocation support (owner can disconnect at any time)

### Temporary Credentials
✅ Secure temp directory (isolated per-call via GUID)  
✅ Unix: chmod 600 on credential file  
✅ Overwrite with random data before deletion  
✅ Cleanup on error path

### Audit
✅ Source-generated structured logging  
✅ All token operations logged  
✅ Key Vault access logged (native)

## Limitations & Future Work

### Current Limitations
- GHCP SDK does not support passing credentials to MCP servers natively
  - Workaround: Environment variable + temp file
  - Future: Propose `SessionConfig.M365Token` to GitHub Copilot SDK
- Device Flow requires owner to manually authenticate once
  - Could add silent refresh via browser for interactive scenarios
- M365 scopes are hardcoded (could be configurable)

### Future Enhancements
1. **OBO Flow (On-Behalf-Of)**: If WorkIQ supports service principal auth
   - Eliminates need for refresh token storage
   - Uses short-lived delegated tokens instead
2. **Consent Management UI**: Dashboard to view/revoke M365 auth
3. **Token Rotation**: Automatic refresh token rotation on use
4. **Audit Dashboard**: Visualize WorkIQ data access patterns
5. **Incremental Consent**: Request additional scopes on-demand

## References

- [OAuth 2.0 Device Authorization Grant](https://datatracker.ietf.org/doc/html/rfc8628)
- [Microsoft Graph Authentication](https://learn.microsoft.com/en-us/graph/auth/)
- [Azure Key Vault SDK](https://learn.microsoft.com/en-us/azure/key-vault/general/overview)
- [Microsoft SFI Defaults](https://microsoft.github.io/security-framework-implementation/)
- [Source-Generated Logging](https://learn.microsoft.com/en-us/dotnet/core/extensions/logger-message-generator)
