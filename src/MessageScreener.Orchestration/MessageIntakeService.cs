using MessageScreener.Contracts;
using Microsoft.Extensions.Logging;

namespace MessageScreener.Orchestration
{
    public interface IInboundEventStore
    {
        ValueTask<bool> TryMarkProcessedAsync(string eventId, CancellationToken cancellationToken);
    }

    public interface ITriggerPolicy
    {
        TriggerEvaluationResult Evaluate(TeamsInboundMessage message);
    }

    public interface IMessageIntakeService
    {
        ValueTask<MessageIntakeResult> IntakeAsync(TeamsInboundMessage message, CancellationToken cancellationToken);
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
                return new MessageIntakeResult(false, false, "invalid_event_id", invalidTrigger);
            }

            var isFirstObservation = await inboundEventStore.TryMarkProcessedAsync(message.EventId, cancellationToken);
            TriggerEvaluationResult trigger = triggerPolicy.Evaluate(message);

            if (!isFirstObservation)
            {
                IntakeLog.DuplicateInboundEvent(logger, message.EventId, trigger.ReasonCode);
                return new MessageIntakeResult(false, true, "duplicate_event", trigger);
            }

            IntakeLog.InboundEventProcessed(logger, message.EventId, trigger.ShouldCreateReview, trigger.ReasonCode);
            return new MessageIntakeResult(true, false, "accepted", trigger);
        }
    }
}