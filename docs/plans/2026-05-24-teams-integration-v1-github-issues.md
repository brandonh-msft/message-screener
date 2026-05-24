---
date: 2026-05-24
topic: teams-integration-v1
type: github-issues-breakdown
source_backlog: docs/plans/2026-05-24-teams-integration-v1-execution-backlog.md
---

# Teams Integration V1 - GitHub Issue Breakdown

Use one issue per task ID. Create in dependency order where possible.

## Label Recommendations

- `area:platform`
- `area:integration`
- `area:backend`
- `area:security`
- `area:observability`
- `area:qa`
- `milestone:m1-foundation`
- `milestone:m2-drafting-ux`
- `milestone:m3-hardening`
- `milestone:m4-pilot`
- `priority:p0` (critical path)
- `priority:p1`

## Issue Template

```markdown
## Summary
[Short description]

## Scope
- [ ] Item 1
- [ ] Item 2

## Dependencies
- [ ] #<issue-number> Txx

## Acceptance Criteria
- [ ] Criterion 1
- [ ] Criterion 2

## Requirement Links
- docs/brainstorms/2026-05-24-teams-integration-v1-requirements.md

## Plan Links
- docs/plans/2026-05-24-teams-integration-v1-ce-plan.md
- docs/plans/2026-05-24-teams-integration-v1-execution-backlog.md
```

## Issues

### T01 - Scaffold .NET 10 solution and projects
**Title:** `[T01][M1] Scaffold .NET 10 solution for Teams integration v1 services`

```markdown
## Summary
Create the baseline .NET 10 solution and project structure for ingress, orchestrator, review delivery, and audit services.

## Scope
- [ ] Create .NET 10 solution and core projects
- [ ] Add shared contracts project for cross-service DTOs
- [ ] Ensure project references align to service boundaries in CE plan
- [ ] Add build scripts and baseline CI build job

## Dependencies
- [ ] None

## Acceptance Criteria
- [ ] `dotnet build` passes with zero warnings introduced
- [ ] Project layout supports service boundaries in CE plan

## Requirement Links
- R16

## Plan Links
- W1, W5
```

### T02 - Baseline telemetry and source-generated logging
**Title:** `[T02][M1] Add OpenTelemetry baseline and source-generated logging`

```markdown
## Summary
Wire startup telemetry and source-generated structured logging templates.

## Scope
- [ ] Add OpenTelemetry traces/metrics/logs configuration
- [ ] Add source-generated logger classes for ingress/orchestrator paths
- [ ] Emit one end-to-end trace from intake to decision

## Dependencies
- [ ] #<T01-issue-number> T01

## Acceptance Criteria
- [ ] Telemetry emitted from startup and sample request path
- [ ] Logging uses source-generated pattern in hot paths
- [ ] Build passes with zero warnings introduced

## Requirement Links
- R18

## Plan Links
- W5
```

### T03 - Graph webhook intake and validation
**Title:** `[T03][M1] Implement Graph webhook intake endpoint and validation`

```markdown
## Summary
Implement secure Graph webhook intake and handshake validation endpoint.

## Scope
- [ ] Webhook validation handshake endpoint
- [ ] Event intake endpoint with signature/token checks
- [ ] Structured logging for incoming event metadata

## Dependencies
- [ ] #<T01-issue-number> T01

## Acceptance Criteria
- [ ] Webhook validation handshake succeeds
- [ ] Intake endpoint returns expected status codes
- [ ] Invalid requests are rejected and logged

## Requirement Links
- R1, R5a

## Plan Links
- W1
```

### T04 - Trigger filter matrix
**Title:** `[T04][M1] Implement Teams trigger filter matrix for v1 scope`

```markdown
## Summary
Apply deterministic trigger filtering for 1:1 and group @mention scenarios only.

## Scope
- [ ] Include 1:1 messages
- [ ] Include group messages only when user @mentioned
- [ ] Exclude channels, edits/deletes/reactions/system events
- [ ] Exclude bot/self/attachment-only messages

## Dependencies
- [ ] #<T03-issue-number> T03

## Acceptance Criteria
- [ ] Automated tests cover all trigger/non-trigger classes
- [ ] Group messages without @mention are ignored

## Requirement Links
- R1, R2, R3, R5a, R5b, R5c

## Plan Links
- W1
```

### T05 - Idempotency for duplicate create events
**Title:** `[T05][M1] Add idempotency layer for duplicate create deliveries`

```markdown
## Summary
Ensure duplicate inbound create events do not produce duplicate side effects.

## Scope
- [ ] Define idempotency key strategy for Teams message create events
- [ ] Persist and enforce dedupe on card/draft/audit actions
- [ ] Add replay test fixtures for duplicate delivery

## Dependencies
- [ ] #<T03-issue-number> T03

## Acceptance Criteria
- [ ] Duplicate replay creates no duplicate cards
- [ ] Duplicate replay creates no duplicate draft intents
- [ ] Duplicate replay creates no duplicate audit actions

## Requirement Links
- R5d

## Plan Links
- W1
```

### T06 - Recency-check service and interaction log
**Title:** `[T06][M1] Implement recency-check service and interaction log contract`

```markdown
## Summary
Implement deterministic known/recent evaluation and interaction log read/write model.

## Scope
- [ ] Define interaction log schema and access contract
- [ ] Implement configurable recency window behavior
- [ ] Record effective recency value in audit events

## Dependencies
- [ ] #<T04-issue-number> T04
- [ ] #<T05-issue-number> T05

## Acceptance Criteria
- [ ] Known/recent decision deterministic for defined scenarios
- [ ] Recency window configurable without code changes
- [ ] Effective recency value audited

## Requirement Links
- R4, R5

## Plan Links
- W1
```

### T07 - Genuine vs spam classifier shell
**Title:** `[T07][M2] Implement genuine/spam classifier shell with domain feedback hooks`

```markdown
## Summary
Implement first-pass classifier path and reclassification hooks.

## Scope
- [ ] Implement classification interface and default model/heuristics
- [ ] Add domain-level reclassification feedback mechanism
- [ ] Route spam cases to compact card flow

## Dependencies
- [ ] #<T04-issue-number> T04
- [ ] #<T06-issue-number> T06

## Acceptance Criteria
- [ ] Triggered messages classified before drafting
- [ ] Spam path always produces compact card actions
- [ ] Reclassification hooks available for domain-level feedback

## Requirement Links
- R6, R6a, R9

## Plan Links
- W2
```

### T08 - Drafting + confidence + citations
**Title:** `[T08][M2] Build drafting pipeline with confidence score and citations`

```markdown
## Summary
Generate draft responses with confidence score and citation markers.

## Scope
- [ ] Draft generation pipeline for genuine path
- [ ] Confidence scoring output and threshold input
- [ ] Citation markers and no-source fallback marker

## Dependencies
- [ ] #<T06-issue-number> T06
- [ ] #<T07-issue-number> T07

## Acceptance Criteria
- [ ] Genuine messages produce draft + confidence
- [ ] Citation markers reference used sources only
- [ ] Missing-source case renders `No supporting source retrieved`

## Requirement Links
- R7

## Plan Links
- W2
```

### T09 - SME suggestions for low confidence
**Title:** `[T09][M2] Add low-confidence SME suggestion payload`

```markdown
## Summary
Provide top-3 SME suggestions with rationale when confidence is low.

## Scope
- [ ] Build low-confidence branching logic
- [ ] Integrate candidate ranking payload (max 3)
- [ ] Attach one-line rationale per suggestion

## Dependencies
- [ ] #<T08-issue-number> T08

## Acceptance Criteria
- [ ] Low-confidence cases include <=3 SME suggestions
- [ ] Each suggestion includes rationale text

## Requirement Links
- R8

## Plan Links
- W2
```

### T10 - Adaptive Card templates
**Title:** `[T10][M2] Implement Adaptive Card templates for genuine/low-confidence/spam`

```markdown
## Summary
Create Teams card templates for all required review states in personal bot chat.

## Scope
- [ ] Genuine card template
- [ ] Low-confidence card template with SME section
- [ ] Compact spam card template

## Dependencies
- [ ] #<T08-issue-number> T08
- [ ] #<T09-issue-number> T09

## Acceptance Criteria
- [ ] All card variants render correctly in Teams personal bot chat
- [ ] Cards include required action controls

## Requirement Links
- R11, R12, R13

## Plan Links
- W3
```

### T11 - Card action handlers
**Title:** `[T11][M2] Implement card action handlers with no-auto-send enforcement`

```markdown
## Summary
Implement Approve/Edit/Discard/Loop-in action semantics with explicit no-send safeguards.

## Scope
- [ ] Approve -> compose deep-link prefill
- [ ] Edit -> compose deep-link editable path
- [ ] Discard -> terminate workflow state
- [ ] Loop-in -> compose-ready content only, no send/chat creation

## Dependencies
- [ ] #<T10-issue-number> T10

## Acceptance Criteria
- [ ] No action path can auto-send
- [ ] Loop-in action has no implicit side effects beyond draft preparation

## Requirement Links
- R10, R12, R13, R14, R15

## Plan Links
- W3
```

### T12 - Deep-link overflow fallback
**Title:** `[T12][M2] Add deep-link payload overflow fallback behavior`

```markdown
## Summary
Handle over-limit deep-link content by shortening compose prefill and linking full context.

## Scope
- [ ] Payload sizing checks for deep-link generation
- [ ] Shortened prefill strategy
- [ ] Link-back to full draft context in personal bot chat

## Dependencies
- [ ] #<T11-issue-number> T11

## Acceptance Criteria
- [ ] Oversized drafts remain user-actionable
- [ ] Fallback preserves no-auto-send behavior

## Requirement Links
- R15a

## Plan Links
- W3
```

### T13 - Bot chat unavailable fallback
**Title:** `[T13][M3] Implement fallback when personal bot chat delivery is unavailable`

```markdown
## Summary
Provide recoverable Teams fallback notification and re-activation guidance on bot-chat delivery failures.

## Scope
- [ ] Detect personal bot chat delivery failure classes
- [ ] Emit fallback Teams notification with recovery instructions
- [ ] Log structured failure reason and correlation IDs

## Dependencies
- [ ] #<T10-issue-number> T10

## Acceptance Criteria
- [ ] Delivery failure yields recoverable fallback path
- [ ] Failure reasons are captured in logs/audit

## Requirement Links
- R11a

## Plan Links
- W4
```

### T14 - Retries/timeouts/dead-letter
**Title:** `[T14][M3] Add retry, timeout, and dead-letter handling for delivery/callback paths`

```markdown
## Summary
Harden delivery and callback paths with bounded retries, timeout budgets, and dead-letter handling.

## Scope
- [ ] Retry policy with bounded backoff
- [ ] Timeout settings for external calls and callbacks
- [ ] Dead-letter queue/storage and triage metadata

## Dependencies
- [ ] #<T11-issue-number> T11
- [ ] #<T13-issue-number> T13

## Acceptance Criteria
- [ ] Retries are bounded and observable
- [ ] Dead-letter events include actionable diagnostics

## Requirement Links
- R11a, Success Criteria latency goals

## Plan Links
- W4
```

### T15 - Prompt injection and sensitive-content guard
**Title:** `[T15][M3] Implement prompt injection and sensitive-content guard stage`

```markdown
## Summary
Run guard checks before displaying drafts; fail safe on guard failure.

## Scope
- [ ] Pre-display guard stage in drafting path
- [ ] Safe-review fallback when guard fails
- [ ] Guard-reason logging and audit emission

## Dependencies
- [ ] #<T08-issue-number> T08

## Acceptance Criteria
- [ ] Guard failure suppresses draft content
- [ ] Safe-review message is shown on guard failure
- [ ] Guard reason is logged/audited

## Requirement Links
- R20

## Plan Links
- W5
```

### T16 - Append-only audit model
**Title:** `[T16][M3] Implement append-only audit event model with immutable IDs`

```markdown
## Summary
Implement append-only audit events with immutable IDs and timestamps.

## Scope
- [ ] Define audit event contract
- [ ] Enforce append-only write behavior
- [ ] Immutable event identifiers and timestamps

## Dependencies
- [ ] #<T01-issue-number> T01

## Acceptance Criteria
- [ ] Audit events are append-only
- [ ] Event IDs/timestamps immutable after write

## Requirement Links
- R19

## Plan Links
- W5
```

### T17 - Audit retention and owner-only access
**Title:** `[T17][M3] Enforce audit retention policy and owner-only read access`

```markdown
## Summary
Apply default and configurable retention controls and owner-only read rules.

## Scope
- [ ] Default 90-day retention policy
- [ ] Tenant-configurable retention controls
- [ ] Owner-only read authorization in personal mode

## Dependencies
- [ ] #<T16-issue-number> T16

## Acceptance Criteria
- [ ] Retention defaults and overrides behave as specified
- [ ] Unauthorized audit reads are denied

## Requirement Links
- R19a, R19b

## Plan Links
- W5
```

### T18 - Audit redaction policy
**Title:** `[T18][M3] Implement redaction policy for sensitive audit fields`

```markdown
## Summary
Redact sensitive fields before audit storage and display.

## Scope
- [ ] Redaction policy definition and enforcement points
- [ ] Redaction tests for known sensitive field classes
- [ ] Display-layer safety checks

## Dependencies
- [ ] #<T16-issue-number> T16

## Acceptance Criteria
- [ ] Sensitive fields are redacted at storage and retrieval
- [ ] Redaction policy test suite passes

## Requirement Links
- R19c

## Plan Links
- W5
```

### T19 - End-to-end test harness
**Title:** `[T19][M4] Build end-to-end harness for trigger/action/guard/fallback flows`

```markdown
## Summary
Create automated E2E coverage for major requirement paths and edge cases.

## Scope
- [ ] E2E test fixtures for trigger matrix
- [ ] Action flow tests (approve/edit/discard/loop-in)
- [ ] Guard and fallback behavior validation

## Dependencies
- [ ] #<T04-issue-number> T04
- [ ] #<T05-issue-number> T05
- [ ] #<T06-issue-number> T06
- [ ] #<T07-issue-number> T07
- [ ] #<T08-issue-number> T08
- [ ] #<T09-issue-number> T09
- [ ] #<T10-issue-number> T10
- [ ] #<T11-issue-number> T11
- [ ] #<T12-issue-number> T12
- [ ] #<T13-issue-number> T13
- [ ] #<T14-issue-number> T14
- [ ] #<T15-issue-number> T15
- [ ] #<T16-issue-number> T16
- [ ] #<T17-issue-number> T17
- [ ] #<T18-issue-number> T18

## Acceptance Criteria
- [ ] E2E suite covers major paths and key edge cases
- [ ] Test outputs map back to requirement IDs

## Requirement Links
- R1-R20

## Plan Links
- W6
```

### T20 - Load/replay verification for SLO + idempotency
**Title:** `[T20][M4] Add load and replay verification for latency SLO and idempotency`

```markdown
## Summary
Validate 95/99 delivery latency targets and duplicate-event idempotency under stress.

## Scope
- [ ] Load test for card delivery latency metrics
- [ ] Replay test for duplicate create-event scenarios
- [ ] Report generation for SLO evidence

## Dependencies
- [ ] #<T13-issue-number> T13
- [ ] #<T14-issue-number> T14
- [ ] #<T19-issue-number> T19

## Acceptance Criteria
- [ ] 95/99 latency evidence produced
- [ ] Idempotency holds under replay stress

## Requirement Links
- R5d, Success Criteria

## Plan Links
- W6
```

### T21 - KPI instrumentation and pilot scorecards
**Title:** `[T21][M4] Implement KPI instrumentation and pilot scorecards`

```markdown
## Summary
Implement measurement for pilot success criteria and operational dashboards.

## Scope
- [ ] Metrics for delivery latency, time-to-compose, auto-send violations
- [ ] Metrics for SME relevance feedback and traceability completeness
- [ ] Dashboard/query assets for pilot reporting

## Dependencies
- [ ] #<T19-issue-number> T19

## Acceptance Criteria
- [ ] KPI dashboards and queries available
- [ ] Metrics align to success criteria definitions

## Requirement Links
- Success Criteria section

## Plan Links
- W6
```

### T22 - Pilot readiness and go/no-go package
**Title:** `[T22][M4] Produce pilot readiness evidence and go/no-go recommendation`

```markdown
## Summary
Compile pilot readiness package and make explicit go/no-go decision.

## Scope
- [ ] Gather evidence from E2E, load, replay, security, and audit checks
- [ ] Summarize pass/fail against success criteria
- [ ] Record go/no-go decision with remediation list if no-go

## Dependencies
- [ ] #<T20-issue-number> T20
- [ ] #<T21-issue-number> T21

## Acceptance Criteria
- [ ] Go/no-go packet complete and reviewable
- [ ] Decision references measurable criteria and evidence

## Requirement Links
- Success Criteria, R19-R20

## Plan Links
- W6
```

## Creation Order (Recommended)

1. T01, T02, T03
2. T04, T05, T06
3. T07, T08, T09
4. T10, T11, T12
5. T13, T14, T15, T16, T17, T18
6. T19, T20, T21, T22

## Notes

- Replace dependency placeholders like `#<T01-issue-number>` once issues are created.
- Keep issue bodies aligned to requirement IDs for traceability.
- Mark critical-path tasks (`T04`, `T05`, `T10`, `T11`, `T13`, `T19`, `T20`, `T22`) as `priority:p0`.
