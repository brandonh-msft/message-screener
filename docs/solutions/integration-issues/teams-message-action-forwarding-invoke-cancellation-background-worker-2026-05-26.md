---
title: Teams Message-Action Forwarding Offloads Work to a Background Worker to Avoid Invoke Cancellation
problem_type: integration_issue
category: integration-issues
module: message-action-forwarding
component: teams-forward-invoke-processing
status: resolved
date: 2026-05-26
tags:
  - teams
  - compose-extension
  - message-action-forwarding
  - background-service
  - cancellation
  - audit
  - bot-connector
---

## Problem

The Teams message-action forward path was doing intake, Copilot drafting, delivery, and audit writes inline on the `/api/messages` invoke. That exceeded the Teams invoke window and caused the request to be canceled before processing completed.

## Symptoms

- Teams showed a modal: `Unable to reach app. Please try again.`
- Logs showed Copilot drafting failing with `A task was canceled.`
- The invoke handler then failed with `OperationCanceledException` while appending the audit entry.

## What Didn't Work

1. Doing the whole forward flow synchronously inside the compose-extension submit handler.
- The invoke lifecycle controlled the entire workflow, so a slow draft or delivery step could cancel the request before completion.

2. Relying on Copilot fallback text alone.
- Even when drafting fell back to a template, the request could still be aborted before the rest of the pipeline completed.

3. Letting request cancellation propagate into persistence.
- `InMemoryForwardAuditStore.AppendAsync(...)` honored the caller token, so the audit write could disappear with the canceled invoke.

## Solution

Move the forward path off the invoke thread:

```csharp
await forwardActionQueue.EnqueueAsync(
    new ForwardActionWorkItem(forwardedMessage, bootstrapContext),
    cancellationToken);

return Results.Ok(CreateComposeExtensionStatus(
    "Message forwarded. I’ll post the draft in your personal Message Screener chat."));
```

Add a background worker to drain the queue:

```csharp
public sealed class ForwardActionBackgroundService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (ForwardActionWorkItem workItem in queue.DequeueAllAsync(stoppingToken))
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            IForwardActionProcessor processor = scope.ServiceProvider.GetRequiredService<IForwardActionProcessor>();
            await processor.ProcessAsync(workItem, stoppingToken);
        }
    }
}
```

Process bootstrap, intake, drafting, delivery, and audit in the worker:

```csharp
await forwardAuditStore.AppendAsync(
    CreateForwardAuditEntry(message, intakeResult),
    CancellationToken.None);
```

The queue and worker are wired through `Program.cs`, and the Teams action now returns immediately instead of waiting for the draft to finish.

## Why This Works

The Teams invoke finishes quickly, so the client no longer tears down the request before the reply is ready. The work that can take time now runs in a background scope that is no longer coupled to the request cancellation token.

Persisting the audit record with `CancellationToken.None` in the worker prevents request aborts from erasing the audit trail. The background worker also keeps the earlier personal-review bootstrap logic intact, so the first forward can still create and reuse the personal chat.

## Prevention

- Keep Teams invoke handlers thin: enqueue work, acknowledge, and return.
- Treat request cancellation as caller context, not as a reason to drop downstream persistence.
- Keep any Copilot or connector I/O out of the synchronous invoke path.
- Add a regression test or manual check that forwarding returns immediately and the draft arrives later in the personal review chat.

## Related References

- `docs/solutions/integration-issues/teams-message-action-graph-removal-truthful-status-2026-05-25.md`
- `docs/solutions/integration-issues/automatic-dm-screening-requires-tenant-admin-graph-consent-2026-05-24.md`
- `src/MessageScreener.Api/Program.cs`
- `src/MessageScreener.Api/ForwardActionQueue.cs`
- `src/MessageScreener.Api/ForwardActionProcessor.cs`
- `src/MessageScreener.Audit/ForwardAuditStore.cs`
- `src/MessageScreener.ReviewDelivery/PersonalReviewConversationBootstrapper.cs`
