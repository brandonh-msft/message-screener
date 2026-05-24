﻿using MessageScreener.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MessageScreener.ReviewDelivery;

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

		if (!_options.SendAutomaticCallerReply)
		{
			ReviewDeliveryLog.CallerAutoReplySkipped(logger, message.ConversationId, "auto_reply_disabled");
			return;
		}

		if (string.IsNullOrWhiteSpace(message.ConversationId))
		{
			ReviewDeliveryLog.CallerAutoReplySkipped(logger, message.ConversationId, "missing_conversation_id");
			return;
		}

		await _teamsMessageClient.SendMessageAsync(
			message.ConversationId,
			pendingApprovalText,
			cancellationToken);

		ReviewDeliveryLog.CallerAutoReplyPrepared(
			logger,
			message.ConversationId,
			message.EventId,
			pendingApprovalText);
	}
}
