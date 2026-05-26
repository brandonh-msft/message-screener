using Microsoft.Extensions.Logging;

namespace MessageScreener.ReviewDelivery;

public interface IPersonalReviewConversationBootstrapper
{
    ValueTask<PersonalReviewConversationContext> EnsureConversationAsync(
        string serviceUrl,
        string invokingUserId,
        string invokingUserDisplayName,
        string botId,
        CancellationToken cancellationToken);
}

public sealed class PersonalReviewConversationBootstrapper(
    ITeamsMessageClient teamsMessageClient,
    IPersonalReviewConversationRegistry conversationRegistry,
    ILogger<PersonalReviewConversationBootstrapper> logger) : IPersonalReviewConversationBootstrapper
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<PersonalReviewConversationContext> EnsureConversationAsync(
        string serviceUrl,
        string invokingUserId,
        string invokingUserDisplayName,
        string botId,
        CancellationToken cancellationToken)
    {
        PersonalReviewConversationContext? current = await conversationRegistry.GetCurrentAsync(cancellationToken);
        if (current is not null)
        {
            return current;
        }

        if (string.IsNullOrWhiteSpace(serviceUrl) ||
            string.IsNullOrWhiteSpace(invokingUserId) ||
            string.IsNullOrWhiteSpace(botId))
        {
            throw new InvalidOperationException("Unable to bootstrap personal review conversation without Teams routing details.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            current = await conversationRegistry.GetCurrentAsync(cancellationToken);
            if (current is not null)
            {
                return current;
            }

            ReviewDeliveryLog.PersonalReviewConversationBootstrapStarting(
                logger,
                invokingUserId,
                !string.IsNullOrWhiteSpace(serviceUrl));

            PersonalReviewConversationContext created = await teamsMessageClient.CreatePersonalConversationAsync(
                serviceUrl,
                botId,
                invokingUserId,
                invokingUserDisplayName,
                cancellationToken);

            await conversationRegistry.RememberAsync(created, cancellationToken);

            ReviewDeliveryLog.PersonalReviewConversationBootstrapCompleted(
                logger,
                created.ConversationId,
                invokingUserId);

            return created;
        }
        finally
        {
            _gate.Release();
        }
    }
}
