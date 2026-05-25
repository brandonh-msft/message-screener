---
title: Copilot CLI Shim Shadowing in Dev Container
problem_type: integration_issue
category: integration-issues
module: devcontainer-bootstrap
component: copilot-cli
status: resolved
date: 2026-05-24
tags:
  - devcontainer
  - copilot-cli
  - workiq
  - bootstrap
  - path-resolution
---

## Problem
`setup.ps1` failed to generate the communication twin in the dev container because the shell-resolved `copilot` command pointed at a VS Code shim, not the standalone Copilot CLI.

## Symptoms
- Running `copilot --version` returned:
  - `Cannot find GitHub Copilot CLI`
  - `Install GitHub Copilot CLI? ['y/N']`
- Running `pwsh ./scripts/setup.ps1` either failed with Copilot CLI discovery errors or appeared to hang due to an unusable shim path.
- Rebuilding the dev container did not consistently fix behavior because command resolution still favored the shim in some shell contexts.

## What Didn't Work
1. Treating command existence as success:
- Earlier bootstrap logic accepted `command -v copilot` as proof the CLI was installed.
- This was insufficient because the VS Code shim also satisfies `command -v`.

2. Depending on `ghcp` semantics:
- `ghcp` was user alias behavior, not a guaranteed standalone binary contract.
- Using it as a toolchain assumption created inconsistent behavior across shells/sessions.

3. Coupling to `gh` Copilot extension:
- Installing/checking `gh-copilot` extension did not solve standalone `copilot` executable resolution for `setup.ps1` and plugin operations.

## Solution
### 1) Move to feature-first standalone Copilot CLI install
Updated devcontainer features to include official Copilot CLI feature directly:
- `ghcr.io/devcontainers/features/copilot-cli:1`

Kept GitHub CLI as a separate feature (without requiring `gh-copilot` extension coupling for setup).

### 2) Make bootstrap verify a usable standalone CLI, not just any `copilot` command
Created/used bootstrap verifier that:
- Prefers `/usr/local/bin/copilot` when present.
- Rejects known VS Code shim paths.
- Rejects candidates that output `Cannot find GitHub Copilot CLI` on `--version`.
- Fails fast with explicit remediation guidance when no usable CLI is found.

### 3) Use the resolved standalone binary path for plugin install steps
Updated WorkIQ and Digital Twin plugin scripts to use:
- `/usr/local/bin/copilot` when present
- otherwise fallback to PATH `copilot`

This reduced shim-shadowing risk during plugin operations.

### 4) Harden setup command resolution
Updated `scripts/setup.ps1` to:
- resolve and validate Copilot CLI candidates before invoking prompts
- reject VS Code shim path candidates
- prefer `/usr/local/bin/copilot`
- use non-interactive invocation flags (`--prompt`, `--silent`, `--output-format text`)

### 5) Clean up naming drift in devcontainer scripts
Renamed misleading legacy script names (`ghcp`-oriented names) to copilot-oriented names to match actual behavior and reduce future confusion.

## Why This Works
The root cause was command shadowing: a shim executable appeared earlier in PATH and looked valid from a basic existence check, but was not the real CLI. The fix explicitly verifies command usability and path provenance, then standardizes on the feature-installed standalone binary path. This removes ambiguity in both bootstrap and runtime setup flow.

## Prevention
1. Keep Copilot installation source single and deterministic:
- Use official `copilot-cli` devcontainer feature as the source of truth.

2. Verify capability, not presence:
- For critical CLI dependencies, validate `--version`/help behavior and reject known shim outputs.

3. Keep setup scripts non-interactive by default:
- Use explicit prompt flags suitable for automation.

4. Keep naming aligned with behavior:
- Avoid script names that imply aliases or command forms that are not guaranteed binaries.

5. Validate after changes:
- `bash -n` for modified scripts
- `bash .devcontainer/scripts/validate-devcontainer-config.sh`
- `dotnet build MessageScreener.slnx -warnaserror`

## Related References
- No related GitHub issues found for this repo with query: `copilot shim devcontainer setup` (as of 2026-05-24).
