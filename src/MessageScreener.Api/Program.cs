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
const string ForwardMessageActionCommandId = "forwardToMessageScreener";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services
    .AddOptions<MessageScreenerAgentOptions>()
    .BindConfiguration(MessageScreenerAgentOptions.SectionName);
builder.Services
    .AddOptions<MessageScreenerTeamsOptions>()
    .BindConfiguration(MessageScreenerTeamsOptions.SectionName);
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

app.MapPost("/api/intake/forward", async (
    HttpRequest request,
    IMessageIntakeService intakeService,
    ICommunicationTwinService communicationTwinService,
    ICallerAutoResponseComposer callerAutoResponseComposer,
    IReviewDeliveryService reviewDeliveryService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    using Activity activity = new Activity(ServiceName + ".ForwardIntake").Start();
    ILogger logger = loggerFactory.CreateLogger("MessageScreener.ForwardIntake");

    TeamsInboundMessage? forwardMessage = await JsonSerializer.DeserializeAsync<TeamsInboundMessage>(
        request.Body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
        cancellationToken);

    if (forwardMessage is null)
    {
        return Results.BadRequest();
    }

    MessageIntakeResult result = await ProcessInboundMessageAsync(
        forwardMessage,
        intakeService,
        communicationTwinService,
        callerAutoResponseComposer,
        reviewDeliveryService,
        logger,
        cancellationToken);

    return Results.Ok(result);
});

app.MapPost("/api/messages", async (
    HttpRequest request,
    IHttpClientFactory httpClientFactory,
    IOptions<MessageScreenerTeamsOptions> teamsOptions,
    IMessageIntakeService intakeService,
    ICommunicationTwinService communicationTwinService,
    ICallerAutoResponseComposer callerAutoResponseComposer,
    IReviewDeliveryService reviewDeliveryService,
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
    string? invokeName = GetJsonString(root, "name");
    string? serviceUrl = GetJsonString(root, "serviceUrl");
    string? conversationId = GetNestedJsonString(root, "conversation", "id");
    string? fromId = GetNestedJsonString(root, "from", "id");
    string? recipientId = GetNestedJsonString(root, "recipient", "id");

    AppLog.BotWebhookProcessed(
        logger,
        activityType ?? "unknown",
        activityId ?? "unknown",
        !string.IsNullOrWhiteSpace(incomingText));

    if (string.Equals(activityType, "invoke", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(invokeName, "composeExtension/submitAction", StringComparison.OrdinalIgnoreCase))
    {
        TeamsInboundMessage? forwardedMessage = TryParseForwardedMessageFromInvoke(root);
        if (forwardedMessage is null)
        {
            return Results.BadRequest();
        }

        MessageIntakeResult intakeResult = await ProcessInboundMessageAsync(
            forwardedMessage,
            intakeService,
            communicationTwinService,
            callerAutoResponseComposer,
            reviewDeliveryService,
            logger,
            cancellationToken);

        string statusText = intakeResult.Duplicate
            ? "Message was already forwarded to Message Screener."
            : "Message forwarded to Message Screener.";

        return Results.Ok(new
        {
            composeExtension = new
            {
                type = "message",
                text = statusText,
            }
        });
    }

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

    AppLog.InboundIntakeProcessed(
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

static string? GetJsonPathString(JsonElement root, params string[] path)
{
    JsonElement current = root;
    foreach (string segment in path)
    {
        if (current.ValueKind != JsonValueKind.Object ||
            !current.TryGetProperty(segment, out JsonElement next))
        {
            return null;
        }

        current = next;
    }

    if (current.ValueKind != JsonValueKind.String)
    {
        return null;
    }

    return current.GetString();
}

static JsonElement? GetObjectProperty(JsonElement root, string propertyName)
{
    if (!root.TryGetProperty(propertyName, out JsonElement propertyValue) ||
        propertyValue.ValueKind != JsonValueKind.Object)
    {
        return null;
    }

    return propertyValue;
}

static JsonElement? GetObjectPath(JsonElement root, params string[] path)
{
    JsonElement current = root;
    foreach (string segment in path)
    {
        if (current.ValueKind != JsonValueKind.Object ||
            !current.TryGetProperty(segment, out JsonElement next))
        {
            return null;
        }

        current = next;
    }

    if (current.ValueKind != JsonValueKind.Object)
    {
        return null;
    }

    return current;
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

static TeamsInboundMessage? TryParseForwardedMessageFromInvoke(JsonElement root)
{
    if (!root.TryGetProperty("value", out JsonElement valueElement) ||
        valueElement.ValueKind != JsonValueKind.Object)
    {
        return null;
    }

    string? commandId = FirstNonEmpty(
        GetJsonString(valueElement, "commandId"),
        GetJsonPathString(valueElement, "data", "commandId"));

    if (!string.Equals(commandId, ForwardMessageActionCommandId, StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    JsonElement? messagePayload = GetObjectProperty(valueElement, "messagePayload") ??
        GetObjectPath(valueElement, "data", "messagePayload");

    if (messagePayload is null)
    {
        return null;
    }

    string? conversationId = FirstNonEmpty(
        GetNestedJsonString(messagePayload.Value, "conversation", "id"),
        GetNestedJsonString(root, "conversation", "id"));

    string? messageId = GetJsonString(messagePayload.Value, "id");

    if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(messageId))
    {
        return null;
    }

    string senderAadObjectId = FirstNonEmpty(
        GetJsonPathString(messagePayload.Value, "from", "user", "id"),
        GetNestedJsonString(messagePayload.Value, "from", "id"),
        string.Empty) ?? string.Empty;

    string bodyText = StripHtml(GetNestedJsonString(messagePayload.Value, "body", "content"));
    string tenantId = FirstNonEmpty(
        GetJsonPathString(root, "channelData", "tenant", "id"),
        GetNestedJsonString(root, "conversation", "tenantId"),
        string.Empty) ?? string.Empty;

    bool isOneOnOne = string.Equals(
        GetNestedJsonString(root, "conversation", "conversationType"),
        "personal",
        StringComparison.OrdinalIgnoreCase);

    ConversationScope scope = isOneOnOne ? ConversationScope.OneOnOne : ConversationScope.GroupChat;

    return new TeamsInboundMessage(
        EventId: $"teams-action:{conversationId}:{messageId}",
        TenantId: tenantId,
        ConversationId: conversationId,
        SenderAadObjectId: senderAadObjectId,
        BodyPlainText: bodyText,
        Scope: scope,
        IsAtMention: true,
        OccurredAtUtc: DateTimeOffset.UtcNow);
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