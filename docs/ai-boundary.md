# AI Boundary

Message Screener has two separate AI surfaces.

## Developer Surface

These files help developers and reviewers work in this repo:

- `.github/copilot-instructions.md`
- `.digital-twin/`
- `.github/agents/`
- `.github/prompts/`
- `.github/skills/`

Use these for repo authoring, review, planning, and local development assistance.

## Product Runtime Surface

These files are shipped with the service and used by the deployed product harness:

- `src/MessageScreener.Api/copilot-config/`
- `src/MessageScreener.Api/copilot-config/.mcp.json`

The repo-root `.mcp.json` is the developer seed for product MCP configuration and is copied into `src/MessageScreener.Api/copilot-config/.mcp.json` by `scripts/setup-copilot-runtime.ps1`.

The developer seed and product runtime MCP files intentionally differ by environment token source:

- Developer seed `.mcp.json` uses `${env:GITHUB_TOKEN}`
- Runtime MCP files use `${env:MESSAGE_SCREENER_GITHUB_TOKEN}`

Use these for the deployed GHCP SDK harness, runtime message drafting, readiness checks, and product behavior.

## Setup-Time Bridge

`scripts/setup.ps1` bridges the two surfaces.

It uses developer-side tooling to generate the operating user's communication twin from `scripts/prompts/communication-twin.workiq.prompt.md`, then writes runtime artifacts into `src/MessageScreener.Api/copilot-config/` and `src/MessageScreener.Api/copilot-config/prompts/communication-twin.prompt.md`.

The setup prompt used to generate the twin is a bootstrap artifact, not a deployed product asset.
The intermediate JSON response is discarded after the prompt is generated.