---
title: Teams Message-Action Flow Replaced Graph Review Delivery Path with Truthful Status Reporting
problem_type: integration_issue
category: integration-issues
module: message-action-forwarding
component: teams-review-delivery
status: resolved
date: 2026-05-25
last_updated: 2026-05-26
tags:
  - teams
  - compose-extension
  - bot-connector
  - graph-removal
  - kiota-removal
  - delivery-status
---

## Problem
The Teams message-action forwarding path still carried Graph/Kiota-era assumptions, and action responses could report success even when review delivery was skipped or failed. This created trust and operability issues after the pivot away from tenant-admin-dependent Graph webhook flows.

## Symptoms
- Prior dependency chain included Graph/Kiota-related risk while the active runtime path should have used Bot Connector only.
- Teams action UX could return a success-like message even when the personal review destination was missing or delivery failed.
- Planning docs in the Teams integration track still referenced Graph webhook intake, diverging from the implemented message-action architecture.

## What Didn't Work
1. Treating the runtime path and user-facing action response as decoupled concerns:
- Intake and dedupe were correct, but delivery outcome was not surfaced to compose-action status text.

2. Leaving Graph-era naming and planning artifacts in place:
- Legacy naming and stale plan language reduced confidence in the pivot and made architecture intent harder to verify quickly.

3. Assuming source searches over build artifacts reflected active implementation:
- Ignored `bin` outputs could still contain old Graph references and trigger false alarms.

## Solution
### 1) Remove Graph-named runtime surface and keep Bot Connector transport explicit
- Renamed transport file to match actual implementation:
  - `src/MessageScreener.ReviewDelivery/BotConnectorMessageClient.cs`
- Kept `ITeamsMessageClient` backed by Bot Connector HTTP with managed identity token acquisition for:
  - `https://api.botframework.com/.default`

### 2) Add explicit review delivery outcomes and map them to compose-action responses
- Introduced delivery result model in:
  - `src/MessageScreener.ReviewDelivery/ReviewDeliveryService.cs`
  - `ReviewDeliveryStatus`
  - `ReviewDeliveryResult`
- Changed delivery API to return result status instead of fire-and-forget semantics.
- Updated API processing flow in:
  - `src/MessageScreener.Api/Program.cs`
- Added `InboundProcessingOutcome` and `CreateComposeExtensionStatusText(...)` so action responses now reflect actual state:
  - duplicate in-flight
  - duplicate completed
  - criteria not met
  - delivered
  - skipped (auto-reply disabled)
  - skipped (missing personal conversation)
  - skipped (missing service URL)
  - delivery failure

### 3) Align plans to message-action intake architecture
Updated Teams planning docs to remove Graph webhook intake assumptions and describe message-action intake semantics:
- `docs/plans/2026-05-24-teams-integration-v1-ce-plan.md`
- `docs/plans/2026-05-24-teams-integration-v1-execution-backlog.md`
- `docs/plans/2026-05-24-teams-integration-v1-github-issues.md`

## Why This Works
The active runtime now uses a single, coherent delivery model: Teams action invoke -> intake/dedupe -> conditional review delivery -> truthful status response. By removing Graph/Kiota coupling from the review-delivery path and surfacing actual delivery outcomes in compose-action responses, the user-visible behavior matches system reality, which eliminates false-success messaging and rebuilds operational trust.

## Prevention
1. Keep integration surfaces semantically aligned:
- Runtime transport names, dependency graph, and docs should all describe the same architecture.

2. Never return generic success for multi-step action flows:
- Always propagate downstream delivery state into user-facing action responses.

3. Treat stale build artifacts as non-authoritative during dependency verification:
- Exclude `bin/obj` when validating active source dependencies.

4. Add plan-code parity checks to reviews:
- For architecture pivots, review both runtime code and execution-plan docs in the same change window.

## Related References
- Existing related solution (moderate overlap):
  - `docs/solutions/integration-issues/automatic-dm-screening-requires-tenant-admin-graph-consent-2026-05-24.md`
- Companion follow-up:
  - `docs/solutions/integration-issues/teams-message-action-forwarding-invoke-cancellation-background-worker-2026-05-26.md`
- Commits:
  - `7dee1bc` Remove Graph SDK from Teams review delivery path
  - `0527c34` Align Teams action flow with truthful delivery status and remove Graph-named artifacts
  - `8bbb464` unslop: remove shallow ReviewDeliveryService constructor seam
- Key files:
  - `src/MessageScreener.Api/Program.cs`
  - `src/MessageScreener.ReviewDelivery/ReviewDeliveryService.cs`
  - `src/MessageScreener.ReviewDelivery/BotConnectorMessageClient.cs`
