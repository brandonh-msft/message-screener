---
date: 2026-05-24
topic: message-action-forwarding-v1
type: ce-plan
source: docs/brainstorms/2026-05-24-message-action-forwarding-v1-requirements.md
---

# Message Action Forwarding V1 - CE Plan

## 1. Plan Intent

Deliver a non-admin-safe V1 that lets the user forward a specific Teams message from message Actions to Message Screener, then review and manually send a response.

Primary outcomes:
- Replace admin-dependent background Graph subscription intake as the required path.
- Add action-invoked intake for 1:1 and group chats with deterministic fallback behavior.
- Preserve no-auto-send guarantees with owner-only review delivery.

## 2. Delivery Architecture (V1)

Core path:
- Teams message action invocation -> forward payload intake endpoint.
- Intake + idempotency + trigger evaluation.
- Draft generation and review card delivery to personal bot chat.
- Approve/Edit action handling with compose-targeting best effort and copy-ready fallback.

Runtime baseline:
- .NET 10 API host on Azure Container Apps.
- Managed identity remains default for Azure dependencies.
- OpenTelemetry traces/metrics/logs for intake and review flow.
- Source-generated logging for action invocation, dedupe, fallback routing, and audit outcomes.

## 3. Workstreams

- W0 Contract resolution and platform spike (R6a, R4a-R4ab)
- W1 Teams capability and app package changes (R1, R1a, R8)
- W2 Forwarding intake API and contracts (R2-R4c)
- W3 Screening orchestration and review delivery (R5-R7)
- W4 Fallback UX and action semantics (R6a, R8a-R8c)
- W5 Security, privacy, and audit controls (R9-R11)
- W6 Validation matrix and pilot readiness (success criteria)

## 4. Backlog by Workstream

## 4.1 W0 Contract Resolution and Platform Spike

Deliverables:
- Confirm per-client compose-targeting feasibility for desktop, web, and mobile.
- Define explicit per-client fallback for unsupported compose-targeting surfaces.
- Confirm unresolved sender-key policy behavior for dedupe, audit labels, and telemetry.

Acceptance tests:
- A signed compatibility matrix exists with supported/unsupported compose-targeting cells.
- Unresolved identity behavior is documented and testable.

## 4.2 W1 Teams Capability and App Package Changes

Deliverables:
- Update Teams app capabilities so Message Screener appears in message Actions for 1:1 and group chat contexts.
- Extend manifest generation script to include required action capability configuration.
- Preserve installability across desktop/web/mobile with fallback guidance where capability is absent.

Acceptance tests:
- Action entry point appears on supported clients after package upload.
- Unsupported clients route users to documented fallback flow.

## 4.3 W2 Forwarding Intake API and Contracts

Deliverables:
- Add action-forward intake endpoint (new API contract) accepting source message context and sender profile payload.
- Extend contracts to carry sender display name plus best-available sender identity key and unresolved-key state.
- Implement 24-hour idempotency keyed by source message identity plus owner context.
- Implement duplicate in-flight status and failed-run one-click requeue semantics.

Acceptance tests:
- Duplicate forwards for the same source message return existing run/result.
- In-flight duplicate requests return pending status, not new runs.
- Failed duplicate requests expose requeue path.

## 4.4 W3 Screening Orchestration and Review Delivery

Deliverables:
- Reuse existing intake and composer pipeline for action-forwarded payloads.
- Ensure result delivery remains personal-chat-only.
- Preserve no-auto-send invariant in all action paths.

Acceptance tests:
- Accepted forwards produce a personal-chat result card.
- No path auto-sends content to original chat.

## 4.5 W4 Fallback UX and Action Semantics

Deliverables:
- Implement Approve/Edit handlers:
  - best-effort prefilled compose targeting original conversation when supported.
  - copy-ready response fallback with explicit paste guidance when unsupported.
- Implement forward failure guidance and retry instructions.
- Implement manual-paste template path when source context cannot be captured.

Acceptance tests:
- Unsupported compose-targeting clients show copy-ready fallback deterministically.
- Failed forwarding attempts expose actionable retry guidance.

## 4.6 W5 Security, Privacy, and Audit Controls

Deliverables:
- Emit immutable audit event per forward action with event ID, timestamp, and source chat context.
- Persist only approved sender fields (display name + identity key/unresolved state).
- Apply redaction policy to logs/displays and enforce owner-only access to result/audit history.

Acceptance tests:
- Audit records are immutable and traceable per forward request.
- Sensitive sender profile fields are not persisted.
- Access control blocks non-owner reads in personal-agent mode.

## 4.7 W6 Validation Matrix and Pilot Readiness

Deliverables:
- Client compatibility test matrix for desktop/web/mobile action behavior.
- End-to-end tests for supported flow, fallback flow, duplicate/idempotency, and requeue behavior.
- SLO instrumentation and scorecards for latency and acceptance metrics.

Acceptance tests:
- p95 action-to-confirmation and result-card latency targets are met.
- Forward acceptance and fallback success targets meet requirement thresholds.
- 0 auto-send violations in test and pilot.

## 5. Integration Sequence

Checkpoint A: Contract lock
- W0 complete with signed compose-targeting and unresolved-identity contracts.

Checkpoint B: Action surface availability
- W1 complete and package validated on supported clients.

Checkpoint C: Intake correctness
- W2 complete with idempotency and requeue semantics.

Checkpoint D: End-to-end review loop
- W3 + W4 complete with deterministic fallback behavior.

Checkpoint E: Compliance and pilot evidence
- W5 + W6 complete with audit/access controls and KPI evidence.

## 6. Definition of Done

Functional:
- Requirements R1-R11 implemented with fallback behavior matching per-client matrix.
- Forwarding works without tenant admin consent as required intake path.

Reliability:
- Duplicate forwards are idempotent for 24 hours.
- In-flight duplicate and failed-run requeue semantics are deterministic.

Security and compliance:
- Sender data retention follows approved minimal schema.
- Owner-only access and redaction rules are enforced.
- No-auto-send invariant holds across all tested paths.

## 7. Open Technical Questions for Planning Execution

- Q1 [R1,R1a,R8]: Exact Teams capability schema and version constraints for message Actions by client.
- Q2 [R6a]: Preferred compose-targeting mechanism and payload limits across clients.
- Q3 [R4a-R4ab]: Data model for unresolved sender-key state and dedupe key composition.
- Q4 [R9-R11]: Storage backend and retention enforcement strategy for forward-action audit records.

## 8. Immediate Next Actions

1. Run W0 platform spike and publish compatibility matrix.
2. Draft and approve the new action-forward API contract in MessageScreener.Contracts.
3. Implement W2 idempotency and duplicate/requeue behavior before UI polish.
4. Implement W1 Teams capability changes and validate package behavior on desktop/web/mobile.
5. Add W5 audit/redaction/owner-access controls before pilot onboarding.
