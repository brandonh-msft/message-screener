using System.Collections.Concurrent;

namespace MessageScreener.Orchestration;

public sealed class InMemoryInboundEventStore : IInboundEventStore
{
    private readonly ConcurrentDictionary<string, byte> _seenEventIds = new(StringComparer.Ordinal);

    public ValueTask<bool> TryMarkProcessedAsync(string eventId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var wasAdded = _seenEventIds.TryAdd(eventId, 0);
        return ValueTask.FromResult(wasAdded);
    }
}
