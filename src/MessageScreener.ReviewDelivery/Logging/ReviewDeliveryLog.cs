using Microsoft.Extensions.Logging;

namespace MessageScreener.ReviewDelivery;

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
}
