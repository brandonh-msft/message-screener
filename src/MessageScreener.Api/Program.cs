using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using MessageScreener.Api;
using MessageScreener.Api.Logging;
using MessageScreener.Contracts;
using MessageScreener.Orchestration;
using MessageScreener.ReviewDelivery;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

const string ServiceName = "MessageScreener.Api";
const string ServiceVersion = "0.1.0";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services
    .AddOptions<MessageScreenerAgentOptions>()
    .BindConfiguration(MessageScreenerAgentOptions.SectionName);
builder.Services
    .AddOptions<MessageScreenerTeamsOptions>()
    .BindConfiguration(MessageScreenerTeamsOptions.SectionName);
builder.Services
    .AddOptions<GraphWebhookOptions>()
    .BindConfiguration(GraphWebhookOptions.SectionName);
builder.Services.AddSingleton<IInboundEventStore, InMemoryInboundEventStore>();
builder.Services.AddSingleton<ITriggerPolicy, TeamsTriggerPolicy>();
builder.Services.AddScoped<IMessageIntakeService, MessageIntakeService>();
builder.Services.AddSingleton<ICommunicationTwinService, CommunicationTwinService>();
builder.Services.AddSingleton<ICallerAutoResponseComposer, CallerAutoResponseComposer>();
builder.Services.AddSingleton<IGhcpAgentHarness, GhcpAgentHarness>();
builder.Services.AddSingleton(static serviceProvider =>
{
    MessageScreenerTeamsOptions options = serviceProvider
        .GetRequiredService<IOptions<MessageScreenerTeamsOptions>>()
        .Value;

    var credentialOptions = new DefaultAzureCredentialOptions();
    if (!string.IsNullOrWhiteSpace(options.ManagedIdentityClientId))
    {
        credentialOptions.ManagedIdentityClientId = options.ManagedIdentityClientId;
    }

    var credential = new DefaultAzureCredential(credentialOptions);
    return new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
});
builder.Services.AddScoped<ITeamsMessageClient, TeamsGraphMessageClient>();
builder.Services.AddScoped<IReviewDeliveryService, ReviewDeliveryService>();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<GraphSubscriptionHostedService>();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource =>
    {
        resource.AddService(
            serviceName: ServiceName,
            serviceVersion: ServiceVersion,
            serviceInstanceId: Environment.MachineName);
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource(ServiceName)
            .AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter(ServiceName)
            .AddOtlpExporter();
    });

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new
{
    service = ServiceName,
    status = "ok",
    utcTimestamp = DateTimeOffset.UtcNow,
}));

app.MapGet("/webhooks/graph", (HttpRequest request) =>
{
    if (request.Query.TryGetValue("validationToken", out var validationToken) &&
        !string.IsNullOrWhiteSpace(validationToken))
    {
        return Results.Text(validationToken.ToString(), "text/plain");
    }

    return Results.BadRequest();
});

app.MapPost("/webhooks/graph", async (
    HttpRequest request,
    GraphServiceClient graphServiceClient,
    IOptions<GraphWebhookOptions> graphWebhookOptions,
    IMessageIntakeService intakeService,
    ICommunicationTwinService communicationTwinService,
    ICallerAutoResponseComposer callerAutoResponseComposer,
    IReviewDeliveryService reviewDeliveryService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    using Activity activity = new Activity(ServiceName + ".GraphWebhook").Start();
    ILogger logger = loggerFactory.CreateLogger("MessageScreener.GraphWebhook");
    JsonDocument payload = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
    JsonElement root = payload.RootElement;

    // Graph subscriptions post a notification envelope with a top-level 'value' array.
    if (root.TryGetProperty("value", out JsonElement valueElement) && valueElement.ValueKind == JsonValueKind.Array)
    {
        var notifications = JsonSerializer.Deserialize<GraphChangeNotificationEnvelope>(
            root.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (notifications?.Value is null || notifications.Value.Count == 0)
        {
            AppLog.GraphNotificationBatchSkipped(logger, "empty_notification_batch");
            return Results.Ok();
        }

        GraphWebhookOptions webhookOptions = graphWebhookOptions.Value;
        string? expectedClientState = FirstNonEmpty(
            webhookOptions.ClientState,
            Environment.GetEnvironmentVariable("MessageScreener__GraphWebhook__ClientState"),
            Environment.GetEnvironmentVariable("MESSAGE_SCREENER_GRAPH_WEBHOOK_CLIENT_STATE"));

        foreach (GraphChangeNotification notification in notifications.Value)
        {
            if (!string.IsNullOrWhiteSpace(expectedClientState) &&
                !string.Equals(notification.ClientState, expectedClientState, StringComparison.Ordinal))
            {
                AppLog.GraphNotificationRejectedClientState(logger, notification.SubscriptionId ?? "unknown");
                continue;
            }

            if (!TryParseGraphChatMessageReference(notification.Resource, out string chatId, out string messageId))
            {
                AppLog.GraphNotificationBatchSkipped(logger, $"unsupported_resource:{notification.Resource}");
                continue;
            }

            try
            {
                var graphMessage = await graphServiceClient
                    .Chats[chatId]
                    .Messages[messageId]
                    .GetAsync(cancellationToken: cancellationToken);

                var chat = await graphServiceClient
                    .Chats[chatId]
                    .GetAsync(cancellationToken: cancellationToken);

                if (graphMessage is null)
                {
                    AppLog.GraphNotificationBatchSkipped(logger, $"missing_message:{chatId}/{messageId}");
                    continue;
                }

                ConversationScope scope = string.Equals(chat?.ChatType?.ToString(), "oneOnOne", StringComparison.OrdinalIgnoreCase)
                    ? ConversationScope.OneOnOne
                    : ConversationScope.GroupChat;

                var inboundMessage = new TeamsInboundMessage(
                    EventId: $"graph:{chatId}:{messageId}",
                    TenantId: notification.TenantId ?? string.Empty,
                    ConversationId: chatId,
                    SenderAadObjectId: graphMessage.From?.User?.Id ?? string.Empty,
                    BodyPlainText: StripHtml(graphMessage.Body?.Content),
                    Scope: scope,
                    IsAtMention: (graphMessage.Mentions?.Count ?? 0) > 0,
                    OccurredAtUtc: graphMessage.CreatedDateTime ?? DateTimeOffset.UtcNow);

                await ProcessInboundMessageAsync(
                    inboundMessage,
                    intakeService,
                    communicationTwinService,
                    callerAutoResponseComposer,
                    reviewDeliveryService,
                    logger,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                AppLog.GraphNotificationBatchSkipped(logger, $"graph_fetch_failed:{ex.Message}");
            }
        }

        return Results.Ok();
    }

    // Backward-compatible direct contract payload path.
    TeamsInboundMessage? directMessage = JsonSerializer.Deserialize<TeamsInboundMessage>(
        root.GetRawText(),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (directMessage is null)
    {
        return Results.BadRequest();
    }

    MessageIntakeResult directResult = await ProcessInboundMessageAsync(
        directMessage,
        intakeService,
        communicationTwinService,
        callerAutoResponseComposer,
        reviewDeliveryService,
        logger,
        cancellationToken);

    return Results.Ok(directResult);
});

app.MapPost("/api/messages", async (
    HttpRequest request,
    IHttpClientFactory httpClientFactory,
    IOptions<MessageScreenerTeamsOptions> teamsOptions,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    using Activity activity = new Activity(ServiceName + ".BotWebhook").Start();
    ILogger logger = loggerFactory.CreateLogger("MessageScreener.BotWebhook");

    JsonDocument payload = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
    JsonElement root = payload.RootElement;

    string? activityType = GetJsonString(root, "type");
    string? activityId = GetJsonString(root, "id");
    string? incomingText = GetJsonString(root, "text");
    string? serviceUrl = GetJsonString(root, "serviceUrl");
    string? conversationId = GetNestedJsonString(root, "conversation", "id");
    string? fromId = GetNestedJsonString(root, "from", "id");
    string? recipientId = GetNestedJsonString(root, "recipient", "id");

    AppLog.BotWebhookProcessed(
        logger,
        activityType ?? "unknown",
        activityId ?? "unknown",
        !string.IsNullOrWhiteSpace(incomingText));

    if (!string.Equals(activityType, "message", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(incomingText))
    {
        return Results.Ok();
    }

    string normalizedText = incomingText.Trim();
    if (!string.Equals(normalizedText, "help", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Ok();
    }

    if (string.IsNullOrWhiteSpace(serviceUrl) ||
        string.IsNullOrWhiteSpace(conversationId) ||
        string.IsNullOrWhiteSpace(fromId) ||
        string.IsNullOrWhiteSpace(recipientId))
    {
        AppLog.BotReplySkippedMissingFields(logger);
        return Results.Ok();
    }

    string responseText = "Message Screener is online. This preview bot currently supports the 'help' command and logs inbound bot activities.";
    string? error = await TrySendBotReplyAsync(
        httpClientFactory,
        teamsOptions.Value,
        serviceUrl,
        conversationId,
        activityId,
        fromId,
        recipientId,
        responseText,
        cancellationToken);

    if (error is null)
    {
        AppLog.BotReplySent(logger, conversationId);
        return Results.Ok();
    }

    AppLog.BotReplyFailed(logger, error);
    return Results.Ok();
});

ILogger startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MessageScreener.Startup");
AppLog.ServiceStarted(startupLogger, ServiceName, ServiceVersion, app.Environment.EnvironmentName);

app.Run();

static async ValueTask<MessageIntakeResult> ProcessInboundMessageAsync(
    TeamsInboundMessage message,
    IMessageIntakeService intakeService,
    ICommunicationTwinService communicationTwinService,
    ICallerAutoResponseComposer callerAutoResponseComposer,
    IReviewDeliveryService reviewDeliveryService,
    ILogger logger,
    CancellationToken cancellationToken)
{
    MessageIntakeResult intakeResult = await intakeService.IntakeAsync(message, cancellationToken);

    AppLog.GraphWebhookProcessed(
        logger,
        intakeResult.Accepted,
        intakeResult.Duplicate,
        intakeResult.Trigger.ShouldCreateReview,
        intakeResult.ReasonCode,
        intakeResult.Trigger.ReasonCode);

    if (intakeResult.Accepted && intakeResult.Trigger.ShouldCreateReview)
    {
        CommunicationTwinProfile twinProfile = communicationTwinService.GetInitialProfile();
        var pendingApprovalReply = callerAutoResponseComposer.ComposePendingApprovalReply(twinProfile.OwnerDisplayName);

        await reviewDeliveryService.SendPendingApprovalReplyAsync(message, pendingApprovalReply, cancellationToken);
    }

    return intakeResult;
}

static bool TryParseGraphChatMessageReference(string? resource, out string chatId, out string messageId)
{
    chatId = string.Empty;
    messageId = string.Empty;

    if (string.IsNullOrWhiteSpace(resource))
    {
        return false;
    }

    // Graph resources can appear as chats/{chatId}/messages/{messageId} or chats('{chatId}')/messages('{messageId}').
    Match directPathMatch = Regex.Match(resource, "chats/([^/]+)/messages/([^/?]+)", RegexOptions.IgnoreCase);
    if (directPathMatch.Success)
    {
        chatId = directPathMatch.Groups[1].Value;
        messageId = directPathMatch.Groups[2].Value;
        return true;
    }

    Match odataPathMatch = Regex.Match(resource, "chats\\('([^']+)'\\)/messages\\('([^']+)'\\)", RegexOptions.IgnoreCase);
    if (odataPathMatch.Success)
    {
        chatId = odataPathMatch.Groups[1].Value;
        messageId = odataPathMatch.Groups[2].Value;
        return true;
    }

    return false;
}

static string StripHtml(string? content)
{
    if (string.IsNullOrWhiteSpace(content))
    {
        return string.Empty;
    }

    return Regex.Replace(content, "<.*?>", string.Empty).Trim();
}

static string? FirstNonEmpty(params string?[] values)
{
    foreach (string? value in values)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    return null;
}

static string? GetJsonString(JsonElement root, string propertyName)
{
    if (!root.TryGetProperty(propertyName, out JsonElement propertyValue) ||
        propertyValue.ValueKind != JsonValueKind.String)
    {
        return null;
    }

    return propertyValue.GetString();
}

static string? GetNestedJsonString(JsonElement root, string parentPropertyName, string childPropertyName)
{
    if (!root.TryGetProperty(parentPropertyName, out JsonElement parentValue) ||
        parentValue.ValueKind != JsonValueKind.Object)
    {
        return null;
    }

    if (!parentValue.TryGetProperty(childPropertyName, out JsonElement childValue) ||
        childValue.ValueKind != JsonValueKind.String)
    {
        return null;
    }

    return childValue.GetString();
}

static async ValueTask<string?> TrySendBotReplyAsync(
    IHttpClientFactory httpClientFactory,
    MessageScreenerTeamsOptions options,
    string serviceUrl,
    string conversationId,
    string? activityId,
    string fromId,
    string recipientId,
    string messageText,
    CancellationToken cancellationToken)
{
    var credentialOptions = new DefaultAzureCredentialOptions();
    if (!string.IsNullOrWhiteSpace(options.ManagedIdentityClientId))
    {
        credentialOptions.ManagedIdentityClientId = options.ManagedIdentityClientId;
    }

    var credential = new DefaultAzureCredential(credentialOptions);
    AccessToken token;
    try
    {
        token = await credential.GetTokenAsync(
            new TokenRequestContext(["https://api.botframework.com/.default"]),
            cancellationToken);
    }
    catch (Exception ex)
    {
        return $"token_acquisition_failed: {ex.Message}";
    }

    var botReplyPayload = new
    {
        type = "message",
        text = messageText,
        replyToId = activityId,
        from = new { id = recipientId },
        recipient = new { id = fromId },
        conversation = new { id = conversationId }
    };

    string connectorUrl = $"{serviceUrl.TrimEnd('/')}/v3/conversations/{Uri.EscapeDataString(conversationId)}/activities";
    string jsonPayload = JsonSerializer.Serialize(botReplyPayload);
    using var requestMessage = new HttpRequestMessage(HttpMethod.Post, connectorUrl)
    {
        Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
    };
    requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

    using HttpClient client = httpClientFactory.CreateClient();
    using HttpResponseMessage response = await client.SendAsync(requestMessage, cancellationToken);
    if (response.IsSuccessStatusCode)
    {
        return null;
    }

    string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
    return $"connector_send_failed: {(int)response.StatusCode} {response.ReasonPhrase}; body={responseBody}";
}