---
date: 2026-05-24
topic: teams-integration-v1
type: ce-plan
source: docs/brainstorms/2026-05-24-teams-integration-v1-requirements.md
---

# Teams Integration V1 - CE Plan

## 1. Plan Intent

Translate Teams integration v1 requirements into a build-ready delivery plan with zero behavior invention during implementation.

Primary outcomes:
- Implement Teams 1:1 and @mention group-chat ingestion with deterministic trigger rules.
- Deliver personal-bot-chat review workflow with no-auto-send guarantees.
- Achieve predictable operational reliability and traceable security controls under .NET 10 and Azure Container Apps.

## 2. Delivery Architecture (V1)

Core services:
- Ingress API: Graph webhook validation and event intake.
- Event orchestrator: idempotent state machine from trigger -> classify -> draft -> review card.
- Drafting pipeline: retrieval/context assembly, confidence scoring, citations, guardrails.
- Review delivery service: personal bot chat card rendering and action handling.
- Interaction history service: recency-check reads/writes.
- Audit service: append-only event log and user-facing query endpoint.

Runtime baseline:
- .NET 10 worker/web host on Azure Container Apps (always-on).
- Managed identity for Azure dependencies.
- OpenTelemetry traces, metrics, and logs wired at startup.
- Source-generated logging for hot-path decisions and failures.

## 3. Workstreams

- W1 Triggering and idempotent ingestion (R1-R5d)
- W2 Decisioning and drafting quality (R6-R10)
- W3 Teams review UX and action semantics (R11-R15a)
- W4 Reliability and failure-mode handling (R11a + success SLA)
- W5 Security, audit, and compliance controls (R16-R20)
- W6 Validation and pilot readiness (success criteria)

## 4. Backlog by Workstream

## 4.1 W1 Triggering and Idempotent Ingestion

Deliverables:
- Teams webhook endpoint with subscription validation and event intake.
- Trigger filter:
  - include 1:1 and group chats
  - group requires explicit @mention
  - exclude channels
  - exclude edits/deletes/reactions/system/bot/self messages
  - exclude attachment-only messages
- Idempotency layer keyed by stable message identity to suppress duplicate effects.
- Recency evaluator and interaction-log update contract.

Acceptance tests:
- Duplicate event delivery does not create duplicate cards/drafts/audit actions.
- Non-trigger event classes are ignored deterministically.
- Group chat without @mention is ignored.

## 4.2 W2 Decisioning and Drafting Quality

Deliverables:
- Genuine vs spam/automated classifier with domain-level feedback loop.
- Confidence scoring with configurable threshold.
- Draft generation with citations and explicit no-source marker.
- Low-confidence path with top-3 SME suggestion payload.
- Guarded no-auto-send path invariant checks in all code paths.

Acceptance tests:
- Spam path always yields compact card actions, never silent drop.
- Low-confidence cases include SME suggestions and rationale text.
- No draft path can send directly.

## 4.3 W3 Teams Review UX and Action Semantics

Deliverables:
- Personal bot chat card templates:
  - genuine/high-confidence
  - low-confidence + SME actions
  - spam compact card
- Action handlers:
  - Approve -> Teams compose deep-link prefill
  - Edit -> Teams compose deep-link for manual edit
  - Discard -> close workflow
  - Loop-in SME -> compose-ready draft content only (no chat creation/send)
- Long payload fallback (R15a): shortened prefill + link back to full context in bot chat.

Acceptance tests:
- Cards route only to personal bot chat.
- Loop-in cannot send or create chats automatically.
- Deep-link fallback behavior activates for oversize content.

## 4.4 W4 Reliability and Failure Modes

Deliverables:
- Delivery fallback when personal bot chat unavailable:
  - recoverable Teams notification
  - re-activation guidance
  - explicit failure reason logging
- Retry policy and dead-letter handling for card delivery and action callbacks.
- SLO instrumentation for 95th/99th review-card delivery latency.

Acceptance tests:
- Chat-unavailable scenarios surface recoverable fallback notification.
- Delivery and callback retries are bounded and observable.
- SLA metrics are emitted and queryable.

## 4.5 W5 Security, Audit, and Compliance

Deliverables:
- Prompt-injection guard stage prior to any draft display.
- Guard-failure safe review message path with guard-reason audit event.
- Append-only audit model with immutable IDs/timestamps.
- Retention default 90 days and tenant-configurable controls.
- Owner-only audit read model for personal-agent mode.
- Redaction policy for sensitive audit fields.

Acceptance tests:
- Guard failures suppress draft content and log reason.
- Audit retention and access controls enforced by policy.
- Sensitive fields are redacted in stored/displayed audit records.

## 4.6 W6 Validation and Pilot Readiness

Deliverables:
- End-to-end test harness for trigger classes, routing classes, and action flows.
- Replay tests for duplicate webhook deliveries.
- Pilot scorecards for:
  - card delivery latency
  - time-to-compose readiness
  - auto-send violations
  - SME relevance feedback rates
  - audit trace completeness

Acceptance tests:
- 95/99 latency goals demonstrated in pre-pilot load runs.
- 0 auto-send violations in test and pilot.
- Minimum 50 feedback samples available for SME relevance KPI.

## 5. Integration Sequence

Checkpoint A: Ingestion + filter correctness
- W1 complete with deterministic trigger matrix and idempotency.

Checkpoint B: Drafting + confidence split
- W2 complete with classifier, confidence, citations.

Checkpoint C: Teams card workflow end-to-end
- W3 complete with all action semantics including loop-in constraints.

Checkpoint D: Reliability and security hardening
- W4 + W5 complete with fallback and guard/audit controls.

Checkpoint E: Pilot readiness
- W6 complete with KPI instrumentation and acceptance evidence.

## 6. Definition of Done

Functional:
- All requirements R1-R20 from source doc are implemented and validated.
- Behavior in trigger/action edge cases is deterministic and tested.

Reliability:
- Delivery fallback exists for personal-bot-chat unavailable scenarios.
- Duplicate event processing is idempotent under replay/load tests.
- SLA telemetry meets defined thresholds in pre-pilot conditions.

Security and compliance:
- Guard failures are safe by default and auditable.
- Audit store is append-only with policy-driven retention and redaction.
- Owner-only audit view is enforced.

## 7. Open Technical Questions for Planning Execution

- Q1 [R4,R5]: Recency data model and lookup algorithm for group contexts.
- Q2 [R6-R8]: Confidence calibration strategy and threshold governance.
- Q3 [R11-R15a]: Deep-link payload sizing rules and truncation strategy.
- Q4 [R18-R19]: Tenant-bound audit backend and tamper-evidence approach.

## 8. Immediate Next Actions

1. Scaffold .NET 10 solution skeleton for ingress/orchestration/review/audit services.
2. Implement W1 trigger matrix and idempotency first (highest correctness risk).
3. Stand up OTel + source-generated logging baseline before feature implementation.
4. Implement W3 review action handlers in parallel with W2 drafting pipeline.
5. Add reliability/security gates in CI tied to W4/W5 acceptance tests.
