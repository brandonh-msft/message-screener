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
    }
}