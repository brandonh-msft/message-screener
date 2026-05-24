---
date: 2026-05-24
topic: cold-contact-screener-agent
---

# Cold Contact Screener Agent

## Problem Frame

Knowledge workers receive a steady stream of first-contact and long-dormant-contact messages
across Teams and email. These messages often require thoughtful, context-rich replies that
reference past work, expertise, and organizational knowledge — but the recipient rarely has
bandwidth to respond in a timely, high-quality way. The result is delayed replies, thin
responses, or messages that go unanswered entirely.

This agent addresses two co-equal goals:

1. **Drafting**: Intercept cold-contact messages, draft a complete, on-brand reply using the
   user's knowledge graph and writing style, and surface it for inline review — without ever
   sending anything autonomously.
2. **Routing**: When the agent lacks sufficient context to draft a confident reply, identify
   the most relevant internal subject matter expert (SME) and surface a pre-composed
   introduction forward so the user can route the question with a single action.

```
┌──────────────────────────────────────────────────────────────────────────┐
│                          Inbound Message Arrives                         │
│                        (Teams DM or Outlook direct To:)                  │
└───────────────────────────────┬──────────────────────────────────────────┘
                                │
                                ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                  Contact History Check (cross-channel)                   │
│          Is sender known with interaction within recency window?         │
└─────────────┬────────────────────────────────────────┬───────────────────┘
              │ YES — known/recent                      │ NO — cold/unknown
              ▼                                         ▼
       ┌─────────────┐                   ┌──────────────────────────────┐
       │   IGNORE    │                   │      Message Classifier       │
       └─────────────┘                   │  (spam/automated vs. genuine) │
                                         └───────────────┬──────────────┘
                                                         │
                              ┌──────────────────────────┴──────────────┐
                              │                                          │
                              ▼                                          ▼
                   ┌──────────────────────┐          ┌─────────────────────────┐
                   │   Genuine Request    │          │    Likely Spam/Auto     │
                   │  RAG Knowledge       │          │  Compact card + flag    │
                   │  Retrieval           │          │  Discard / Reclassify   │
                   └──────────┬───────────┘          └─────────────────────────┘
                              │
                              ▼
                   ┌──────────────────────┐
                   │  Style-Matched       │
                   │  Draft Generator     │
                   └──────────┬───────────┘
                              │
               ───────────────┴────────────────
               │                              │
               ▼                              ▼
  ┌────────────────────────┐    ┌──────────────────────────────────┐
  │  High-Confidence Draft │    │  Low-Confidence Partial Draft    │
  │                        │    │  + SME Lookup in parallel        │
  │                        │    │  (org dir, threads, code owner,  │
  │                        │    │   doc authorship)                │
  └────────────┬───────────┘    └───────────────┬──────────────────┘
               │                                │
               └───────────────┬────────────────┘
                               │
                               ▼
              ┌─────────────────────────────────────────────┐
              │   Save Draft → Outlook Drafts / Teams       │
              │   composed deep-link                        │
              │   Send Adaptive Card notification:          │
              │   • Draft inline + confidence score         │
              │   • Inline citations for factual claims     │
              │   • AI disclosure toggle state              │
              │   • Approve / Edit / Discard                │
              │   • (low-conf) Up to 3 SME suggestions      │
              │   • (low-conf) Loop in [SME] per expert     │
              └─────────────────────────────────────────────┘
```

## Requirements

**Trigger and Routing**

- R1. The agent MUST monitor Microsoft Teams direct messages and Outlook emails where the
  user's primary SMTP address appears in the `To:` field. CC'd, BCC'd, and mailing-list-
  routed messages are excluded.
  - *Note:* BCC detection is best-effort — when a user is BCC'd, the received copy has the
    BCC header stripped and the message is indistinguishable from a direct `To:` message.
    The spam/genuine classifier (R4) serves as a secondary filter for bulk BCC traffic.
- R2. The agent MUST check cross-channel interaction history when a message arrives. A sender
  is "known and recent" if there has been any interaction (send or receive) across email or
  Teams within the configured recency window (default: 6 months). Messages from known-and-
  recent senders are silently ignored.
- R2a. The agent MUST implement a cross-channel identity resolver that maps Teams AAD UPNs
  and email SMTP addresses to a canonical contact ID using Graph `/users?$filter=proxyAddresses`
  lookups. When resolution fails, the agent MUST treat the message as cold and flag the
  contact as unresolved, surfacing a disambiguation prompt to the user on the Adaptive Card.
- R2b. When sender identity cannot be resolved to a known AAD principal, the Adaptive Card
  MUST include a warning: "Sender address does not match any known contact in the interaction
  log — verify the recipient before approving."
- R3. The recency window MUST be configurable by the user without code changes (default:
  6 months).
- R4. The agent MUST classify each cold message as either **Genuine** (real human ask) or
  **Likely Spam/Automated** (marketing, bulk notifications, automated tooling) before
  drafting.
- R4a. Spam-classified messages MUST still generate a compact Adaptive Card notification
  (sender name, message preview, spam rationale) with at minimum a **Discard** action and a
  **Reclassify as Genuine** button. Spam messages MUST NOT be silently dropped.
- R4b. User reclassifications MUST be recorded per sender domain and used to improve future
  classification accuracy. Reclassified messages MUST be reprocessed through the full genuine
  draft pipeline.
- R4c. The agent MUST apply a secondary urgency classifier to genuine messages (signals:
  keywords such as "urgent", "deadline", "today", "emergency"; sender title/seniority via the
  People API). Messages classified as both **Genuine** and **Urgent** MUST escalate the
  notification via Teams @mention or mobile push, distinct from standard card delivery.

**Draft Generation**

- R5. For genuine messages, the agent MUST draft a reply that is:
  - **Contextually complete** — it addresses the sender's actual ask using relevant retrieved
    knowledge.
  - **Written in the user's voice** — tone, vocabulary, structure, and signature style are
    derived from the user's historical emails and Teams messages.
- R5a. Each factual claim in the draft that is sourced from the knowledge base MUST carry an
  inline citation visible to the user during review (source document, email thread, or commit
  link). Claims with no traceable source MUST be marked `[unverified]`.
- R6. The knowledge base used to compose replies MUST include:
  - Outlook email history
  - Teams message history
  - SharePoint / OneDrive documents
  - GitHub repositories the user has access to
  - Corpus bounds (all configurable): email and Teams history within the past 2 years
    (default); SharePoint sites the user has explicitly pinned; GitHub repos the user
    has committed to in the past 12 months (default).
- R7. When the agent cannot assemble a confident, complete reply, it MUST produce a partial
  draft flagged as **low-confidence** AND run an SME lookup in parallel (see R21–R27).
- R7a. Draft confidence MUST be expressed as a normalized score [0.0–1.0]. The threshold
  separating high and low confidence (default: 0.6) MUST be configurable without code
  changes. The Adaptive Card MUST display the numeric or bucketed score alongside the
  confidence label.
- R8. The agent MUST NEVER send any reply automatically. All messages remain in draft state
  until the user explicitly acts.
- R8a. Approved drafts MUST include a configurable AI disclosure footer (e.g. "Drafted with
  AI assistance") that defaults to ON. The toggle state MUST be persisted as a user
  preference and visible on the Adaptive Card before approval.
- R8b. If draft generation fails or exceeds a 10-minute timeout, the agent MUST deliver a
  fallback Adaptive Card containing the raw inbound message, a failure reason, and a
  **Reply in [Client]** button that opens the native compose window. The failure MUST be
  logged to the user-accessible audit log (see R-SEC-7).

**Notification and Review**

- R9. Upon saving a genuine draft, the agent MUST deliver a Teams Adaptive Card notification
  containing:
  - Sender name, channel (Teams / Email), and message preview
  - The full draft reply inline
  - Confidence score (R7a)
  - Inline citations for sourced claims (R5a)
  - AI disclosure toggle state (R8a)
  - For low-confidence drafts: up to 3 SME suggestions (see R22)
  - Action buttons: **Approve**, **Edit**, **Discard**
  - For low-confidence drafts: a **Loop in [SME Name]** button per suggested expert (see R23)
- R10. **Approve** behavior varies by channel:
  - *Email*: moves the draft to the user's **Outlook Drafts folder**. The user manually
    clicks Send in Outlook.
  - *Teams*: opens a pre-populated Teams compose deep-link
    (`teams://l/chat/...?message=<encoded-draft>`). The user clicks Send from the compose
    box. Teams exposes no staged-send or draft API; the deep-link is the only supported
    mechanism.
- R11. **Edit** opens the draft in the relevant native client (Outlook for email, Teams
  compose for chat) for inline editing before sending.
- R12. **Discard** deletes the draft and dismisses the notification card.
- R13. Spam-classified messages MUST produce a compact Adaptive Card (per R4a) distinct from
  the full genuine draft card. This card MUST NOT include a draft reply or knowledge
  citations — only sender info, message preview, spam classification rationale, **Discard**,
  and **Reclassify as Genuine**.
- R14. If Teams Adaptive Card delivery fails or the user is offline for longer than a
  configurable threshold (default: 30 minutes), the agent MUST fall back to an email
  notification containing a summary and a deep link to the draft. The fallback channel MUST
  be configurable.

**Style Model**

- R15. The user's writing style MUST be derived automatically from existing email and Teams
  message history — no manual persona document required.
- R16. The user MUST be able to specify exclusion scopes for style-model ingestion (e.g.,
  folders, date ranges, or sensitivity-label classes). The system MUST default to excluding
  messages bearing Microsoft Purview sensitivity labels of **Confidential** or higher.
- R17. The style model MUST auto-update on a user-configurable schedule (default: weekly)
  and MUST be refreshable on demand.

**Contact History**

- R18. The agent MUST maintain a unified cross-channel interaction log (canonical contact ID,
  channel, timestamp) used for the recency check in R2.
- R18a. On first activation, the agent MUST perform a one-time backfill of the interaction
  log by scanning Outlook Sent Items and Teams message history for the configured recency
  window before enabling live webhook monitoring. The agent MUST display a
  backfill-in-progress state and MUST defer message processing until backfill completes (or
  document its behavior when backfill is incomplete at time of first message arrival).
- R19. The interaction log MUST be updated after the user sends any reply, regardless of
  whether the agent was involved. This requires separate Graph change notification
  subscriptions on SentItems and outbound Teams messages; see R-W1.
- R20. When the user acts on a **Loop-in** forward (R23), the forwarded-to SME is NOT added
  to the user's interaction log for the original sender. If the SME later initiates direct
  contact with the user, that interaction IS added to the log as a new contact relationship.

**Subject Matter Expert Routing**

- R21. When a low-confidence draft is produced, the agent MUST search internal sources in
  parallel to identify up to 3 colleagues most likely to answer the question. Sources:
  - Microsoft 365 People API / org directory (role, title, org-chart proximity)
  - Email and Teams threads related to the inbound message's topic
  - Code ownership signals (CODEOWNERS files, commit/PR authorship, GitHub review history)
  - SharePoint document authorship on topics matching the message
- R22. For each suggested SME, the Adaptive Card MUST display:
  - Name and title/role
  - A one-line rationale (e.g., "Authored 4 docs on Azure AI Search", "Primary reviewer for
    the auth module", "Active in the Teams channel for this product area")
  - **In Teams notifications**: the SME's name MUST be rendered as a Teams Contact Info card
    link so the user can view their full profile and contact options without leaving Teams.
  - **In email fallback notifications** (R14): the SME's name MUST be rendered as a
    `mailto:` link using their primary email address.
- R23. The **Loop in [SME Name]** card action creates a pre-composed forward of the original
  inbound message addressed to the selected SME. This MUST be a **separate draft object**
  from the reply draft (not reusing the R10 Approve slot) with its own Drafts folder /
  compose deep-link entry.
- R23a. The Adaptive Card for a Loop-in forward MUST display the exact content that will be
  sent to the SME — including the original sender's full message text — before any action is
  taken.
- R23b. The user MUST be able to edit the forwarded content before approving.
- R23c. The agent MUST strip attachments from the forwarded copy unless the user explicitly
  re-attaches them.
- R23d. The Loop-in forward follows the same Approve → Drafts folder / compose deep-link
  pattern as R10. Nothing is sent automatically.

**Reliability**

- R24. The agent MUST proactively renew Graph change notification subscriptions before their
  expiry (email subscriptions: ~4,230 min; Teams chat subscriptions: ~60 min).
- R25. If subscription renewal fails, the agent MUST notify the user via Teams (or email
  fallback) that monitoring is paused, with a **Re-activate** button.
- R26. On restart or recovery from a monitoring gap, the agent MUST back-fill any messages
  received during the gap via Graph delta-query polling before resuming live processing.
- R27. The agent MUST complete spam/genuine classification, draft generation, SME lookup (if
  applicable), and Adaptive Card delivery within 10 minutes for high-confidence drafts and
  within 15 minutes when SME lookup is triggered, under normal operating conditions (knowledge
  base indexed, agent host warm). Serverless deployments with cold-start times exceeding
  5 minutes are not a supported topology.

**Security**

- R-SEC-1. Inbound message content MUST be treated as untrusted data throughout the pipeline.
  Prompt templates MUST structurally separate system instructions from inbound message content
  using hard delimiters. The LLM invocation MUST use a system-prompt-locked template that
  refuses to act on instructions embedded in message bodies. (Prompt injection mitigation.)
- R-SEC-2. The token strategy MUST be explicitly defined: delegated tokens require a secure
  refresh-token store with encryption-at-rest and a documented expiry/re-auth notification
  flow; application permissions require admin-consent justification. The agent MUST NOT store
  access tokens in plaintext. Token expiry MUST alert the user via an alternative channel.
- R-SEC-3. All third-party access tokens (GitHub) MUST be stored in a secrets manager
  (e.g., Azure Key Vault), never in application config or environment variables. Tokens MUST
  be scoped to the minimum required permissions (read-only), MUST support rotation without
  service interruption, and MUST be independently revocable by the user.
- R-SEC-4. The implementation MUST enumerate all Microsoft Graph API and GitHub permission
  scopes requested. Each scope MUST be justified by a named requirement. Any scope not tied
  to a named requirement MUST be removed.
- R-SEC-5. All agent-managed persistent stores (interaction log, style model, knowledge
  index) MUST reside within the user's own M365 tenant or a tenant-bound storage resource.
  Data MUST be encrypted at rest. Access MUST be restricted to the agent's service identity
  and the user. No shared multi-tenant storage is permitted.
- R-SEC-6. Before any document chunk is included in a draft reply, the agent MUST evaluate
  its sensitivity label (Microsoft Purview or equivalent). Chunks from documents labeled
  **Confidential** or above MUST NOT be surfaced in drafts destined for external or
  unverified recipients. Every chunk used in generation MUST be logged (see R-SEC-7).
- R-SEC-7. The agent MUST maintain an immutable, append-only audit log recording: every
  message intercepted and its classification; every knowledge chunk retrieved and its source;
  every draft created; every user action (Approve / Edit / Discard / Loop-in); every message
  moved to Drafts or compose deep-link. The log MUST be user-accessible, retained for a
  minimum of 90 days, and not modifiable by the agent service.

## Success Criteria

- Cold-contact messages from genuine senders receive a draft and Adaptive Card notification
  within 10 minutes of arrival (15 minutes when SME lookup is triggered).
- The user can review a draft and either approve it or initiate a Loop-in forward in under
  30 seconds from the Adaptive Card.
- After a 2-week trial with ≥20 approved genuine-contact drafts, ≥50% of drafts require no
  more than minor edits, measured on a 3-point scale (no edit / minor edit / major rewrite)
  captured via an Adaptive Card feedback action. Target: median edit distance <15% of draft
  length by week 4.
- Zero messages are auto-sent without explicit user action.
- The recency window accurately reflects cross-channel history — a sender emailed last month
  is not incorrectly flagged as cold.
- No Confidential-or-above content appears in any draft destined for an external recipient.

## Scope Boundaries

- The agent monitors Teams **direct messages** only; group/channel messages are excluded.
- The agent processes emails where the user appears in `To:` only; CC, BCC, and mailing-list
  routing are excluded (BCC exclusion is best-effort per R1 note).
- The agent NEVER auto-sends under any circumstances.
- Calendar invites, task assignments, and non-message channels are out of scope for v1.
- Voice/audio communication channels are out of scope.
- The agent is designed as a **single-user personal agent** with delegated M365 permissions.
  SME routing (R21–R23) requires read access to org-directory and colleagues' public repo
  activity via application-level Graph scopes (People.Read.All, Sites.Read.All); the
  permission model MUST be documented and admin-consented per tenant policy. No shared or
  multi-tenant deployment is supported.

## Key Decisions

- **Cross-channel unified interaction log**: Avoids the false-cold case where an active email
  correspondent messages via Teams for the first time.
- **SME routing is v1**: Core to the value proposition; a low-confidence draft alone is not
  sufficient when the agent cannot answer well.
- **Teams compose deep-link, not draft API**: Teams exposes no API for staging a draft or
  send-queue. The deep-link pre-populates the compose box; the user sends manually. This
  preserves the no-auto-send guarantee.
- **Approve → Outlook Drafts (not Outbox)**: The Outbox auto-transmits on next sync,
  violating the no-auto-send guarantee. Drafts folder is the correct target.
- **Auto-mined style model**: Lower setup friction than a persona document; the agent learns
  from real communication patterns with sensitivity-label exclusions.
- **AI disclosure footer default ON**: Legal and trust alignment (EU AI Act); configurable
  off for users who explicitly opt out.
- **SME Contact Info / mailto links**: Teams notifications link to the SME's Contact Info
  card for in-context profile access; email fallback notifications use mailto: links. Both
  avoid copy-paste friction while respecting channel context.
- **Full-scope v1 (no phasing)**: All subsystems — trigger, classifier, RAG indexer, style
  model, interaction log, SME routing — are in-scope for v1. No deliberate phasing.
- **New repository tree**: Standalone agent service; not embedded in the existing
  `message-screener` repo.

## Dependencies / Assumptions

- The user has a Microsoft 365 account with Graph API access for Teams and Outlook.
- Graph change notification (webhook) subscriptions are available for the user's tenant;
  Teams chat subscriptions expire at ~60 minutes and require frequent renewal (R24).
- The agent requires admin consent for org-scoped Graph scopes (People.Read.All,
  Sites.Read.All) to support SME lookup and SharePoint knowledge indexing.
- An Azure Bot Service instance (or equivalent Teams App registration) MUST be pre-installed
  in the user's tenant and have had at least one prior user interaction before proactive
  Adaptive Card delivery is possible (Teams platform requirement).
- GitHub access tokens are available for repository indexing; these MUST be stored per R-SEC-3.
- The agent host MUST remain warm (not cold-start serverless) to meet R27's latency target.
- Outbound interaction tracking (R19) requires separate Graph subscriptions on SentItems
  (Mail.Read) and outbound Teams messages (Chat.Read) with independent renewal cycles.

## Outstanding Questions

### Resolve Before Planning

*All blocking questions resolved.*

### Deferred to Planning

- [Affects R5, R15] [Technical] What chunking and embedding strategy best preserves
  email/Teams conversational style for both RAG retrieval and style modeling?
- [Affects R18, R18a] [Technical] What is the best storage backend for the cross-channel
  interaction log given the agent is single-user and potentially serverless?
- [Affects R9, R10, R13] [Technical] Azure Bot Service vs. Teams App manifest with
  messaging extension — which bot registration path best supports proactive Adaptive Card
  delivery and deep-link compose actions?
- [Affects R6] [Needs research] Does SharePoint Graph search return document content in a
  form suitable for RAG, or is a separate indexing pipeline (e.g., Azure AI Search) required?
- [Affects R21] [Needs research] Microsoft 365 People API vs. `/people` Graph endpoint vs.
  OrgChart API — which provides the richest signal for SME matching (role, expertise, org
  proximity)?
- [Affects R21] [Technical] What ranking model best combines org-directory signals, document
  authorship, and GitHub code ownership into a confidence-ranked SME list (up to 3)?
- [Affects R-SEC-2] [Technical] Delegated refresh token storage vs. application-permission
  grant — which is appropriate given the always-on monitoring requirement and tenant policy?

## Next Steps

→ All blocking questions resolved. Proceed to `/ce-plan`.
