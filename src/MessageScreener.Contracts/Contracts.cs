namespace MessageScreener.Contracts
{
    public enum ConversationScope
    {
        OneOnOne = 0,
        GroupChat = 1,
    }

    public sealed record TeamsInboundMessage(
        string EventId,
        string TenantId,
        string ConversationId,
        string SenderAadObjectId,
        string BodyPlainText,
        ConversationScope Scope,
        bool IsAtMention,
        DateTimeOffset OccurredAtUtc);

    public sealed record TriggerEvaluationResult(
        bool ShouldCreateReview,
        string ReasonCode);

    public sealed record MessageIntakeResult(
        bool Accepted,
        bool Duplicate,
        string ReasonCode,
        TriggerEvaluationResult Trigger);

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