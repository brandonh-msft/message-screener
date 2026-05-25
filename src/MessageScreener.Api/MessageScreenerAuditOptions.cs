namespace MessageScreener.Api;

public sealed class MessageScreenerAuditOptions
{
    public const string SectionName = "MessageScreener:Audit";

    public string? OwnerReadApiKey { get; init; }
}
