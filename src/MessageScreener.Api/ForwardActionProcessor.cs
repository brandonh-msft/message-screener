using MessageScreener.Audit;
using MessageScreener.Api.Logging;
using MessageScreener.Contracts;
using MessageScreener.Orchestration;
using MessageScreener.ReviewDelivery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace MessageScreener.Api;

public interface IForwardActionProcessor
{
    ValueTask ProcessAsync(ForwardActionWorkItem workItem, CancellationToken cancellationToken);
}

public sealed class ForwardActionProcessor(
    IPersonalReviewConversationBootstrapper bootstrapper,
    IMessageIntakeService intakeService,
    IForwardAuditStore forwardAuditStore,
    ICommunicationTwinService communicationTwinService,
    ICallerAutoResponseComposer callerAutoResponseComposer,
    IReviewDeliveryService reviewDeliveryService,
    ILogger<ForwardActionProcessor> logger) : IForwardActionProcessor
{
    public async ValueTask ProcessAsync(ForwardActionWorkItem workItem, CancellationToken cancellationToken)
    {
        try
        {
            if (workItem.BootstrapContext is not null)
            {
                await bootstrapper.EnsureConversationAsync(
                    workItem.BootstrapContext.ServiceUrl,
                    workItem.BootstrapContext.TenantId,
                    workItem.BootstrapContext.InvokingUserId,
                    workItem.BootstrapContext.InvokingUserDisplayName,
                    workItem.BootstrapContext.BotId,
                    cancellationToken);
            }

            await ProcessInboundMessageAsync(workItem.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background forward processing failed for event {EventId}.", workItem.Message.EventId);
        }
    }

    private async ValueTask ProcessInboundMessageAsync(TeamsInboundMessage message, CancellationToken cancellationToken)
    {
        MessageIntakeResult intakeResult = await intakeService.IntakeAsync(message, cancellationToken);

        AppLog.InboundIntakeProcessed(
            logger,
            intakeResult.Accepted,
            intakeResult.Duplicate,
            intakeResult.Trigger.ShouldCreateReview,
            intakeResult.ReasonCode,
            intakeResult.Trigger.ReasonCode);

        try
        {
            if (intakeResult.Accepted && intakeResult.Trigger.ShouldCreateReview)
            {
                CommunicationTwinProfile twinProfile = communicationTwinService.GetInitialProfile();
                string pendingApprovalReply = await callerAutoResponseComposer.ComposePendingApprovalReplyAsync(
                    message,
                    twinProfile,
                    cancellationToken);

                await reviewDeliveryService.SendPendingApprovalReplyAsync(
                    message,
                    pendingApprovalReply,
                    cancellationToken);
            }

            await forwardAuditStore.AppendAsync(
                CreateForwardAuditEntry(message, intakeResult),
                CancellationToken.None);

            await intakeService.MarkCompletedAsync(intakeResult, cancellationToken);
        }
        catch
        {
            await forwardAuditStore.AppendAsync(
                CreateForwardAuditEntry(message, intakeResult),
                CancellationToken.None);

            await intakeService.ResetAsync(intakeResult, cancellationToken);
            throw;
        }
    }

    private static ForwardAuditEntry CreateForwardAuditEntry(TeamsInboundMessage message, MessageIntakeResult intakeResult)
    {
        return new ForwardAuditEntry(
            AuditEventId: Guid.NewGuid().ToString("N"),
            RecordedAtUtc: DateTimeOffset.UtcNow,
            TenantId: message.TenantId,
            SourceConversationId: message.ConversationId,
            SourceMessageId: message.SourceMessageId,
            SenderDisplayName: message.SenderDisplayName,
            SenderIdentityKey: message.SenderIdentityKey,
            SenderIdentityKeyKind: message.SenderIdentityKeyKind,
            ProcessingState: intakeResult.ProcessingState,
            IntakeReasonCode: intakeResult.ReasonCode,
            ReviewRequested: intakeResult.Trigger.ShouldCreateReview);
    }
}

public sealed class ForwardActionBackgroundService(
    IForwardActionQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<ForwardActionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (ForwardActionWorkItem workItem in queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                IForwardActionProcessor processor = scope.ServiceProvider.GetRequiredService<IForwardActionProcessor>();
                await processor.ProcessAsync(workItem, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Forward action background loop failed for event {EventId}.", workItem.Message.EventId);
            }
        }
    }
}
