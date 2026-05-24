---
date: 2026-05-24
topic: cold-contact-screener-agent
type: ce-plan
source: docs/brainstorms/2026-05-24-cold-contact-screener-agent-requirements.md
---

# Cold Contact Screener Agent - CE Plan

## 1. Plan Intent

This plan translates the approved brainstorm requirements into an executable build-and-validate plan for a standalone v1 service.

North-star outcomes:
- Intercept only true cold-contact inbound messages with phased channels (v1 Teams DM, v2 adds Outlook direct To:), classify, and draft within SLA.
- Never auto-send. All outbound content remains user-approved drafts/deep-links.
- Deliver low-friction review and escalation (SME loop-in) with explicit confidence and citations.
- Enforce tenant-bound security, least privilege, and immutable auditability from day one.
- Implement all code on .NET 10 with managed identity-first auth, OpenTelemetry-first telemetry, and source-generated structured logging.

## 2. Delivery Model

Delivery is intentionally phased by channel:

- **v1 (Teams-first):** Teams DM ingestion, Teams-native draft/review loop, Teams interaction log, Teams reliability and security hardening.
- **v2 (Email expansion):** Outlook direct-To ingestion, Outlook draft handling, cross-channel recency and interaction history.

Execution runs as parallel workstreams per phase with explicit phase-gate checkpoints.

Parallel workstreams:
- W1 Triggering + identity + interaction log
- W2 Classification + urgency + feedback learning
- W3 Knowledge indexing + retrieval + citation pipeline
- W4 Style model + generation + confidence
- W5 Notification UX + draft actions + fallback channel
- W6 SME ranking + loop-in forward workflow
- W7 Reliability + subscription lifecycle + gap recovery
- W8 Security, permissions, secrets, and audit
- W9 Validation harness, telemetry, and launch readiness

Implementation baseline:
- .NET 10 SDK and modern C# language features
- Managed identity first for Azure dependencies
- OpenTelemetry traces/metrics/logs wired at startup
- Source-generated logging patterns for hot paths and security events

## 3. Architecture Baseline

Runtime services:
- Ingestion service: Graph webhook endpoints for inbound Teams updates in v1; v2 adds mail and sent-item updates.
- Orchestration service: per-message state machine (cold-check -> classify -> generate -> notify).
- Retrieval service: connectors and indexed corpora (M365 + GitHub).
- Style service: periodic style profile build + on-demand refresh.
- Notification service: Teams Adaptive Card delivery in v1; v2 adds email fallback and email-channel artifacts.
- SME service: candidate fetch, scoring, rationale generation.
- Audit service: append-only event writer + user-facing query API/UI hook.

Primary stores (tenant-bound):
- Interaction log store (canonical_contact_id, channel, timestamp, direction).
- Knowledge index store (chunk, source metadata, sensitivity label, ACL context).
- Style profile store (feature vectors, update timestamp, exclusions applied).
- Preferences store (recency window, confidence threshold, AI disclosure toggle, fallback channel).
- Audit log store (immutable append-only).

External dependencies:
- Microsoft Graph (v1: Teams Chat, Users/People, Sites/Files; v2 adds Mail/SentItems).
- Teams App/Bot for proactive Adaptive Card delivery.
- GitHub read-only integration for repo/ownership signals.
- Secrets manager (Azure Key Vault or equivalent) for non-M365 tokens.

## 4. Requirement-to-Workstream Mapping

Phase tags:
- **v1 core:** Teams-only capability set
- **v2 add-on:** Email and cross-channel expansion

- W1: R1, R2, R2a, R2b, R3, R18, R18a, R19, R20 (v1 Teams subset first, v2 full cross-channel)
- W2: R4, R4a, R4b, R4c
- W3: R5a, R6, R-SEC-6 (v1 Teams corpus first, v2 adds email corpus)
- W4: R5, R7, R7a, R8, R8a, R8b, R15, R16, R17 (v1 style on Teams corpus, v2 style on Teams+email)
- W5: R9, R10, R11, R12, R13, R14, R22 (v1 Teams action semantics, v2 email semantics)
- W6: R21, R22, R23, R23a, R23b, R23c, R23d
- W7: R24, R25, R26, R27
- W8: R-SEC-1, R-SEC-2, R-SEC-3, R-SEC-4, R-SEC-5, R-SEC-7
- W9: Success Criteria verification, operational readiness, trial instrumentation

## 5. Execution Backlog

Phase sequencing:
- **Phase 1 (v1):** implement W1-W9 for Teams-only operation.
- **Phase 2 (v2):** extend existing W1-W9 assets to email and cross-channel behavior.

## 5.1 W1 Triggering, Identity, and Interaction Log

Deliverables:
- Graph subscriptions for:
  - Inbound Teams direct messages (v1)
  - Outbound Teams messages for outbound log updates (v1)
  - Inbound email where user appears in To: (v2)
  - SentItems changes for outbound log updates (v2)
- Canonical identity resolver:
  - v1: Teams identity normalization to canonical contact ID
  - v2: SMTP <-> AAD UPN mapping via proxyAddresses lookup
  - Unresolved identity path and Adaptive Card warning payload
- Recency evaluator with configurable window default 6 months (v1 Teams-only, v2 cross-channel)
- One-time activation backfill job and backfill status signaling

Acceptance tests:
- v1: sender contacted in Teams within window is ignored.
- v2: sender contacted in either channel within window is ignored in both channels.
- Unresolved sender is treated cold and warning appears on card.
- Outbound non-agent replies update interaction log for in-scope channels.

## 5.2 W2 Classification and Urgency

Deliverables:
- Cold-message classifier (Genuine vs Likely Spam/Automated)
- Compact spam card model with rationale + Discard + Reclassify
- Reclassification feedback store by sender domain
- Secondary urgency classifier and escalated notification path

Acceptance tests:
- Spam classification never silently drops a message.
- Reclassify reruns full genuine pipeline.
- Genuine + urgent triggers elevated notification mode.

## 5.3 W3 Knowledge Indexing and Citation Pipeline

Deliverables:
- Connectors for:
  - Teams history (bounded by configurable retention, v1)
  - Outlook history (v2)
  - SharePoint/OneDrive pinned sites
  - GitHub repos (recently contributed set)
- Retrieval contract requiring source metadata per chunk
- Inline citation formatter for draft rendering
- Unverified claim tagging policy
- Sensitivity gate that blocks Confidential+ chunks for external/unverified recipients

Acceptance tests:
- Every sourced factual claim has an attached citation.
- Unsourced factual claims are labeled [unverified].
- Confidential+ chunk inclusion is blocked and logged for prohibited recipient classes.

## 5.4 W4 Draft Generation, Style, Confidence

Deliverables:
- Style profile trainer from Teams corpus with exclusion scopes and label defaults (v1); v2 extends to email+Teams corpus
- Weekly auto-refresh scheduler + manual refresh endpoint/action
- Draft generation pipeline using:
  - inbound ask
  - top retrieval context
  - style profile features
- Confidence scoring [0.0-1.0] with configurable threshold default 0.6
- Timeout/failure fallback card path with user-safe manual compose escape hatch
- AI disclosure footer default ON with persisted preference

Acceptance tests:
- Drafts match expected style rubric against held-out historical samples.
- Confidence label and score displayed and threshold respected.
- No automatic send path exists in any code path.

## 5.5 W5 Notification UX and User Actions

Deliverables:
- Genuine card template including preview, full draft, confidence, citations, actions
- Low-confidence card extension with top 3 SMEs and loop-in actions
- Spam compact card template
- Action handlers:
  - v1 Approve: Teams compose deep-link
  - v2 Approve: Outlook Drafts placement for email; Teams compose deep-link for Teams
  - Edit: open native compose/edit path for in-scope channel
  - Discard: delete draft and dismiss context
- v1 fallback: configurable Teams-native retry/escalation path
- v2 fallback: configurable email summary fallback

Acceptance tests:
- Teams Approve uses compose deep-link only.
- v2: Email Approve always lands in Drafts, never Outbox.
- Card schemas render and function on desktop and mobile Teams clients.

## 5.6 W6 SME Routing and Loop-in Flow

Deliverables:
- Candidate fetch in parallel from:
  - People/org directory
  - related Teams threads (v1)
  - related Teams and email threads (v2)
  - code ownership/review activity
  - document authorship
- Scoring model with explicit rationale text per candidate
- Channel-specific rendering:
  - Teams Contact Info card links
  - Email mailto links (v2)
- Loop-in forward draft as separate object from reply draft
- Forward preview, editable content, and default attachment stripping

Acceptance tests:
- Low-confidence path returns 0-3 ranked SMEs with rationale.
- Loop-in creates separate draft entity and never sends automatically.
- Attachments are omitted by default unless user re-attaches.

## 5.7 W7 Reliability and Monitoring Continuity

Deliverables:
- Subscription renewal scheduler with pre-expiry guard windows
- Monitoring-paused alert + Re-activate action on renewal failure
- Gap-recovery delta polling on restart/outage
- End-to-end latency budgets and watchdog alarms:
  - <= 10 min high-confidence pipeline
  - <= 15 min low-confidence with SME lookup

Phase-specific reliability controls:
- v1 hardens Teams subscription churn and gap recovery first.
- v2 adds mail subscription lifecycle and cross-channel reconciliation checks.

Acceptance tests:
- Forced subscription expiry triggers pause notification and recovery flow.
- Recovery backfills missed messages before live mode resumes.
- SLA dashboards expose percentile latencies and timeout causes.

## 5.8 W8 Security, Permissions, and Audit

Deliverables:
- Prompt-injection-hardened prompt templates with strict role/content delimiters
- Token strategy document and implementation:
  - delegated refresh token storage encryption + re-auth flow
  - or application permission model with admin-consent traceability
- GitHub token storage/rotation/revocation through secrets manager only
- Permission scope ledger mapping each scope to requirement IDs
- Phase-aware permission tiers:
  - v1 minimal Teams-first scope set
  - v2 expanded mail/cross-channel scope set with explicit approval gate
- Tenant-bound data residency guarantees and access policy enforcement
- Immutable append-only audit log with user-readable access

Acceptance tests:
- Security review confirms no plaintext token persistence.
- Scope review rejects any permission without requirement mapping.
- Audit replay reconstructs full lifecycle of sampled messages.

## 5.9 W9 Validation, Trial Instrumentation, and Launch

Deliverables:
- Synthetic and recorded-message test harness for all major flows (v1 Teams suite, v2 expanded suite)
- Structured telemetry for:
  - time-to-card
  - confidence distribution
  - edit-distance and edit-class feedback
  - classifier precision/recall with reclassification feedback
- 2-week pilot protocol for >=20 approved genuine-contact drafts
- Go/no-go checklist aligned to success criteria and security gates

Acceptance tests:
- Pilot captures no-edit/minor/major outcomes and median edit distance.
- Zero auto-send violations in test and pilot logs.
- External-recipient confidentiality guard remains at 100% block rate.

## 6. Integration Checkpoints (Phased Delivery)

Checkpoint A (v1): Teams core message lifecycle wired
- W1 + W2 + W5 minimal vertical slice for Teams cold detection, classification, and carding.

Checkpoint B (v1): Teams draft quality and provenance
- W3 + W4 integrated with citation and confidence rendering for Teams scenarios.

Checkpoint C (v1): Teams low-confidence resolution path
- W6 integrated with W4/W5 for SME suggestions and Teams loop-in draft flow.

Checkpoint D (v1): Teams production hardening
- W7 + W8 + W9 complete with SLA, audit, and launch readiness evidence for Teams-only scope.

Checkpoint E (v2): Email expansion
- Extend W1/W3/W4/W5/W7 to email and validate cross-channel parity with v1 guarantees.

## 7. Definition of Done

Functional DoD:
- v1 DoD: all Teams-scoped behaviors from R1-R27 and R-SEC-1-R-SEC-7 implemented/tested.
- v2 DoD: all remaining email and cross-channel behaviors implemented/tested.
- No path can transmit messages without explicit user action.
- In-scope channel-specific action semantics match requirements exactly.

Operational DoD:
- Subscription lifecycle and outage recovery proven in chaos/restart tests.
- User-facing audit access available with >=90-day retention.
- Latency targets met under normal operating conditions.

Security DoD:
- Least-privilege scope matrix approved.
- Secrets and tokens stored only in approved encrypted stores.
- Prompt-injection and data-leakage mitigations validated.

## 8. Open Technical Spikes (From Deferred Questions)

Run these as time-boxed spikes in parallel with implementation; each outputs an ADR:
- S1 Chunking + embeddings strategy for mixed conversational + document corpora.
- S2 Interaction-log backend selection for single-user, always-on behavior.
- S3 Teams proactive delivery architecture decision (Bot Service vs manifest path).
- S4 SharePoint retrieval feasibility and whether separate indexing layer is required.
- S5 SME ranking model calibration across people, docs, and code signals.
- S6 Token model decision (delegated refresh vs application permissions).
- S7 .NET 10 host architecture baseline (worker vs ASP.NET minimal host) with OTel/logging conventions aligned to digital-twin preferences.

ADR output template:
- Context
- Options considered
- Decision
- Consequences
- Requirement impact references

## 9. Risks and Mitigations

- Risk: Graph subscription churn (Teams 60-minute expiry) causes blind spots.
  - Mitigation: aggressive pre-renewal, renewal jitter, and gap backfill via delta queries.
- Risk: Over-classification of genuine messages as spam.
  - Mitigation: human-visible spam card, reclassify loop, domain-level adaptive feedback.
- Risk: Style mimicry quality is poor early.
  - Mitigation: minimum corpus thresholds, style fallback presets, continuous refresh.
- Risk: Permission scope pushback from tenant admins.
  - Mitigation: strict requirement-to-scope ledger and optional reduced-capability mode.
- Risk: Latency regressions from cold infrastructure.
  - Mitigation: warm-host requirement enforcement and startup guardrail checks.

## 10. Immediate Next Actions

1. Create repository scaffold for standalone .NET 10 service with workstream ownership and CI gates.
2. Author phase-aware scope-to-permission matrix (v1 Teams-only, v2 adds mail/cross-channel).
3. Implement v1 W1 webhook ingestion + Teams identity resolver + Teams interaction log skeleton first.
4. In parallel, run spikes S1-S7 and capture ADRs to unblock detailed implementation choices.
5. Stand up baseline OpenTelemetry pipeline and immutable audit schema before end-to-end workflow coding.
6. Define explicit v1-to-v2 promotion gate with measurable pass criteria (quality, SLA, security, reliability).
