namespace MessageScreener.Api.Logging
{
    internal static partial class AppLog
    {
        [LoggerMessage(
            EventId = 1000,
            Level = LogLevel.Information,
            Message = "{ServiceName} started. Version={ServiceVersion} Environment={EnvironmentName}")]
        public static partial void ServiceStarted(
            ILogger logger,
            string serviceName,
            string serviceVersion,
            string environmentName);

        [LoggerMessage(
            EventId = 1001,
            Level = LogLevel.Information,
            Message = "Inbound intake processed. accepted={Accepted} duplicate={Duplicate} shouldCreateReview={ShouldCreateReview} intakeReason={IntakeReason} triggerReason={TriggerReason}")]
        public static partial void InboundIntakeProcessed(
            ILogger logger,
            bool accepted,
            bool duplicate,
            bool shouldCreateReview,
            string intakeReason,
            string triggerReason);

        [LoggerMessage(
            EventId = 1002,
            Level = LogLevel.Information,
            Message = "Bot webhook processed. activityType={ActivityType} activityId={ActivityId} hasText={HasText}")]
        public static partial void BotWebhookProcessed(
            ILogger logger,
            string activityType,
            string activityId,
            bool hasText);

        [LoggerMessage(
            EventId = 1003,
            Level = LogLevel.Warning,
            Message = "Bot reply skipped because required Bot Framework fields were missing from activity payload.")]
        public static partial void BotReplySkippedMissingFields(ILogger logger);

        [LoggerMessage(
            EventId = 1004,
            Level = LogLevel.Information,
            Message = "Bot help reply sent to conversation {ConversationId}.")]
        public static partial void BotReplySent(ILogger logger, string conversationId);

        [LoggerMessage(
            EventId = 1005,
            Level = LogLevel.Error,
            Message = "Bot reply failed. {ErrorDetail}")]
        public static partial void BotReplyFailed(ILogger logger, string errorDetail);

    }
}