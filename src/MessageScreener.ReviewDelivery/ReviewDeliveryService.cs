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

        public ReviewDeliveryService(
            ITeamsMessageClient teamsMessageClient,
            IOptions<MessageScreenerTeamsOptions> options,
            ILogger<ReviewDeliveryService> logger) : this(logger)
        {
            _teamsMessageClient = teamsMessageClient;
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

            await _teamsMessageClient.SendMessageAsync(
                targetConversationId,
                pendingApprovalText,
                cancellationToken);

            ReviewDeliveryLog.CallerAutoReplyPrepared(
                logger,
                targetConversationId,
                message.EventId,
                pendingApprovalText);
        }
    }
}