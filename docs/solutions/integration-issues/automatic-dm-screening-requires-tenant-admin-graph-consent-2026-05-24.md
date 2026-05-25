---
title: Automatic DM Screening Requires Tenant Admin Graph Consent
problem_type: integration_issue
category: integration-issues
module: graph-webhook-ingress
component: microsoft-graph-permissions
status: resolved
date: 2026-05-24
tags:
  - microsoft-graph
  - teams
  - permissions
  - tenant-admin
  - webhook
  - dm-screening
---

## Problem
The project requires automatic screening of all inbound Teams direct messages, but the deploying user does not have tenant admin rights. Provisioning initially assumed Graph app-role assignment could always be performed during `azd` hooks.

## Symptoms
- Deployment users without Entra admin rights cannot grant Graph application roles.
- Provisioning failed when the postprovision script attempted app-role assignment.
- Automatic webhook-based DM screening could not be activated in non-admin environments.

## What Didn't Work
1. Treating Graph app-role assignment as universally available:
- The hook attempted to assign `Chat.Read.All` and `Chat.ReadWrite.All` unconditionally.
- In user-scoped (non-admin) contexts, Graph returned privilege errors.

2. Failing provision on permission-grant error:
- `postprovision` with strict failure behavior blocked deployment for valid non-admin users.
- This prevented use of still-functional bot command paths.

## Solution
### 1) Make Graph permission setup best-effort for non-admin contexts
Updated `scripts/azd-configure-graph-permissions.ps1` to:
- detect common Graph privilege errors (`Insufficient privileges`, `Authorization_RequestDenied`, authorization-denied variants)
- emit warnings and return success for those cases
- continue throwing for genuine non-permission failures

### 2) Make the azd postprovision hook non-blocking
Updated `azure.yaml`:
- `hooks.postprovision.continueOnError: true`

This keeps provisioning usable for non-admin users while preserving automation for admin-capable tenants.

### 3) Document capability boundaries clearly
Updated `README.md` to state:
- non-admin users can deploy and use bot-command flows (for example `help`)
- full Graph webhook auto-subscription and Graph-based automatic screening still require admin-granted app permissions

## Why This Works
Automatic background DM screening relies on Graph app permissions that require tenant admin consent in enterprise tenants. The fix aligns runtime/deploy behavior with that platform boundary:
- non-admin deployment succeeds and exposes features that do not require tenant-wide app-role grants
- admin-capable tenants still get automated app-role assignment through the same hook

## Prevention
1. Treat tenant-level identity operations as capability-conditional:
- Design hooks to degrade gracefully when admin privileges are absent.

2. Keep feature gating explicit:
- Distinguish manual/bot-command workflows from background Graph subscription workflows in config and docs.

3. Preserve deploy success for partial capability environments:
- Avoid hard failures for optional-but-enhancing platform integrations.

4. Validate after permission-flow changes:
- Parse check PowerShell hook scripts.
- `dotnet build MessageScreener.slnx -warnaserror -v minimal`.

## Related References
- Commit introducing best-effort non-admin flow: `50cd2c8`.
- Related files:
  - `scripts/azd-configure-graph-permissions.ps1`
  - `azure.yaml`
  - `README.md`
