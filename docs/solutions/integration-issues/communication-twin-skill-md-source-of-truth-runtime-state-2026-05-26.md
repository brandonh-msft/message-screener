---
title: Communication twin skill becomes the runtime source of truth
problem_type: integration_issue
track: bug
category: integration-issues
module: communication-twin
component: setup/runtime bridge
status: resolved
date: 2026-05-26
tags:
  - copilot
  - skill-generation
  - runtime-config
  - setup
---

## Problem

The communication-twin flow had a persistent JSON artifact alongside `SKILL.md`, which made the runtime source of truth ambiguous.

## Symptoms

- `communication-twin.json` existed under `src/MessageScreener.Api/copilot-config/skills/communication-twin/`
- readiness logic checked the JSON file instead of the skill
- setup left behind an intermediate persona file that looked runtime-relevant

## What Didn't Work

- Treating the generated JSON as a shipped runtime asset
- Validating persona readiness by reading the JSON directly
- Leaving the transient Copilot CLI output in the repo after skill generation

## Solution

The runtime now uses `SKILL.md` as the communication-twin source of truth.

Key changes:

```csharp
public string CommunicationTwinSkillPath { get; init; } = "copilot-config/skills/communication-twin/SKILL.md";
```

```csharp
string? skillContent = await ghcpAgentHarness.GetCommunicationTwinSkillContentAsync(cancellationToken);
```

`scripts/setup.ps1` now generates the skill and deletes the temporary JSON payload after parsing it.

## Why This Works

`SKILL.md` is the artifact the GHCP harness actually loads, so it is the right thing to validate and ship. Removing the extra JSON copy prevents drift between setup output and runtime behavior.

## Prevention

- Keep the runtime contract centered on the file the harness consumes.
- Delete bootstrap-only outputs after they are transformed into shipped artifacts.
- Make readiness checks validate the deployed path, not a temporary generator output.

