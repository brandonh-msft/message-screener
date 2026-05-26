namespace MessageScreener.Api;

public sealed class MessageScreenerSkillOptions
{
    public const string SectionName = "MessageScreener:Skill";

    public string? AppId { get; init; }

    public string? PublicBaseUrl { get; init; }
}
