using System.Collections.Concurrent;

namespace MessageScreener.Orchestration
{
    public sealed class InMemoryInboundEventStore : IInboundEventStore
    {
        private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromHours(24);

        private readonly ConcurrentDictionary<string, TrackedInboundEvent> _trackedEvents = new(StringComparer.Ordinal);

        public ValueTask<InboundEventRegistrationResult> TryBeginProcessingAsync(
            string deduplicationKey,
            DateTimeOffset observedAtUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DateTimeOffset cutoff = observedAtUtc.Subtract(DeduplicationWindow);

            while (true)
            {
                if (!_trackedEvents.TryGetValue(deduplicationKey, out TrackedInboundEvent? existing))
                {
                    var created = new TrackedInboundEvent(observedAtUtc, Completed: false);
                    if (_trackedEvents.TryAdd(deduplicationKey, created))
                    {
                        return ValueTask.FromResult(new InboundEventRegistrationResult(
                            deduplicationKey,
                            InboundEventRegistrationState.Accepted,
                            Accepted: true));
                    }

                    continue;
                }

                if (existing.ObservedAtUtc < cutoff)
                {
                    var refreshed = new TrackedInboundEvent(observedAtUtc, Completed: false);
                    if (_trackedEvents.TryUpdate(deduplicationKey, refreshed, existing))
                    {
                        return ValueTask.FromResult(new InboundEventRegistrationResult(
                            deduplicationKey,
                            InboundEventRegistrationState.Accepted,
                            Accepted: true));
                    }

                    continue;
                }

                InboundEventRegistrationState state = existing.Completed
                    ? InboundEventRegistrationState.DuplicateCompleted
                    : InboundEventRegistrationState.DuplicateInFlight;

                return ValueTask.FromResult(new InboundEventRegistrationResult(
                    deduplicationKey,
                    state,
                    Accepted: false));
            }
        }

        public ValueTask MarkCompletedAsync(string deduplicationKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (_trackedEvents.TryGetValue(deduplicationKey, out TrackedInboundEvent? existing))
            {
                if (existing.Completed)
                {
                    break;
                }

                var completed = existing with { Completed = true };
                if (_trackedEvents.TryUpdate(deduplicationKey, completed, existing))
                {
                    break;
                }
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(string deduplicationKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _trackedEvents.TryRemove(deduplicationKey, out _);
            return ValueTask.CompletedTask;
        }

        private sealed record TrackedInboundEvent(DateTimeOffset ObservedAtUtc, bool Completed);
    }
}