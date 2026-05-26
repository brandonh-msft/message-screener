using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using MessageScreener.Audit;
using MessageScreener.Api;
using MessageScreener.Api.Logging;
using MessageScreener.Contracts;
using MessageScreener.Orchestration;
using MessageScreener.ReviewDelivery;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

const string ServiceName = "MessageScreener.Api";
const string ServiceVersion = "0.1.0";
const string ForwardMessageActionCommandId = "forwardToMessageScreener";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services
    .AddOptions<MessageScreenerAgentOptions>()
    .BindConfiguration(MessageScreenerAgentOptions.SectionName);
builder.Services
    .AddOptions<MessageScreenerCopilotOptions>()
    .BindConfiguration(MessageScreenerCopilotOptions.SectionName);
builder.Services
    .AddOptions<MessageScreenerTeamsOptions>()
    .BindConfiguration(MessageScreenerTeamsOptions.SectionName);
builder.Services
    .AddOptions<MessageScreenerAuditOptions>()
    .BindConfiguration(MessageScreenerAuditOptions.SectionName);
builder.Services
    .AddOptions<M365TokenProviderOptions>()
    .BindConfiguration(M365TokenProviderOptions.SectionName);
builder.Services.AddSingleton<IInboundEventStore, InMemoryInboundEventStore>();
builder.Services.AddSingleton<IForwardAuditStore, InMemoryForwardAuditStore>();
builder.Services.AddSingleton<ITriggerPolicy, TeamsTriggerPolicy>();
builder.Services.AddScoped<IMessageIntakeService, MessageIntakeService>();
builder.Services.AddSingleton<ICommunicationTwinService, CommunicationTwinService>();
builder.Services.AddSingleton<ICopilotReplyDraftingService, CopilotReplyDraftingService>();
builder.Services.AddSingleton<ICopilotReadinessService, CopilotReadinessService>();
builder.Services.AddSingleton<ICallerAutoResponseComposer, CallerAutoResponseComposer>();
builder.Services.AddSingleton<IGhcpAgentHarness, GhcpAgentHarness>();
builder.Services.AddSingleton<IM365TokenProvider, M365TokenProvider>();
builder.Services.AddSingleton<IMcpCredentialBridge, McpCredentialBridge>();
builder.Services.AddSingleton<IPersonalReviewConversationRegistry, InMemoryPersonalReviewConversationRegistry>();
builder.Services.AddScoped<ITeamsMessageClient, BotConnectorMessageClient>();
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

app.MapGet("/api/audit/forwards", async (
    HttpRequest request,
    IForwardAuditStore forwardAuditStore,
    IOptions<MessageScreenerAuditOptions> auditOptions,
    CancellationToken cancellationToken) =>
{
    string? configuredKey = auditOptions.Value.OwnerReadApiKey;
    if (string.IsNullOrWhiteSpace(configuredKey))
    {
        return Results.NotFound();
    }

    if (!request.Headers.TryGetValue("X-MessageScreener-Owner-Key", out var providedKey) ||
        !string.Equals(providedKey.ToString(), configuredKey, StringComparison.Ordinal))
    {
        return Results.Forbid();
    }

    int limit = 50;
    if (request.Query.TryGetValue("limit", out var limitValue) &&
        int.TryParse(limitValue.ToString(), out int parsedLimit))
    {
        limit = Math.Clamp(parsedLimit, 1, 200);
    }

    IReadOnlyList<ForwardAuditEntry> entries = await forwardAuditStore.GetRecentAsync(limit, cancellationToken);
    return Results.Ok(entries);
});

app.MapGet("/api/readiness/copilot", async (
    HttpRequest request,
    ICopilotReadinessService copilotReadinessService,
    IOptions<MessageScreenerAuditOptions> auditOptions,
    CancellationToken cancellationToken) =>
{
    string? configuredKey = auditOptions.Value.OwnerReadApiKey;
    if (string.IsNullOrWhiteSpace(configuredKey))
    {
        return Results.NotFound();
    }

    if (!request.Headers.TryGetValue("X-MessageScreener-Owner-Key", out var providedKey) ||
        !string.Equals(providedKey.ToString(), configuredKey, StringComparison.Ordinal))
    {
        return Results.Forbid();
    }

    CopilotReadinessReport report = await copilotReadinessService.EvaluateAsync(cancellationToken);
    if (report.Ready)
    {
        return Results.Ok(report);
    }

    return Results.Json(report, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapPost("/api/intake/forward", async (
    HttpRequest request,
    IMessageIntakeService intakeService,
    IForwardAuditStore forwardAuditStore,
    ICommunicationTwinService communicationTwinService,
    ICallerAutoResponseComposer callerAutoResponseComposer,
    IReviewDeliveryService reviewDeliveryService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    using Activity activity = new Activity(ServiceName + ".ForwardIntake").Start();
    ILogger logger = loggerFactory.CreateLogger("MessageScreener.ForwardIntake");

    ForwardedMessageIntakeRequest? forwardRequest = await JsonSerializer.DeserializeAsync<ForwardedMessageIntakeRequest>(
        request.Body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
        cancellationToken);

    if (forwardRequest is null)
    {
        return Results.BadRequest();
    }

    TeamsInboundMessage forwardMessage = CreateInboundMessage(forwardRequest);

    InboundProcessingOutcome result = await ProcessInboundMessageAsync(
        forwardMessage,
        intakeService,
        forwardAuditStore,
        communicationTwinService,
        callerAutoResponseComposer,
        reviewDeliveryService,
        logger,
        cancellationToken);

    return Results.Ok(result.IntakeResult);
});

app.MapPost("/api/messages", async (
    HttpRequest request,
    IHttpClientFactory httpClientFactory,
    IOptions<MessageScreenerTeamsOptions> teamsOptions,
    IMessageIntakeService intakeService,
    IForwardAuditStore forwardAuditStore,
    IPersonalReviewConversationRegistry personalReviewConversationRegistry,
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
    string? conversationType = GetNestedJsonString(root, "conversation", "conversationType");
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
        ForwardedMessageIntakeRequest? forwardedRequest = TryParseForwardedMessageFromInvoke(root);
        if (forwardedRequest is null)
        {
            return Results.Ok(CreateComposeExtensionStatus(
                "Forwarding could not capture the full message context. Open your personal Message Screener chat and paste the message manually."));
        }

        TeamsInboundMessage forwardedMessage = CreateInboundMessage(forwardedRequest);

        try
        {
            InboundProcessingOutcome processingOutcome = await ProcessInboundMessageAsync(
                forwardedMessage,
                intakeService,
                forwardAuditStore,
                communicationTwinService,
                callerAutoResponseComposer,
                reviewDeliveryService,
                logger,
                cancellationToken);

            string statusText = CreateComposeExtensionStatusText(processingOutcome);

            return Results.Ok(CreateComposeExtensionStatus(statusText));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process composeExtension submit action.");
            return Results.Ok(CreateComposeExtensionStatus(
                "Message Screener is temporarily unavailable for this action. Open your personal Message Screener chat and paste the message manually."));
        }
    }

    if (!string.Equals(activityType, "message", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(incomingText))
    {
        return Results.Ok();
    }

    if (string.Equals(conversationType, "personal", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(conversationId) &&
        !string.IsNullOrWhiteSpace(serviceUrl))
    {
        personalReviewConversationRegistry.Remember(conversationId, serviceUrl);
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

app.MapControllers();

ILogger startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MessageScreener.Startup");
AppLog.ServiceStarted(startupLogger, ServiceName, ServiceVersion, app.Environment.EnvironmentName);

app.Run();

static async ValueTask<InboundProcessingOutcome> ProcessInboundMessageAsync(
    TeamsInboundMessage message,
    IMessageIntakeService intakeService,
    IForwardAuditStore forwardAuditStore,
    ICommunicationTwinService communicationTwinService,
    ICallerAutoResponseComposer callerAutoResponseComposer,
    IReviewDeliveryService reviewDeliveryService,
    ILogger logger,
    CancellationToken cancellationToken)
{
    MessageIntakeResult intakeResult = await intakeService.IntakeAsync(message, cancellationToken);
    ReviewDeliveryResult deliveryResult = new(ReviewDeliveryStatus.NotAttempted, "not_required");

    AppLog.InboundIntakeProcessed(
        logger,
        intakeResult.Accepted,
        intakeResult.Duplicate,
        intakeResult.Trigger.ShouldCreateReview,
        intakeResult.ReasonCode,
        intakeResult.Trigger.ReasonCode);

    try
    {
        if (intakeResult.Accepted && intakeResult.Trigger.ShouldCreateReview)
        {
            CommunicationTwinProfile twinProfile = communicationTwinService.GetInitialProfile();
            var pendingApprovalReply = await callerAutoResponseComposer.ComposePendingApprovalReplyAsync(
                message,
                twinProfile,
                cancellationToken);

            deliveryResult = await reviewDeliveryService.SendPendingApprovalReplyAsync(message, pendingApprovalReply, cancellationToken);
        }

        await forwardAuditStore.AppendAsync(
            CreateForwardAuditEntry(message, intakeResult),
            cancellationToken);

        await intakeService.MarkCompletedAsync(intakeResult, cancellationToken);
        return new InboundProcessingOutcome(intakeResult, deliveryResult);
    }
    catch
    {
        await forwardAuditStore.AppendAsync(
            CreateForwardAuditEntry(message, intakeResult),
            cancellationToken);

        await intakeService.ResetAsync(intakeResult, cancellationToken);
        throw;
    }
}

static ForwardAuditEntry CreateForwardAuditEntry(TeamsInboundMessage message, MessageIntakeResult intakeResult)
{
    return new ForwardAuditEntry(
        AuditEventId: Guid.NewGuid().ToString("N"),
        RecordedAtUtc: DateTimeOffset.UtcNow,
        TenantId: message.TenantId,
        SourceConversationId: message.ConversationId,
        SourceMessageId: message.SourceMessageId,
        SenderDisplayName: message.SenderDisplayName,
        SenderIdentityKey: message.SenderIdentityKey,
        SenderIdentityKeyKind: message.SenderIdentityKeyKind,
        ProcessingState: intakeResult.ProcessingState,
        IntakeReasonCode: intakeResult.ReasonCode,
        ReviewRequested: intakeResult.Trigger.ShouldCreateReview);
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

static ForwardedMessageIntakeRequest? TryParseForwardedMessageFromInvoke(JsonElement root)
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

    string? senderAadObjectId = GetJsonPathString(messagePayload.Value, "from", "user", "id");
    string? teamsSenderId = GetNestedJsonString(messagePayload.Value, "from", "id");
    string senderDisplayName = FirstNonEmpty(
        GetJsonPathString(messagePayload.Value, "from", "user", "displayName"),
        GetNestedJsonString(messagePayload.Value, "from", "name"),
        "Unknown sender") ?? "Unknown sender";

    SenderIdentityKeyKind senderIdentityKeyKind;
    string? senderIdentityKey;
    if (!string.IsNullOrWhiteSpace(senderAadObjectId))
    {
        senderIdentityKeyKind = SenderIdentityKeyKind.AadObjectId;
        senderIdentityKey = senderAadObjectId;
    }
    else if (!string.IsNullOrWhiteSpace(teamsSenderId))
    {
        senderIdentityKeyKind = SenderIdentityKeyKind.TeamsSenderId;
        senderIdentityKey = teamsSenderId;
    }
    else
    {
        senderIdentityKeyKind = SenderIdentityKeyKind.Unresolved;
        senderIdentityKey = null;
    }

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

    return new ForwardedMessageIntakeRequest(
        TenantId: tenantId,
        ConversationId: conversationId,
        SourceMessageId: messageId,
        SenderDisplayName: senderDisplayName,
        SenderIdentityKey: senderIdentityKey,
        SenderIdentityKeyKind: senderIdentityKeyKind,
        BodyPlainText: bodyText,
        Scope: scope,
        IsAtMention: true,
        OccurredAtUtc: DateTimeOffset.UtcNow);
}

    static TeamsInboundMessage CreateInboundMessage(ForwardedMessageIntakeRequest request)
    {
        return new TeamsInboundMessage(
        EventId: $"teams-action:{request.ConversationId}:{request.SourceMessageId}",
        TenantId: request.TenantId,
        ConversationId: request.ConversationId,
        SourceMessageId: request.SourceMessageId,
        SenderDisplayName: request.SenderDisplayName,
        SenderIdentityKey: request.SenderIdentityKey,
        SenderIdentityKeyKind: request.SenderIdentityKeyKind,
        BodyPlainText: request.BodyPlainText,
        Scope: request.Scope,
        IsAtMention: request.IsAtMention,
        OccurredAtUtc: request.OccurredAtUtc);
    }

static object CreateComposeExtensionStatus(string text)
{
    return new
    {
        composeExtension = new
        {
            type = "message",
            text,
        }
    };
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

static string CreateComposeExtensionStatusText(InboundProcessingOutcome outcome)
{
    MessageIntakeResult intakeResult = outcome.IntakeResult;
    ReviewDeliveryResult deliveryResult = outcome.ReviewDeliveryResult;

    if (intakeResult.ProcessingState == MessageProcessingState.DuplicateInFlight)
    {
        return "Message is already being processed by Message Screener.";
    }

    if (intakeResult.ProcessingState == MessageProcessingState.DuplicateCompleted)
    {
        return "Message was already forwarded to Message Screener.";
    }

    if (!intakeResult.Trigger.ShouldCreateReview)
    {
        return "Message was received but did not meet screening criteria.";
    }

    return deliveryResult.Status switch
    {
        ReviewDeliveryStatus.Delivered => "Message forwarded to Message Screener.",
        ReviewDeliveryStatus.SkippedAutoReplyDisabled => "Forward received, but automatic review delivery is disabled.",
        ReviewDeliveryStatus.SkippedMissingConversationId => "Forward received, but personal Message Screener destination is not configured yet. Open personal chat and send 'help'.",
        ReviewDeliveryStatus.SkippedMissingServiceUrl => "Forward received, but bot service URL is missing for delivery. Open personal chat and send 'help'.",
        ReviewDeliveryStatus.FailedToDeliver => "Forward received, but delivery to Message Screener failed. Please retry from message actions.",
        _ => "Forward received.",
    };
}

sealed record InboundProcessingOutcome(
    MessageIntakeResult IntakeResult,
    ReviewDeliveryResult ReviewDeliveryResult);