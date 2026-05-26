# Message Screener

Message Screener is a .NET 10 solution that screens inbound Teams messages, proposes a fast response, and keeps the owner in approval control.

## After Clone Setup

Run this from the repository root:

```powershell
pwsh ./scripts/setup.ps1
```

The setup script calls GitHub Copilot CLI and instructs it to use WorkIQ to generate your communication twin from your Teams and email history.

Generated outputs:

- `src/MessageScreener.Api/copilot-config/skills/communication-twin/communication-twin.json`
- `src/MessageScreener.Api/copilot-config/skills/communication-twin/SKILL.md`

Prompt source (setup-time only):

- `scripts/prompts/communication-twin.workiq.prompt.md`

The generated twin files are deployment-shipped runtime files referenced by the screening pipeline. The setup prompt is a developer-time artifact only and is not part of product runtime configuration.

## AI Boundary

This repo keeps developer-facing AI guidance separate from product runtime AI assets.

Developer-facing guidance for people working in this repo lives in:

- `.github/copilot-instructions.md`
- `.digital-twin/`
- `.github/agents/`
- `.github/prompts/`
- `.github/skills/`

Product runtime AI assets shipped with the deployed service live in:

- `src/MessageScreener.Api/copilot-config/`
- `src/MessageScreener.Api/copilot-config/.mcp.json`

The repo-root `.mcp.json` is the developer seed for runtime MCP configuration. `scripts/setup-copilot-runtime.ps1` copies it into `src/MessageScreener.Api/copilot-config/.mcp.json`.

`Message Screener` uses `GitHub.Copilot.SDK` at runtime to draft responses during screening. The deployed app only needs the runtime assets under `src/MessageScreener.Api/copilot-config/`; the repo-level developer assets are not part of the product harness.

If your Copilot CLI binary is not `copilot`:

```powershell
pwsh ./scripts/setup.ps1 -CopilotCliPath "/usr/local/bin/copilot"
```

If you need to recreate your profile:

```powershell
pwsh ./scripts/setup.ps1 -Force
```

### One-Command Copilot Runtime Hook

To imbue runtime GitHub Copilot SDK sessions with repo MCP/skills/agent configuration in one step:

```powershell
pwsh ./scripts/setup-copilot-runtime.ps1 -Force
```

This hook:

- validates the repo-root `.mcp.json` exists
- writes `src/MessageScreener.Api/copilot-config/.mcp.json` for runtime MCP discovery and default GHCP behavior
- ensures the runtime skill directory exists (`src/MessageScreener.Api/copilot-config/skills`)
- writes `src/MessageScreener.Api/copilot-config/copilot.runtime.settings.sample.json`

`setup.ps1` runs this hook automatically unless you pass:

```powershell
pwsh ./scripts/setup.ps1 -SkipCopilotRuntimeHook
```

To add custom capability quickly:

1. Add MCP servers to `.mcp.json`, then run `pwsh ./scripts/setup-copilot-runtime.ps1 -Force`.
2. Add or update runtime knowledge skills under `src/MessageScreener.Api/copilot-config/skills/`.
3. Update runtime researcher behavior in `src/MessageScreener.Api/copilot-config/agents/` and `src/MessageScreener.Api/copilot-config/prompts/`.
4. Update runtime system behavior in `src/MessageScreener.Api/copilot-config/prompts/copilot-reply.system.prompt.md`.
5. Optionally set `MESSAGE_SCREENER_COPILOT_AGENT` and `MESSAGE_SCREENER_COPILOT_MODEL`.

## Dev Container

This repository includes a dev container with the tooling needed for setup, build, and deploy workflows:

- .NET 10 SDK
- PowerShell
- Azure CLI
- Azure Developer CLI (azd)
- GitHub CLI
- GitHub Copilot CLI (`copilot`)

Open the repo in the dev container and the post-create bootstrap will configure GitHub Copilot CLI + Digital Twin plugin and shell aliases.

If `copilot --version` prints `Cannot find GitHub Copilot CLI`, your shell is resolving a VS Code shim instead of the real CLI. Re-run bootstrap in the container:

```bash
bash .devcontainer/scripts/bootstrap-copilot-digital-twin.sh
```

`azd` is installed via the devcontainer feature (`ghcr.io/azure/azure-dev/azd:latest`). If `azd` is missing, rebuild the container so feature provisioning re-runs.

Before first container start on a new host, export effective git config for in-container global replication:

```bash
bash .devcontainer/scripts/export-effective-git-config.sh "$(pwd)" .devcontainer/gitconfig.effective
```

To generate and merge host cache mounts into the final dev container config:

```bash
bash .devcontainer/scripts/generate-cache-mount-config.sh --allow-missing .devcontainer/cache-mounts.generated.json
bash .devcontainer/scripts/merge-devcontainer-config.sh
```

To validate the resulting dev container configuration:

```bash
bash .devcontainer/scripts/validate-devcontainer-config.sh
```

If host cache environment variables are not set, validation reports cache-related warnings and continues.

Inside the container you can run:

```powershell
pwsh ./scripts/setup.ps1
dotnet build MessageScreener.slnx -warnaserror
```

VS Code tasks are included:

- `setup: communication twin`
- `build: solution`

## Build

```powershell
dotnet build MessageScreener.slnx -warnaserror
```

## Deploy With azd

Deployment is expected to run through Azure Developer CLI (`azd`).

`azure.yaml` defines a `postdeploy` hook that calls:

```powershell
pwsh ./scripts/azd-generate-teams-manifest.ps1
```

This single script resolves azd environment values and generates the Teams manifest directly.

The generated manifest includes a Teams message action command (`Forward to Message Screener`) so users can invoke screening from message Actions in 1:1 and group chats.

### Provisioned v1 Infrastructure

`infra/main.bicep` provisions the baseline required for v1:

- Azure Container Apps environment and API container app
- User-assigned managed identity for workload auth
- Azure Bot Service registration bound to the managed identity
- Microsoft Teams channel on the Bot Service
- Azure Container Registry for image deployment
- Log Analytics workspace for container app diagnostics

The template also creates role assignments for the app identity:

- `AcrPull` on ACR

### azd Environment Inputs

Set these before running `azd up`:

```powershell
azd env set AZURE_LOCATION eastus
azd env set MESSAGE_SCREENER_GITHUB_TOKEN <github-token>
azd env set MESSAGE_SCREENER_PUBLIC_BASE_URL https://<deployed-api-url>
```

Optional configuration:

```powershell
azd env set MESSAGE_SCREENER_COPILOT_MODEL gpt-4.1
azd env set MESSAGE_SCREENER_COPILOT_AGENT message-screener-researcher
azd env set MESSAGE_SCREENER_PERSONAL_REVIEW_CONVERSATION_ID <teams-chat-id>
azd env set MESSAGE_SCREENER_AUDIT_OWNER_READ_API_KEY <key-for-audit-reads>
```

### M365 Authentication for WorkIQ

This service no longer performs backend-managed M365 OAuth app registration or token storage.
To avoid tenant admin-consent blockers, WorkIQ access is expected to run in the user's approved
Copilot/Teams context.

You can run this helper to confirm the mode:

```powershell
pwsh ./scripts/auth-workiq.ps1
```

`MESSAGE_SCREENER_TEAMS_APP_ID` is optional. If omitted, infra generates a deterministic Teams app ID for the manifest package.

When required environment values are present, the hook generates:

- `dist/<env>/teamsapp/manifest.json`
- `dist/<env>/message-screener-teamsapp.zip`

After `azd up`, import the generated zip package into Teams for testing:

- `manifest.json`
- `color.png`
- `outline.png`

Expected azd environment values:

- `MESSAGE_SCREENER_PUBLIC_BASE_URL`
- `MESSAGE_SCREENER_TEAMS_APP_ID`
- `MESSAGE_SCREENER_TEAMS_BOT_ID`

Copilot runtime settings (recommended):

- `MESSAGE_SCREENER_GITHUB_TOKEN` (required for deployed runtime Copilot SDK sessions)
- `MESSAGE_SCREENER_COPILOT_MODEL` (optional)
- `MESSAGE_SCREENER_COPILOT_AGENT` (optional, default `message-screener-researcher`)

Additional Copilot configuration keys (optional):

- `MessageScreener__Copilot__ConfigDirectory` (default `copilot-config`)
- `MessageScreener__Copilot__EnableConfigDiscovery` (default `true`)
- `MessageScreener__Copilot__SystemPromptPath` (default `copilot-config/prompts/copilot-reply.system.prompt.md`)
- `MessageScreener__Copilot__SkillDirectories__0` (default `copilot-config/skills`)
- `MessageScreener__Copilot__MessageMode` (default `interactive`)

The runtime harness keeps its Copilot config, MCP config, prompts, and skills under `src/MessageScreener.Api/copilot-config/`. Those are separate from the repo-development Copilot instructions under `.github/`.

### How To Configure Deployed Agent Knowledge And Behavior

Use these runtime files to tune deployed behavior:

- Knowledge and context retrieval skills: `src/MessageScreener.Api/copilot-config/skills/`
- Research-agent definition: `src/MessageScreener.Api/copilot-config/agents/message-screener-researcher.agent.md`
- Research-agent prompting scaffold: `src/MessageScreener.Api/copilot-config/prompts/message-screener-reply.prompt.md`
- Runtime system instructions: `src/MessageScreener.Api/copilot-config/prompts/copilot-reply.system.prompt.md`
- Runtime communication persona: `src/MessageScreener.Api/copilot-config/skills/communication-twin/communication-twin.json` and `src/MessageScreener.Api/copilot-config/skills/communication-twin/SKILL.md`
- Setup-only twin generation prompt: `scripts/prompts/communication-twin.workiq.prompt.md` (not deployed runtime config)

Use these MCP files intentionally by environment:

- Developer seed config: `.mcp.json` (uses `${env:GITHUB_TOKEN}`)
- Deployed runtime MCP config: `src/MessageScreener.Api/copilot-config/.mcp.json` (uses `${env:MESSAGE_SCREENER_GITHUB_TOKEN}`)

When you change MCP servers in `.mcp.json`, re-run `pwsh ./scripts/setup-copilot-runtime.ps1 -Force` so the runtime copies stay in sync.

Recommended environment setup before `azd up`:

```powershell
azd env set AZURE_LOCATION eastus
azd env set MESSAGE_SCREENER_GITHUB_TOKEN <github-token-with-copilot-access>
azd env set MESSAGE_SCREENER_COPILOT_MODEL gpt-4.1
azd env set MESSAGE_SCREENER_COPILOT_AGENT message-screener-researcher
```

Runtime review delivery also requires a personal screener destination conversation ID:

- `MessageScreener__Teams__PersonalReviewConversationId`

For first-run testing, you do not need to set this before `azd up`. After importing the app, open the Message Screener personal chat and send `help` once. The service learns that personal conversation at runtime and uses it as the review destination.

Immediate use flow after install:

1. Open Message Screener in a personal chat and send `help` once.
2. In a supported Teams message, click `Forward to Message Screener` from message actions.
3. Review the generated draft reply and manually send it.

If you want the destination to persist across restarts or redeployments, set it explicitly and redeploy:

```powershell
azd env set MESSAGE_SCREENER_PERSONAL_REVIEW_CONVERSATION_ID <teams-personal-chat-id>
azd deploy
```

Optional owner-scoped audit reads can be enabled with:

- `MessageScreener__Audit__OwnerReadApiKey`

When configured, recent forward audit entries are available from `GET /api/audit/forwards` with header `X-MessageScreener-Owner-Key: <configured key>`.

Copilot runtime readiness is also available through:

- `GET /api/readiness/copilot`

This endpoint uses the same `X-MessageScreener-Owner-Key` header and returns:

- `200` when all checks pass
- `503` when one or more checks fail

Readiness checks include:

1. Persona file exists and is non-default.
2. GitHub token is configured for runtime Copilot SDK sessions.
3. MCP servers are discovered and at least one is connected.
4. Skills are loaded and at least one `message-screener` skill is enabled.
5. A live Copilot draft probe succeeds.

`MESSAGE_SCREENER_TEAMS_BOT_ID` is produced by infra deployment from the managed identity client ID and does not need to be set manually.

If those values are missing, the hook logs a skip message and deployment continues safely.

## Notes

- Runtime Copilot assets used by the deployed service are under `src/MessageScreener.Api/copilot-config`.

## Documented Solutions

- Knowledge base root: `docs/solutions/`
- This session's learning: `docs/solutions/integration-issues/copilot-cli-shim-shadowing-devcontainer-2026-05-24.md`
