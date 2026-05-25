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
            Message = "Graph webhook processed. accepted={Accepted} duplicate={Duplicate} shouldCreateReview={ShouldCreateReview} intakeReason={IntakeReason} triggerReason={TriggerReason}")]
        public static partial void GraphWebhookProcessed(
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

        [LoggerMessage(
            EventId = 1006,
            Level = LogLevel.Information,
            Message = "Graph subscription created. id={SubscriptionId} resource={Resource} notificationUrl={NotificationUrl} expiresAt={ExpirationUtc}")]
        public static partial void GraphSubscriptionCreated(
            ILogger logger,
            string subscriptionId,
            string resource,
            string notificationUrl,
            DateTimeOffset expirationUtc);

        [LoggerMessage(
            EventId = 1007,
            Level = LogLevel.Information,
            Message = "Graph subscription renewed. id={SubscriptionId} expiresAt={ExpirationUtc}")]
        public static partial void GraphSubscriptionRenewed(
            ILogger logger,
            string subscriptionId,
            DateTimeOffset expirationUtc);

        [LoggerMessage(
            EventId = 1008,
            Level = LogLevel.Warning,
            Message = "Graph subscription skipped. reason={ReasonCode}")]
        public static partial void GraphSubscriptionSkipped(ILogger logger, string reasonCode);

        [LoggerMessage(
            EventId = 1009,
            Level = LogLevel.Warning,
            Message = "Graph notification rejected due to clientState mismatch. subscriptionId={SubscriptionId}")]
        public static partial void GraphNotificationRejectedClientState(ILogger logger, string subscriptionId);

        [LoggerMessage(
            EventId = 1010,
            Level = LogLevel.Warning,
            Message = "Graph notification batch item skipped. reason={ReasonCode}")]
        public static partial void GraphNotificationBatchSkipped(ILogger logger, string reasonCode);
    }
}