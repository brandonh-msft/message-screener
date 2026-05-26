using MessageScreener.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MessageScreener.ReviewDelivery
{
    public enum ReviewDeliveryStatus
    {
        NotAttempted = 0,
        Delivered = 1,
        SkippedAutoReplyDisabled = 2,
        SkippedMissingConversationId = 3,
        SkippedMissingServiceUrl = 4,
        FailedToDeliver = 5,
    }

    public sealed record ReviewDeliveryResult(ReviewDeliveryStatus Status, string ReasonCode);

    public interface IReviewDeliveryService
    {
        ValueTask<ReviewDeliveryResult> SendPendingApprovalReplyAsync(
            TeamsInboundMessage message,
            string pendingApprovalText,
            CancellationToken cancellationToken);
    }

    public sealed class ReviewDeliveryService(
        ITeamsMessageClient teamsMessageClient,
        IPersonalReviewConversationRegistry conversationRegistry,
        IOptions<MessageScreenerTeamsOptions> options,
        ILogger<ReviewDeliveryService> logger) : IReviewDeliveryService
    {
        private readonly MessageScreenerTeamsOptions _options = options.Value;

        public ValueTask<ReviewDeliveryResult> SendPendingApprovalReplyAsync(
            TeamsInboundMessage message,
            string pendingApprovalText,
            CancellationToken cancellationToken)
            => SendPendingApprovalReplyCoreAsync(message, pendingApprovalText, cancellationToken);

        private async ValueTask<ReviewDeliveryResult> SendPendingApprovalReplyCoreAsync(
            TeamsInboundMessage message,
            string pendingApprovalText,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? targetConversationId = _options.PersonalReviewConversationId;
            string? targetServiceUrl = _options.BotServiceUrl;

            PersonalReviewConversationContext? remembered = await conversationRegistry.GetCurrentAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(targetConversationId))
            {
                targetConversationId = remembered?.ConversationId;
            }

            if (string.IsNullOrWhiteSpace(targetServiceUrl))
            {
                targetServiceUrl = remembered?.ServiceUrl;
            }

            if (!_options.SendAutomaticCallerReply)
            {
                ReviewDeliveryLog.CallerAutoReplySkipped(logger, targetConversationId ?? string.Empty, "auto_reply_disabled");
                return new ReviewDeliveryResult(ReviewDeliveryStatus.SkippedAutoReplyDisabled, "auto_reply_disabled");
            }

            if (string.IsNullOrWhiteSpace(targetConversationId))
            {
                ReviewDeliveryLog.CallerAutoReplySkipped(logger, message.ConversationId, "missing_personal_review_conversation_id");
                return new ReviewDeliveryResult(ReviewDeliveryStatus.SkippedMissingConversationId, "missing_personal_review_conversation_id");
            }

            if (string.IsNullOrWhiteSpace(targetServiceUrl))
            {
                ReviewDeliveryLog.CallerAutoReplySkipped(logger, targetConversationId, "missing_bot_service_url");
                return new ReviewDeliveryResult(ReviewDeliveryStatus.SkippedMissingServiceUrl, "missing_bot_service_url");
            }

            try
            {
                await teamsMessageClient.SendMessageAsync(
                    targetServiceUrl,
                    targetConversationId,
                    pendingApprovalText,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                ReviewDeliveryLog.CallerAutoReplyFailed(logger, targetConversationId, ex.Message);
                return new ReviewDeliveryResult(ReviewDeliveryStatus.FailedToDeliver, "connector_send_failed");
            }

            ReviewDeliveryLog.CallerAutoReplyPrepared(
                logger,
                targetConversationId,
                message.EventId,
                pendingApprovalText);

            return new ReviewDeliveryResult(ReviewDeliveryStatus.Delivered, "delivered");
        }
    }
}