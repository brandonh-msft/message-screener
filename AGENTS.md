# AGENTS.md

## Project overview

Message Screener is a .NET 10 solution that screens inbound Teams messages, drafts a response, and keeps the owner in explicit approval control before anything is sent.

- Solution file: `MessageScreener.slnx`
- Primary API: `src/MessageScreener.Api`
- Supporting libraries: `src/MessageScreener.Audit`, `src/MessageScreener.Contracts`, `src/MessageScreener.Orchestration`, `src/MessageScreener.ReviewDelivery`
- Infrastructure/deploy: `azure.yaml`, `infra/main.bicep`, `scripts/azd-*.ps1`

## AI boundary and instruction precedence

Use this order of authority for repo-development behavior:

1. `.github/copilot-instructions.md`
2. `.digital-twin/overlay.md`
3. `.digital-twin/implementation-checklist.md`
4. `AGENTS.md` (this file)

Keep developer-facing AI assets separate from deployed runtime AI assets:

- Developer guidance: `.github/`, `.digital-twin/`
- Runtime assets shipped with app: `src/MessageScreener.Api/copilot-config/`

## Setup commands

Run from repo root:

```powershell
pwsh ./scripts/setup.ps1
```

Optional setup variants:

```powershell
pwsh ./scripts/setup.ps1 -Force
pwsh ./scripts/setup.ps1 -SkipCopilotRuntimeHook
pwsh ./scripts/setup-copilot-runtime.ps1 -Force
```

## Development workflow

Primary local build:

```powershell
dotnet build MessageScreener.slnx -warnaserror
```

Useful app-local run command:

```powershell
dotnet run --project src/MessageScreener.Api/MessageScreener.Api.csproj
```

VS Code tasks:

- `setup: communication twin`
- `build: solution`

## Testing instructions

There are currently no dedicated test projects checked into this repository (`*Test*.csproj` / `*Tests*.csproj` not present).

For now, use build as the required quality gate:

```powershell
dotnet build MessageScreener.slnx -warnaserror
```

When adding tests, follow `.digital-twin/implementation-checklist.md` quality expectations:

- unit tests for normal and failure paths
- integration tests for external boundaries
- telemetry assertions for critical workflows

## Code style and conventions

- Follow `.editorconfig` exactly (including CRLF/LF and indentation rules by file type).
- C# conventions are enforced through analyzer severity in `.editorconfig`; treat violations as errors.
- Baseline platform is `.NET 10` (`net10.0`).
- Prefer managed identity over secrets for external dependencies.
- Implement OpenTelemetry-first traces/metrics/logs.
- Use source-generated structured logging in runtime paths.
- Preserve explicit user control: no hidden auto-send behavior and no implicit side effects.

## Build and deployment

Deployment uses Azure Developer CLI (`azd`) with hooks defined in `azure.yaml`.

Typical deploy flow:

```powershell
azd auth login
azd env new <env-name>
azd env set AZURE_LOCATION eastus
azd env set MESSAGE_SCREENER_GITHUB_TOKEN <token>
azd env set MESSAGE_SCREENER_PUBLIC_BASE_URL https://<deployed-api-url>
azd up
```

Post-provision hook runs `scripts/azd-configure-m365-app.ps1`.
Post-deploy hook runs `scripts/azd-generate-teams-manifest.ps1`.

## Security and operations guardrails

- Use least-privilege permissions and document scope rationale.
- Keep secrets in approved secret stores only.
- Define failure modes, fallback behavior, and validation criteria for new features.
- Do not merge hidden automation paths that can send user-impacting messages.

## Commit and pull request rules for agents

- Commit continuously in small, atomic commits as each logical unit completes.
- Do not batch all work into one end-of-session commit.
- Before each commit, run the smallest relevant validation command(s) for changed scope.
- Keep commits focused; avoid mixing unrelated files or refactors.

Recommended commit format:

```text
<concise imperative summary>

<optional detail>
```

