using System.Text.Json.Serialization;

namespace MessageScreener.Api;

public sealed class GraphChangeNotificationEnvelope
{
    [JsonPropertyName("value")]
    public List<GraphChangeNotification> Value { get; init; } = [];
}

public sealed class GraphChangeNotification
{
    [JsonPropertyName("subscriptionId")]
    public string? SubscriptionId { get; init; }

    [JsonPropertyName("changeType")]
    public string? ChangeType { get; init; }

    [JsonPropertyName("resource")]
    public string? Resource { get; init; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; init; }

    [JsonPropertyName("clientState")]
    public string? ClientState { get; init; }
}
