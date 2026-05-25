using MessageScreener.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MessageScreener.ReviewDelivery
{
    public interface IReviewDeliveryService
    {
        ValueTask SendPendingApprovalReplyAsync(
            TeamsInboundMessage message,
            string pendingApprovalText,
            CancellationToken cancellationToken);
    }

    public sealed class ReviewDeliveryService(ILogger<ReviewDeliveryService> logger) : IReviewDeliveryService
    {
        private readonly ITeamsMessageClient _teamsMessageClient = null!;
        private readonly MessageScreenerTeamsOptions _options = null!;
        private readonly IPersonalReviewConversationRegistry _conversationRegistry = null!;

        public ReviewDeliveryService(
            ITeamsMessageClient teamsMessageClient,
            IPersonalReviewConversationRegistry conversationRegistry,
            IOptions<MessageScreenerTeamsOptions> options,
            ILogger<ReviewDeliveryService> logger) : this(logger)
        {
            _teamsMessageClient = teamsMessageClient;
            _conversationRegistry = conversationRegistry;
            _options = options.Value;
        }

        public ValueTask SendPendingApprovalReplyAsync(
            TeamsInboundMessage message,
            string pendingApprovalText,
            CancellationToken cancellationToken)
            => SendPendingApprovalReplyCoreAsync(message, pendingApprovalText, cancellationToken);

        private async ValueTask SendPendingApprovalReplyCoreAsync(
            TeamsInboundMessage message,
            string pendingApprovalText,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? targetConversationId = _options.PersonalReviewConversationId;
            string? targetServiceUrl = _options.BotServiceUrl;

            PersonalReviewConversationContext? remembered = _conversationRegistry.GetCurrent();
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
                return;
            }

            if (string.IsNullOrWhiteSpace(targetConversationId))
            {
                ReviewDeliveryLog.CallerAutoReplySkipped(logger, message.ConversationId, "missing_personal_review_conversation_id");
                return;
            }

            if (string.IsNullOrWhiteSpace(targetServiceUrl))
            {
                ReviewDeliveryLog.CallerAutoReplySkipped(logger, targetConversationId, "missing_bot_service_url");
                return;
            }

            try
            {
                await _teamsMessageClient.SendMessageAsync(
                    targetServiceUrl,
                    targetConversationId,
                    pendingApprovalText,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                ReviewDeliveryLog.CallerAutoReplyFailed(logger, targetConversationId, ex.Message);
                return;
            }

            ReviewDeliveryLog.CallerAutoReplyPrepared(
                logger,
                targetConversationId,
                message.EventId,
                pendingApprovalText);
        }
    }
}