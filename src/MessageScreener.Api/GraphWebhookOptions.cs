namespace MessageScreener.Api;

public sealed class GraphWebhookOptions
{
    public const string SectionName = "MessageScreener:GraphWebhook";

    public bool Enabled { get; init; } = true;

    public bool AutoProvisionSubscription { get; init; } = true;

    public string Resource { get; init; } = "chats/getAllMessages";

    public string ChangeType { get; init; } = "created";

    public string? NotificationUrl { get; init; }

    public string? PublicBaseUrl { get; init; }

    public string? ClientState { get; init; }

    public string? SubscriptionId { get; init; }

    public int SubscriptionDurationMinutes { get; init; } = 60;
}
