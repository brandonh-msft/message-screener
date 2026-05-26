# WorkIQ M365 Authentication Solution

**Date:** 2026-05-25  
**Status:** Investigation & Design  
**Audience:** Architecture, API team

## Problem Statement

The Message Screener API (running via GHCP SDK in a hosted context) needs to invoke WorkIQ MCP server to query the owner's M365 data (Teams messages, emails, etc.) at runtime. Currently:

1. **Setup-time**: WorkIQ is invoked via Copilot CLI with the developer's local authentication context (VS Code user is already authenticated to M365).
2. **Runtime**: The API runs in a service context (Azure Container App with managed identity). When `CopilotSession` initializes, it has no inherent M365 authentication for WorkIQ to use.

**Gap:** WorkIQ cannot access the owner's M365 data at runtime because:
- The MCP server is launched by the GHCP SDK but has no M365 credentials.
- The API runs under managed identity (for Teams Bot Connector, Graph, etc.), not the owner's user context.
- WorkIQ needs the owner's explicit M365 authentication (or delegated auth from the app).

## Required Capabilities

1. **Owner M365 Authentication**: The owner must grant the API permission to access their M365 data.
2. **Token Management**: Store and refresh the owner's M365 auth tokens securely.
3. **MCP Server Auth Context**: Pass the M365 auth context to WorkIQ when the GHCP SDK invokes it.
4. **Consent & Compliance**: Implement clear consent flow and audit trail.

## Investigation: GHCP SDK + MCP Server Authentication

### Current State

From `CopilotReplyDraftingService.cs`:
```csharp
await using CopilotSession session = await client.CreateSessionAsync(new SessionConfig
{
    OnPermissionRequest = PermissionHandler.ApproveAll,
    Model = options.Value.Model,
    Agent = options.Value.Agent,
    GitHubToken = options.Value.GitHubToken,  // GitHub auth only
    EnableConfigDiscovery = options.Value.EnableConfigDiscovery,
    ConfigDir = configDirectory,
    SkillDirectories = skillDirectories,
    SystemMessage = new SystemMessageConfig { ... }
});
```

**Key Observations:**
- `SessionConfig` accepts `GitHubToken` but no M365 token field.
- `OnPermissionRequest` handles session-level permission prompts, not MCP server auth.
- MCP servers in `.mcp.json` are launched with inherited environment variables only.

### MCP Server Launch Mechanism

From `.mcp.json`:
```json
{
  "workiq": {
    "command": "copilot",
    "args": [
      "mcp",
      "serve",
      "workiq"
    ]
  }
}
```

When `session.Rpc.Mcp.ListAsync()` is called, the GHCP SDK:
1. Spawns the MCP server process with configured `command` and `args`.
2. Communicates via stdio/RPC.
3. MCP server has access to environment variables and file system only.

**Currently, there is no built-in GHCP SDK mechanism to pass credentials to MCP servers at session init time.**

## Proposed Solution

### Architecture

```
Owner (M365 Tenant)
        ↓
    [OAuth2 Flow]
        ↓
[Tenant Admin / User Consent Dialog]
        ↓
API receives: access_token + refresh_token
        ↓
[Azure Key Vault] ← Store refresh_token securely
        ↓
At Runtime:
  - API retrieves refresh_token from KV
  - Exchanges for fresh access_token
  - Passes token to Copilot session (via SessionConfig OR environment)
  - Copilot session → WorkIQ MCP → M365 Graph
```

### Implementation Steps

#### Phase 1: Owner M365 Authentication Setup

**Goal:** Capture owner's M365 consent and store refresh token.

1. **Add M365 OAuth app registration** (tenant admin task, documented in setup):
   - Scopes: `Mail.Read`, `Chat.Read`, `TeamsActivity.Read` (minimum for WorkIQ)
   - Redirect URI: `https://<api-base>/api/authm365/callback`
   - Client ID & Secret → stored in Key Vault

2. **Add `/api/authm365/start` endpoint** (unauthenticated):
   - Initiates OAuth2 Authorization Code + PKCE flow.
   - Returns redirect URI for browser sign-in.

3. **Add `/api/authm365/callback` endpoint**:
   - Receives auth code from owner.
   - Exchanges code for access_token + refresh_token.
   - Stores refresh_token in Key Vault with owner identifier.
   - Returns success confirmation.

4. **Update appsettings**:
   ```json
   {
     "MessageScreener": {
       "M365Auth": {
         "Enabled": false,
         "ClientId": "",
         "ClientSecretKeyVaultUri": "",
         "RefreshTokenKeyVaultUri": "",
         "TenantId": ""
       }
     }
   }
   ```

#### Phase 2: Token Retrieval & Refresh

**Goal:** Keep access token fresh for runtime WorkIQ calls.

1. **Create `IM365TokenProvider`**:
   ```csharp
   public interface IM365TokenProvider
   {
       ValueTask<string> GetM365AccessTokenAsync(CancellationToken ct);
       ValueTask RefreshM365TokenAsync(CancellationToken ct);
   }
   ```

2. **Implement token cache + refresh logic**:
   - Cache access_token in memory with TTL.
   - On near-expiry, retrieve refresh_token from KV, exchange for new access_token.
   - Update KV with new refresh_token.

#### Phase 3: Pass M365 Token to GHCP Session

**Goal:** Make M365 token available to WorkIQ MCP server.

**Option A: Environment Variable Passthrough**
- At session creation, set environment variable `M365_ACCESS_TOKEN` or similar.
- Modify `.mcp.json` to reference that variable:
  ```json
  {
    "workiq": {
      "command": "copilot",
      "args": ["mcp", "serve", "workiq"],
      "env": {
        "M365_ACCESS_TOKEN": "${env:M365_ACCESS_TOKEN}"
      }
    }
  }
  ```
- **Caveat**: This requires GHCP SDK to support environment variable interpolation at session time (unlikely without extension).

**Option B: SessionConfig Extension (Future)**
- Propose GHCP SDK enhancement: add `M365Token` field to `SessionConfig`.
- GHCP SDK passes it to MCP servers via a standard credential context mechanism.
- **Status**: Requires SDK update; file feature request with GitHub.

**Option C: Intermediate Credential Bridge (Pragmatic)**
- Create a local credential store / environment setup before session creation.
- Write M365 token to a temporary file with restricted permissions.
- MCP server reads from known path (e.g., `~/.message-screener/m365-token`).
- Ensure file is cleaned up after session.

#### Phase 4: WorkIQ Integration

**Goal:** Ensure WorkIQ can read the M365 token and use it.

1. **Verify WorkIQ MCP server** supports M365 credential injection:
   - Check if WorkIQ docs specify expected auth mechanism.
   - If it expects env var or file path, align implementation.

2. **Update runtime prompt** to request WorkIQ calls:
   ```markdown
   # Communication Twin Skill
   
   Use WorkIQ to query the owner's Teams and email history.
   WorkIQ will use the authenticated M365 context to:
   - Fetch recent messages
   - Analyze communication patterns
   - Surface persona insights
   ```

#### Phase 5: Consent & Audit

**Goal:** Clear audit trail and owner control.

1. **Add consent record**:
   - Track who (owner), when, which scopes.
   - Store in audit log table or Key Vault secrets.

2. **Revocation endpoint** `/auth/m365/revoke`:
   - Owner can revoke M365 auth.
   - Deletes refresh_token from KV.
   - Calls Microsoft Graph to revoke token.

3. **Readiness check**:
   - `CopilotReadinessService` should report M365 token status.
   - Add to `CopilotDraftProbeResult`: `m365_auth_required` or `m365_auth_present`.

## Recommended Sequence

### v1 (Near-term: Manual Setup)
1. **Tenant admin registers app** in Entra ID (manual, documented).
2. **Owner authenticates once** via browser authorization endpoint (`/api/authm365/start`).
3. **Refresh token stored in Key Vault**.
4. **Option C (Credential Bridge)** implemented: token written to temp file before session.
5. **WorkIQ calls fetch user's data** using environment-injected token.

### v2 (Medium-term: SDK Enhancement)
- Propose `SessionConfig.M365Token` to GitHub Copilot SDK team.
- Once available, refactor to use native GHCP mechanism.

### v3 (Long-term: Delegated Flow)
- Explore on-behalf-of (OBO) flow if WorkIQ supports service principal auth.
- Reduces need for refresh token storage; uses short-lived delegated tokens.

## Risk & Compliance

### Security
- **Risk**: Refresh token stored in KV could be exfiltrated.
- **Mitigation**: RBAC on KV; minimal scope (`Mail.Read`, `Chat.Read`); rotate regularly.

### Compliance
- **Consent**: Clear consent dialog before storing token.
- **Audit**: Log all WorkIQ calls and M365 data accessed.
- **Retention**: Define token rotation and cleanup policy.

### User Experience
- **Complexity**: Owner must authenticate once at setup.
- **Transparency**: Clearly communicate what data is accessed and why.

## Implementation Checklist

- [ ] Add M365 OAuth app registration (Entra ID).
- [ ] Implement `IM365TokenProvider` interface.
- [ ] Add `/api/authm365/start` endpoint.
- [ ] Add `/api/authm365/callback` endpoint.
- [ ] Add `/api/authm365/revoke` endpoint.
- [ ] Implement token cache + refresh logic.
- [ ] Update `.mcp.json` or create credential bridge for WorkIQ auth.
- [ ] Update `CopilotReadinessService` to report M365 auth status.
- [ ] Add audit logging for M365 operations.
- [ ] Document OAuth flow in README.
- [ ] Test end-to-end: Owner auth → Token refresh → WorkIQ call.

## References

- [Microsoft Graph Authentication](https://learn.microsoft.com/graph/auth/)
- [OAuth 2.0 Authorization Code Flow with PKCE](https://learn.microsoft.com/azure/active-directory/develop/v2-oauth2-auth-code-flow)
- [Model Context Protocol (MCP) Spec](https://modelcontextprotocol.io/)
- [GitHub Copilot SDK Documentation](https://github.com/github/copilot-sdk)
