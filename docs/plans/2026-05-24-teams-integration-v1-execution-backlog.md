---
date: 2026-05-24
topic: teams-integration-v1
type: execution-backlog
source_requirements: docs/brainstorms/2026-05-24-teams-integration-v1-requirements.md
source_plan: docs/plans/2026-05-24-teams-integration-v1-ce-plan.md
---

# Teams Integration V1 - Execution Backlog

## Planning Assumptions

- Target stack: .NET 10 on Azure Container Apps (always-on).
- Architecture guardrails: managed identity first, OpenTelemetry-first telemetry, source-generated logging, no-auto-send invariant.
- Estimation scale:
  - S = <= 1 day
  - M = 2-3 days
  - L = 4-6 days
  - XL = 7+ days

## Milestone Overview

- M1 Foundation + Ingestion Correctness
- M2 Drafting + Review UX
- M3 Reliability + Security Hardening
- M4 Pilot Readiness

## Backlog

| ID | Milestone | Work Item | Primary Owner | Estimate | Dependencies | Exit Criteria |
|---|---|---|---|---|---|---|
| T01 | M1 | Scaffold .NET 10 solution and projects for ingress, orchestrator, review, audit | Platform Eng | M | None | Build succeeds; solution structure supports CE-plan service boundaries |
| T02 | M1 | Wire baseline OpenTelemetry traces/metrics/logs and source-generated logging templates | Observability Eng | M | T01 | Telemetry emitted from startup and one sample request path; no warnings |
| T03 | M1 | Implement Graph webhook endpoint with validation and secure intake path | Integration Eng | M | T01 | Webhook validation handshake passes; ingress endpoint returns expected status codes |
| T04 | M1 | Implement trigger filter matrix (1:1 + group @mention only; ignore channels and excluded events) | Integration Eng | L | T03 | Automated tests cover all trigger/non-trigger classes defined by R1-R5c |
| T05 | M1 | Implement idempotency layer for duplicate create events | Backend Eng | M | T03 | Replay tests prove no duplicate cards/drafts/audit actions for same message identity |
| T06 | M1 | Implement recency-check service contract and interaction-log write/read model | Backend Eng | L | T04, T05 | Deterministic known/recent decisions and configurable window behavior validated |
| T07 | M2 | Implement spam vs genuine classifier shell with domain-level reclassification hooks | ML/Backend Eng | L | T04, T06 | Classifier path runs end-to-end; spam path always yields compact review action set |
| T08 | M2 | Implement drafting pipeline with confidence score and citation marker output | ML/Backend Eng | L | T06, T07 | Genuine drafts include confidence and citations or explicit no-source marker |
| T09 | M2 | Implement low-confidence SME suggestion payload (top 3 + rationale) | Backend Eng | M | T08 | Low-confidence responses always include bounded SME payload with rationales |
| T10 | M2 | Build Adaptive Card templates for genuine, low-confidence, and spam review flows | UX/Integration Eng | L | T08, T09 | Cards render in Teams personal bot chat with required actions and content |
| T11 | M2 | Implement card action handlers (Approve/Edit/Discard/Loop-in) with no-auto-send enforcement | Integration Eng | L | T10 | Approve/Edit open compose deep-link only; Loop-in prepares draft only; no send side effects |
| T12 | M2 | Implement deep-link payload overflow fallback (shortened compose + link to full context) | Integration Eng | M | T11 | Oversized payloads gracefully fallback and remain user-actionable |
| T13 | M3 | Implement personal-bot-chat unavailable fallback notification and recovery guidance | Integration Eng | M | T10 | Delivery failure triggers recoverable Teams fallback plus logged failure reason |
| T14 | M3 | Implement retry, timeout, and dead-letter strategy for delivery and callback paths | SRE/Backend Eng | L | T11, T13 | Retries are bounded/observable; dead-letter cases queryable with correlation IDs |
| T15 | M3 | Implement prompt-injection and sensitive-content guard stage with safe-failure behavior | Security Eng | L | T08 | Guard failures suppress draft display and emit guard-reason audit events |
| T16 | M3 | Implement append-only audit store model (immutable IDs/timestamps) | Security/Backend Eng | M | T01 | Audit writes are append-only and tamper-safe at application layer |
| T17 | M3 | Enforce audit retention default (90 days), tenant-configurable policy, and owner-only read access | Security/Backend Eng | M | T16 | Retention and access policy tests pass for personal-agent mode |
| T18 | M3 | Implement audit redaction policy for sensitive fields | Security Eng | M | T16 | Sensitive fields redacted in storage and retrieval views |
| T19 | M4 | Build E2E test harness for trigger/action/guard/fallback paths | QA/Backend Eng | L | T04-T18 | Deterministic E2E coverage for all major requirement paths |
| T20 | M4 | Build load/replay tests to verify 95/99 delivery SLO and idempotency under duplicate delivery | QA/SRE | M | T13, T14, T19 | 95/99 SLO evidence captured; duplicate replay remains idempotent |
| T21 | M4 | Add KPI instrumentation and pilot scorecard queries (latency, time-to-compose, SME relevance, traceability) | Observability Eng | M | T19 | Pilot dashboard and reports available with defined metrics |
| T22 | M4 | Pilot readiness review and go/no-go package | Tech Lead | M | T20, T21 | Explicit go/no-go decision with evidence against success criteria |

## Critical Path

1. T01 -> T03 -> T04 -> T05 -> T10 -> T11 -> T13 -> T19 -> T20 -> T22
2. T06 -> T07 -> T08 -> T09 -> T10
3. T15 -> T16 -> T17/T18 -> T19

## Definition of Ready (Per Task)

- Requirement mapping is explicit to at least one R-ID from source requirements.
- Dependency tasks are complete or parallel-safe.
- Test strategy for exit criteria is identified before implementation starts.

## Definition of Done (Per Task)

- Code compiles on .NET 10 with zero warnings introduced.
- Required telemetry and structured logs are present for the task path.
- Task-specific acceptance checks pass locally and in CI.
- Security-sensitive tasks include negative tests for expected failure behavior.

## Risk-Weighted Sequence Recommendation

- Start with correctness risks first: trigger matrix and idempotency (T04-T05).
- Then build UX/action semantics while no-auto-send invariant is enforced by tests (T10-T12).
- Then harden delivery failure and guard/audit behavior (T13-T18).
- Pilot only after measurable SLO and traceability proof (T20-T22).

## Suggested Sprint Cut (Two-Week Sprint 1)

- Target: complete M1 and partial M2.
- Commit candidate set: T01, T02, T03, T04, T05, T06, T10 skeleton.
- Sprint 1 goal: deterministic event intake and safe review-card shell in personal bot chat.
