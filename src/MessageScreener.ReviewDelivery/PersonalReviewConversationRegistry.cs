using System.Threading;

namespace MessageScreener.ReviewDelivery;

public interface IPersonalReviewConversationRegistry
{
    void Remember(string conversationId, string serviceUrl);

    PersonalReviewConversationContext? GetCurrent();
}

public sealed record PersonalReviewConversationContext(string ConversationId, string ServiceUrl);

public sealed class InMemoryPersonalReviewConversationRegistry : IPersonalReviewConversationRegistry
{
    private PersonalReviewConversationContext? _current;

    public void Remember(string conversationId, string serviceUrl)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(serviceUrl))
        {
            return;
        }

        Interlocked.Exchange(ref _current, new PersonalReviewConversationContext(conversationId, serviceUrl));
    }

    public PersonalReviewConversationContext? GetCurrent()
    {
        return Volatile.Read(ref _current);
    }
}
