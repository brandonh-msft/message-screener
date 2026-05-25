using MessageScreener.Contracts;
using Microsoft.Extensions.Logging;

namespace MessageScreener.Orchestration
{
    public enum InboundEventRegistrationState
    {
        Accepted = 0,
        DuplicateInFlight = 1,
        DuplicateCompleted = 2,
    }

    public readonly record struct InboundEventRegistrationResult(
        string DeduplicationKey,
        InboundEventRegistrationState State,
        bool Accepted);

    public interface IInboundEventStore
    {
        ValueTask<InboundEventRegistrationResult> TryBeginProcessingAsync(
            string deduplicationKey,
            DateTimeOffset observedAtUtc,
            CancellationToken cancellationToken);

        ValueTask MarkCompletedAsync(string deduplicationKey, CancellationToken cancellationToken);

        ValueTask ResetAsync(string deduplicationKey, CancellationToken cancellationToken);
    }

    public interface ITriggerPolicy
    {
        TriggerEvaluationResult Evaluate(TeamsInboundMessage message);
    }

    public interface IMessageIntakeService
    {
        ValueTask<MessageIntakeResult> IntakeAsync(TeamsInboundMessage message, CancellationToken cancellationToken);

        ValueTask MarkCompletedAsync(MessageIntakeResult result, CancellationToken cancellationToken);

        ValueTask ResetAsync(MessageIntakeResult result, CancellationToken cancellationToken);
    }

    public sealed class MessageIntakeService(
        IInboundEventStore inboundEventStore,
        ITriggerPolicy triggerPolicy,
        ILogger<MessageIntakeService> logger) : IMessageIntakeService
    {
        public async ValueTask<MessageIntakeResult> IntakeAsync(TeamsInboundMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(message.EventId))
            {
                var invalidTrigger = new TriggerEvaluationResult(false, "invalid_event_id");
                IntakeLog.InvalidEventId(logger);
                return new MessageIntakeResult(
                    false,
                    false,
                    "invalid_event_id",
                    invalidTrigger,
                    string.Empty,
                    MessageProcessingState.DuplicateCompleted,
                    false);
            }

            string deduplicationKey = CreateDeduplicationKey(message);
            InboundEventRegistrationResult registration = await inboundEventStore.TryBeginProcessingAsync(
                deduplicationKey,
                message.OccurredAtUtc,
                cancellationToken);
            TriggerEvaluationResult trigger = triggerPolicy.Evaluate(message);

            if (!registration.Accepted)
            {
                MessageProcessingState processingState = registration.State switch
                {
                    InboundEventRegistrationState.DuplicateInFlight => MessageProcessingState.DuplicateInFlight,
                    _ => MessageProcessingState.DuplicateCompleted,
                };

                string reasonCode = processingState == MessageProcessingState.DuplicateInFlight
                    ? "duplicate_event_in_flight"
                    : "duplicate_event_completed";

                IntakeLog.DuplicateInboundEvent(logger, deduplicationKey, reasonCode);
                return new MessageIntakeResult(
                    false,
                    true,
                    reasonCode,
                    trigger,
                    deduplicationKey,
                    processingState,
                    false);
            }

            IntakeLog.InboundEventProcessed(logger, deduplicationKey, trigger.ShouldCreateReview, trigger.ReasonCode);
            return new MessageIntakeResult(
                true,
                false,
                "accepted",
                trigger,
                deduplicationKey,
                MessageProcessingState.Accepted,
                false);
        }

        public ValueTask MarkCompletedAsync(MessageIntakeResult result, CancellationToken cancellationToken)
        {
            if (!result.Accepted || string.IsNullOrWhiteSpace(result.DeduplicationKey))
            {
                return ValueTask.CompletedTask;
            }

            return inboundEventStore.MarkCompletedAsync(result.DeduplicationKey, cancellationToken);
        }

        public ValueTask ResetAsync(MessageIntakeResult result, CancellationToken cancellationToken)
        {
            if (!result.Accepted || string.IsNullOrWhiteSpace(result.DeduplicationKey))
            {
                return ValueTask.CompletedTask;
            }

            return inboundEventStore.ResetAsync(result.DeduplicationKey, cancellationToken);
        }

        private static string CreateDeduplicationKey(TeamsInboundMessage message)
        {
            return string.Join(
                ':',
                message.TenantId.Trim(),
                message.ConversationId.Trim(),
                message.SourceMessageId.Trim());
        }
    }
}