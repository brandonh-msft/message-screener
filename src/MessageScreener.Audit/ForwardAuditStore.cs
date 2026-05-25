using System.Collections.Concurrent;
using MessageScreener.Contracts;

namespace MessageScreener.Audit;

public sealed record ForwardAuditEntry(
    string AuditEventId,
    DateTimeOffset RecordedAtUtc,
    string TenantId,
    string SourceConversationId,
    string SourceMessageId,
    string SenderDisplayName,
    string? SenderIdentityKey,
    SenderIdentityKeyKind SenderIdentityKeyKind,
    MessageProcessingState ProcessingState,
    string IntakeReasonCode,
    bool ReviewRequested);

public interface IForwardAuditStore
{
    ValueTask AppendAsync(ForwardAuditEntry entry, CancellationToken cancellationToken);
}

public sealed class InMemoryForwardAuditStore : IForwardAuditStore
{
    private readonly ConcurrentQueue<ForwardAuditEntry> _entries = new();

    public ValueTask AppendAsync(ForwardAuditEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries.Enqueue(entry);
        return ValueTask.CompletedTask;
    }
}
