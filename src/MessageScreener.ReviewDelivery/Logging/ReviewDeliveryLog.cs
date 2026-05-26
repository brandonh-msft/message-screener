using Microsoft.Extensions.Logging;

namespace MessageScreener.ReviewDelivery
{
    public static partial class ReviewDeliveryLog
    {
        [LoggerMessage(
            EventId = 2001,
            Level = LogLevel.Information,
            Message = "Prepared caller auto-reply for conversation {ConversationId} and event {EventId}. Message={PendingApprovalText}")]
        public static partial void CallerAutoReplyPrepared(
            ILogger logger,
            string conversationId,
            string eventId,
            string pendingApprovalText);

        [LoggerMessage(
            EventId = 2002,
            Level = LogLevel.Warning,
            Message = "Skipped caller auto-reply for conversation {ConversationId}. Reason={Reason}")]
        public static partial void CallerAutoReplySkipped(
            ILogger logger,
            string conversationId,
            string reason);

        [LoggerMessage(
            EventId = 2003,
            Level = LogLevel.Error,
            Message = "Failed caller auto-reply delivery for conversation {ConversationId}. Reason={Reason}")]
        public static partial void CallerAutoReplyFailed(
            ILogger logger,
            string conversationId,
            string reason);

        [LoggerMessage(
            EventId = 2004,
            Level = LogLevel.Information,
            Message = "Bootstrapping personal review conversation for user {UserId}. ServiceUrlPresent={HasServiceUrl}")]
        public static partial void PersonalReviewConversationBootstrapStarting(
            ILogger logger,
            string userId,
            bool hasServiceUrl);

        [LoggerMessage(
            EventId = 2005,
            Level = LogLevel.Information,
            Message = "Bootstrapped personal review conversation {ConversationId} for user {UserId}.")]
        public static partial void PersonalReviewConversationBootstrapCompleted(
            ILogger logger,
            string conversationId,
            string userId);

        [LoggerMessage(
            EventId = 2006,
            Level = LogLevel.Information,
            Message = "Loaded personal review conversation {ConversationId} from durable store.")]
        public static partial void PersonalReviewConversationLoaded(
            ILogger logger,
            string conversationId);

        [LoggerMessage(
            EventId = 2007,
            Level = LogLevel.Information,
            Message = "Persisted personal review conversation {ConversationId} to durable store.")]
        public static partial void PersonalReviewConversationPersisted(
            ILogger logger,
            string conversationId);

        [LoggerMessage(
            EventId = 2008,
            Level = LogLevel.Warning,
            Message = "Failed to load personal review conversation from durable store. Error={Error}")]
        public static partial void PersonalReviewConversationLoadFailed(
            ILogger logger,
            string error);

        [LoggerMessage(
            EventId = 2009,
            Level = LogLevel.Warning,
            Message = "Failed to persist personal review conversation {ConversationId}. Error={Error}")]
        public static partial void PersonalReviewConversationPersistFailed(
            ILogger logger,
            string conversationId,
            string error);
    }
}