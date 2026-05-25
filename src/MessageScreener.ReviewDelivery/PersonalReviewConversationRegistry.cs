using System.Threading;

namespace MessageScreener.ReviewDelivery;

public interface IPersonalReviewConversationRegistry
{
    void Remember(string conversationId);

    string? GetCurrent();
}

public sealed class InMemoryPersonalReviewConversationRegistry : IPersonalReviewConversationRegistry
{
    private string? _currentConversationId;

    public void Remember(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        Interlocked.Exchange(ref _currentConversationId, conversationId);
    }

    public string? GetCurrent()
    {
        return Volatile.Read(ref _currentConversationId);
    }
}
