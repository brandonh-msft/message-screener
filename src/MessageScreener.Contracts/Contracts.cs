namespace MessageScreener.Contracts
{
    public enum ConversationScope
    {
        OneOnOne = 0,
        GroupChat = 1,
    }

    public enum SenderIdentityKeyKind
    {
        AadObjectId = 0,
        TeamsSenderId = 1,
        Unresolved = 2,
    }

    public sealed record TeamsInboundMessage(
        string EventId,
        string TenantId,
        string ConversationId,
        string SourceMessageId,
        string SenderDisplayName,
        string? SenderIdentityKey,
        SenderIdentityKeyKind SenderIdentityKeyKind,
        string BodyPlainText,
        ConversationScope Scope,
        bool IsAtMention,
        DateTimeOffset OccurredAtUtc);

    public sealed record ForwardedMessageIntakeRequest(
        string TenantId,
        string ConversationId,
        string SourceMessageId,
        string SenderDisplayName,
        string? SenderIdentityKey,
        SenderIdentityKeyKind SenderIdentityKeyKind,
        string BodyPlainText,
        ConversationScope Scope,
        bool IsAtMention,
        DateTimeOffset OccurredAtUtc);

    public sealed record TriggerEvaluationResult(
        bool ShouldCreateReview,
        string ReasonCode);

    public enum MessageProcessingState
    {
        Accepted = 0,
        DuplicateInFlight = 1,
        DuplicateCompleted = 2,
    }

    public sealed record MessageIntakeResult(
        bool Accepted,
        bool Duplicate,
        string ReasonCode,
        TriggerEvaluationResult Trigger,
        string DeduplicationKey,
        MessageProcessingState ProcessingState,
        bool CanRequeue);

    public sealed record CommunicationTwinProfile(
        string OwnerDisplayName,
        string PersonaSummary,
        string[] PreferredPhrases,
        string[] AvoidPhrases,
        string Tone);

    public sealed record McpServerRegistration(
        string ServerName,
        string Description,
        bool Enabled);

    public sealed record GhcpSkillDefinition(
        string SkillName,
        string Description,
        string[] TriggerKeywords,
        bool Enabled);
}