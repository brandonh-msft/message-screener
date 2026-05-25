---
date: 2026-05-24
topic: message-action-forwarding-client-compatibility
type: compatibility-matrix
source: docs/plans/2026-05-24-message-action-forwarding-v1-ce-plan.md
---

# Message Action Forwarding Client Compatibility Matrix

## Purpose

Track the W0/W1 contract for Teams message-action forwarding across client surfaces before broader implementation depends on unsupported behavior.

## Forwarding Action Contract

- Primary action: `Forward to Message Screener`
- Command ID: `forwardToMessageScreener`
- Intake path: `/api/messages` invoke handling for `composeExtension/submitAction`
- Required fallback when unsupported: route user to personal Message Screener chat with copy-ready or manual-paste guidance
- No-auto-send remains absolute on every surface

## Current Support Matrix

| Capability | Desktop | Web | Mobile | Notes |
|---|---|---|---|---|
| Message action entry point visible | Pending validation | Pending validation | Pending validation | Must verify app package + Teams client support |
| `composeExtension/submitAction` invoke delivered to bot | Pending validation | Pending validation | Pending validation | Current API handler supports command ID validation |
| Source message payload includes `messagePayload` | Pending validation | Pending validation | Pending validation | Handler supports top-level and nested `data.messagePayload` |
| Compose-targeting for Approve/Edit | Unknown | Unknown | Unknown | Product contract is best-effort with fallback |
| Copy-ready fallback from personal screener chat | Planned | Planned | Planned | Required if compose-targeting unsupported |

## Validation Steps

1. Upload the generated Teams app package after `azd deploy`.
2. On each client surface, open the Actions menu on a 1:1 message and a group-chat message.
3. Confirm `Forward to Message Screener` appears.
4. Invoke the action and confirm `/api/messages` receives `composeExtension/submitAction`.
5. Confirm the user receives either:
- a successful forward confirmation and personal screener result card, or
- a deterministic fallback message with next-step guidance.
6. Record whether compose-targeting is supported for Approve/Edit on that client.

## Decision Log

- 2026-05-24: Compose-targeting is treated as best-effort, not required for all clients.
- 2026-05-24: Unsupported or incomplete payload shapes must fail safely and return fallback guidance rather than silently accepting malformed invokes.

## Exit Criteria For W0/W1

- Desktop, web, and mobile each have an explicit status for action visibility.
- Desktop, web, and mobile each have an explicit status for invoke delivery shape.
- Compose-targeting support is marked supported or unsupported per client.
- Fallback path is validated anywhere compose-targeting or action visibility is unsupported.
