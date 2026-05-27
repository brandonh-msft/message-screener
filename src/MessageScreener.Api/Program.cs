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
const string RewriteInUserVoiceSkillActivityId = "rewriteInUserVoice";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();
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
    .AddOptions<MessageScreenerSkillOptions>()
    .BindConfiguration(MessageScreenerSkillOptions.SectionName);
builder.Services.AddSingleton<IInboundEventStore, InMemoryInboundEventStore>();
builder.Services.AddSingleton<IForwardAuditStore, InMemoryForwardAuditStore>();
builder.Services.AddSingleton<ITriggerPolicy, TeamsTriggerPolicy>();
builder.Services.AddScoped<IMessageIntakeService, MessageIntakeService>();
builder.Services.AddSingleton<ICommunicationTwinService, CommunicationTwinService>();
builder.Services.AddSingleton<ICopilotReplyDraftingService, CopilotReplyDraftingService>();
builder.Services.AddSingleton<ICopilotReadinessService, CopilotReadinessService>();
builder.Services.AddSingleton<ICallerAutoResponseComposer, CallerAutoResponseComposer>();
builder.Services.AddSingleton<IGhcpAgentHarness, GhcpAgentHarness>();
builder.Services.AddSingleton<IPersonalReviewConversationRegistry, KeyVaultPersonalReviewConversationRegistry>();
builder.Services.AddSingleton<IPersonalReviewConversationBootstrapper, PersonalReviewConversationBootstrapper>();
builder.Services.AddSingleton<IForwardActionQueue, ForwardActionQueue>();
builder.Services.AddScoped<IForwardActionProcessor, ForwardActionProcessor>();
builder.Services.AddHostedService<ForwardActionBackgroundService>();
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

app.MapGet("/health", () => Results.Ok(new
{
    service = ServiceName,
    status = "ok",
    utcTimestamp = DateTimeOffset.UtcNow,
}));

app.MapGet("/privacy", () => Results.Text(
    "Message Screener processes message context to generate review-first drafts and skill responses. No automatic sends are performed.",
    "text/plain"));

app.MapGet("/swagger/v2/swagger.json", (HttpRequest request) =>
{
    string host = request.Host.Value ?? string.Empty;
    string scheme = string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";

    return Results.Json(BuildRewriteSwaggerV2Document(host, scheme));
});

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

app.MapPost("/api/voice/rewrite", async (
    CommunicationTwinRewriteRequest rewriteRequest,
    ICopilotReplyDraftingService copilotReplyDraftingService,
    ICommunicationTwinService communicationTwinService,
    IGhcpAgentHarness ghcpAgentHarness,
    CancellationToken cancellationToken) =>
{
    return await RewriteInUserVoiceAsync(
        rewriteRequest,
        copilotReplyDraftingService,
        communicationTwinService,
        ghcpAgentHarness,
        cancellationToken);
})
    .WithName("RewriteInUserVoice")
    .WithSummary("Rewrite suggested response in the operating user's voice")
    .Accepts<CommunicationTwinRewriteRequest>("application/json")
    .Produces<CommunicationTwinRewriteResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest);

app.MapPost("/api/skills/communication-twin/messages", async (
    HttpRequest request,
    ICopilotReplyDraftingService copilotReplyDraftingService,
    ICommunicationTwinService communicationTwinService,
    IGhcpAgentHarness ghcpAgentHarness,
    CancellationToken cancellationToken) =>
{
    return await RewriteInUserVoiceSkillAsync(
        request,
        copilotReplyDraftingService,
        communicationTwinService,
        ghcpAgentHarness,
        cancellationToken);
});

app.MapGet("/manifest/message-screener-communication-twin-skill-1.0.json", (
    HttpRequest request,
    IOptions<MessageScreenerTeamsOptions> teamsOptions,
    IOptions<MessageScreenerSkillOptions> skillOptions) =>
{
    string? appId = FirstNonEmpty(skillOptions.Value.AppId, teamsOptions.Value.ManagedIdentityClientId);
    if (string.IsNullOrWhiteSpace(appId))
    {
        return Results.Problem(
            title: "Skill manifest is unavailable",
            detail: "Configure MessageScreener:Skill:AppId to a single-tenant Entra app registration App ID for Copilot Studio skill validation.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (!Guid.TryParse(appId, out _))
    {
        return Results.Problem(
            title: "Skill manifest is unavailable",
            detail: "MessageScreener:Skill:AppId must be a valid GUID App ID.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    string baseUrl = ResolvePublicBaseUrl(request, skillOptions.Value.PublicBaseUrl);
    string endpointUrl = $"{baseUrl}/api/skills/communication-twin/messages";

    Dictionary<string, object?> manifest = new()
    {
        ["$schema"] = "https://schemas.botframework.com/schemas/skills/skill-manifest-2.0.0.json",
        ["$id"] = "MessageScreenerCommunicationTwinSkill",
        ["name"] = "Message Screener Communication Twin Skill",
        ["version"] = "1.0",
        ["description"] = "Rewrites a suggested response into the operating user's voice using the communication twin profile.",
        ["publisherName"] = "Message Screener",
        ["privacyUrl"] = $"{baseUrl}/privacy",
        ["license"] = "",
        ["msaAppId"] = appId,
        ["endpoints"] = new object[]
        {
            new Dictionary<string, object?>
            {
                ["name"] = "default",
                ["protocol"] = "BotFrameworkV3",
                ["description"] = "Skill endpoint for Communication Twin rewrite actions",
                ["endpointUrl"] = endpointUrl,
                ["msAppId"] = appId,
            }
        },
        ["activities"] = new Dictionary<string, object?>
        {
            [RewriteInUserVoiceSkillActivityId] = new Dictionary<string, object?>
            {
                ["type"] = "event",
                ["name"] = RewriteInUserVoiceSkillActivityId,
                ["description"] = "Rewrite suggested response in the operating user's voice. Send rewrite payload in activity.value.",
            }
        }
    };

    return Results.Json(manifest);
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
    IForwardActionQueue forwardActionQueue,
    IPersonalReviewConversationRegistry personalReviewConversationRegistry,
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
    string? fromDisplayName = FirstNonEmpty(
        GetNestedJsonString(root, "from", "name"),
        GetJsonPathString(root, "from", "user", "displayName"),
        "Message Screener user");
    string? recipientId = GetNestedJsonString(root, "recipient", "id");
    string? botId = FirstNonEmpty(recipientId, teamsOptions.Value.ManagedIdentityClientId);

    AppLog.BotWebhookProcessed(
        logger,
        activityType ?? "unknown",
        activityId ?? "unknown",
        !string.IsNullOrWhiteSpace(incomingText));

    logger.LogInformation(
        "Bot webhook invoke metadata. invokeName={InvokeName} serviceUrlPresent={HasServiceUrl} conversationIdPresent={HasConversationId}",
        invokeName ?? "unknown",
        !string.IsNullOrWhiteSpace(serviceUrl),
        !string.IsNullOrWhiteSpace(conversationId));

    if (string.Equals(activityType, "invoke", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(invokeName, "composeExtension/submitAction", StringComparison.OrdinalIgnoreCase))
    {
        ForwardedMessageIntakeRequest? forwardedRequest = TryParseForwardedMessageFromInvoke(root);
        if (forwardedRequest is null)
        {
            return Results.Ok(CreateComposeExtensionStatus(
                "Forwarding could not capture the full message context. Please retry from the message action."));
        }

        TeamsInboundMessage forwardedMessage = CreateInboundMessage(forwardedRequest);

        try
        {
            ForwardActionBootstrapContext? bootstrapContext =
                !string.IsNullOrWhiteSpace(serviceUrl) &&
                !string.IsNullOrWhiteSpace(forwardedRequest.TenantId) &&
                !string.IsNullOrWhiteSpace(fromId) &&
                !string.IsNullOrWhiteSpace(botId)
                    ? new ForwardActionBootstrapContext(
                        serviceUrl,
                        forwardedRequest.TenantId,
                        fromId,
                        fromDisplayName ?? "Message Screener user",
                        botId)
                    : null;

            await forwardActionQueue.EnqueueAsync(
                new ForwardActionWorkItem(forwardedMessage, bootstrapContext),
                cancellationToken);

            return Results.Ok(CreateComposeExtensionStatus(
                "Message forwarded. I’ll post the draft in your personal Message Screener chat."));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process composeExtension submit action.");
            return Results.Ok(CreateComposeExtensionStatus(
                "Message Screener is temporarily unavailable for this action. Please retry from the message action."));
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
        await personalReviewConversationRegistry.RememberAsync(
            new PersonalReviewConversationContext(conversationId, serviceUrl),
            cancellationToken);
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

static async ValueTask<IResult> RewriteInUserVoiceAsync(
    CommunicationTwinRewriteRequest rewriteRequest,
    ICopilotReplyDraftingService copilotReplyDraftingService,
    ICommunicationTwinService communicationTwinService,
    IGhcpAgentHarness ghcpAgentHarness,
    CancellationToken cancellationToken)
{
    if (!TryValidateRewriteRequest(rewriteRequest, out string? validationError))
    {
        return Results.BadRequest(new
        {
            error = "invalid_rewrite_request",
            detail = validationError
        });
    }

    CommunicationTwinProfile profile = communicationTwinService.GetInitialProfile();
    string? communicationTwinSkillContent = await ghcpAgentHarness.GetCommunicationTwinSkillContentAsync(cancellationToken);
    string rewrittenResponse = await copilotReplyDraftingService.RewriteInUserVoiceAsync(
        rewriteRequest,
        profile,
        communicationTwinSkillContent,
        cancellationToken);

    return Results.Ok(new CommunicationTwinRewriteResponse(
        RewrittenResponse: rewrittenResponse,
        OwnerDisplayName: profile.OwnerDisplayName,
        Tone: profile.Tone));
}

static async ValueTask<IResult> RewriteInUserVoiceSkillAsync(
    HttpRequest request,
    ICopilotReplyDraftingService copilotReplyDraftingService,
    ICommunicationTwinService communicationTwinService,
    IGhcpAgentHarness ghcpAgentHarness,
    CancellationToken cancellationToken)
{
    JsonDocument payload;
    try
    {
        payload = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "invalid_json" });
    }

    using (payload)
    {
        JsonElement root = payload.RootElement;
        string? activityType = GetJsonString(root, "type");
        string? activityName = GetJsonString(root, "name");

        if (string.Equals(activityType, "endOfConversation", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new { type = "endOfConversation", code = "completedSuccessfully" });
        }

        if (!string.IsNullOrWhiteSpace(activityType) &&
            !string.Equals(activityType, "event", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(activityType, "invoke", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(activityType, "message", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new
            {
                type = "endOfConversation",
                code = "completedSuccessfully",
                value = new
                {
                    error = "unsupported_activity_type",
                    detail = $"Activity type '{activityType}' is not supported by this skill endpoint.",
                }
            });
        }

        if ((string.Equals(activityType, "event", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(activityType, "invoke", StringComparison.OrdinalIgnoreCase)) &&
            !string.Equals(activityName, RewriteInUserVoiceSkillActivityId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new
            {
                type = "endOfConversation",
                code = "completedSuccessfully",
                value = new
                {
                    error = "unsupported_activity_name",
                    detail = $"Activity name '{activityName ?? "<null>"}' is not supported.",
                }
            });
        }

        if (!TryParseCommunicationTwinRewriteRequest(root, out CommunicationTwinRewriteRequest? rewriteRequest))
        {
            return Results.Ok(new
            {
                type = "endOfConversation",
                code = "completedSuccessfully",
                value = new
                {
                    error = "invalid_rewrite_request",
                    detail = "Provide sourceKind, sourceText, suggestedResponse, and optional supportingEvidence.",
                }
            });
        }

        if (!TryValidateRewriteRequest(rewriteRequest, out string? validationError))
        {
            return Results.Ok(new
            {
                type = "endOfConversation",
                code = "completedSuccessfully",
                value = new
                {
                    error = "invalid_rewrite_request",
                    detail = validationError,
                }
            });
        }

        CommunicationTwinProfile profile = communicationTwinService.GetInitialProfile();
        string? communicationTwinSkillContent = await ghcpAgentHarness.GetCommunicationTwinSkillContentAsync(cancellationToken);
        string rewrittenResponse = await copilotReplyDraftingService.RewriteInUserVoiceAsync(
            rewriteRequest!,
            profile,
            communicationTwinSkillContent,
            cancellationToken);

        return Results.Ok(new
        {
            type = "endOfConversation",
            code = "completedSuccessfully",
            value = new CommunicationTwinRewriteResponse(
                RewrittenResponse: rewrittenResponse,
                OwnerDisplayName: profile.OwnerDisplayName,
                Tone: profile.Tone),
        });
    }
}

static bool TryValidateRewriteRequest(CommunicationTwinRewriteRequest? request, out string? validationError)
{
    validationError = null;
    if (request is null)
    {
        validationError = "Payload is required.";
        return false;
    }

    if (string.IsNullOrWhiteSpace(request.SourceText))
    {
        validationError = "sourceText is required.";
        return false;
    }

    if (string.IsNullOrWhiteSpace(request.SuggestedResponse))
    {
        validationError = "suggestedResponse is required.";
        return false;
    }

    if (request.SourceText.Length > 4000)
    {
        validationError = "sourceText exceeds 4000 characters.";
        return false;
    }

    if (request.SuggestedResponse.Length > 4000)
    {
        validationError = "suggestedResponse exceeds 4000 characters.";
        return false;
    }

    if (request.SupportingEvidence.Length > 25)
    {
        validationError = "supportingEvidence supports up to 25 entries.";
        return false;
    }

    if (request.SupportingEvidence.Any(item => item.Length > 2000))
    {
        validationError = "Each supportingEvidence entry must be 2000 characters or less.";
        return false;
    }

    return true;
}

static bool TryParseCommunicationTwinRewriteRequest(
    JsonElement payload,
    out CommunicationTwinRewriteRequest? request)
{
    if (TryBuildRewriteRequestFromJson(payload, out request))
    {
        return true;
    }

    if (payload.ValueKind != JsonValueKind.Object)
    {
        request = null;
        return false;
    }

    if (payload.TryGetProperty("value", out JsonElement value) &&
        value.ValueKind == JsonValueKind.Object &&
        TryBuildRewriteRequestFromJson(value, out request))
    {
        return true;
    }

    if (payload.TryGetProperty("text", out JsonElement text) &&
        text.ValueKind == JsonValueKind.String)
    {
        string? textValue = text.GetString();
        if (!string.IsNullOrWhiteSpace(textValue))
        {
            try
            {
                using JsonDocument textJson = JsonDocument.Parse(textValue);
                if (TryBuildRewriteRequestFromJson(textJson.RootElement, out request))
                {
                    return true;
                }
            }
            catch (JsonException)
            {
            }
        }
    }

    request = null;
    return false;
}

static bool TryBuildRewriteRequestFromJson(
    JsonElement payload,
    out CommunicationTwinRewriteRequest? request)
{
    request = null;
    if (payload.ValueKind != JsonValueKind.Object)
    {
        return false;
    }

    if (!payload.TryGetProperty("sourceText", out JsonElement sourceTextElement) ||
        sourceTextElement.ValueKind != JsonValueKind.String)
    {
        return false;
    }

    if (!payload.TryGetProperty("suggestedResponse", out JsonElement suggestedResponseElement) ||
        suggestedResponseElement.ValueKind != JsonValueKind.String)
    {
        return false;
    }

    RewriteSourceKind sourceKind = RewriteSourceKind.Message;
    if (payload.TryGetProperty("sourceKind", out JsonElement sourceKindElement))
    {
        if (sourceKindElement.ValueKind == JsonValueKind.String)
        {
            string? sourceKindRaw = sourceKindElement.GetString();
            if (!Enum.TryParse<RewriteSourceKind>(sourceKindRaw, true, out sourceKind))
            {
                return false;
            }
        }
        else if (sourceKindElement.ValueKind == JsonValueKind.Number)
        {
            if (!sourceKindElement.TryGetInt32(out int sourceKindValue) ||
                !Enum.IsDefined(typeof(RewriteSourceKind), sourceKindValue))
            {
                return false;
            }

            sourceKind = (RewriteSourceKind)sourceKindValue;
        }
        else
        {
            return false;
        }
    }

    List<string> supportingEvidence = [];
    if (payload.TryGetProperty("supportingEvidence", out JsonElement evidenceElement))
    {
        if (evidenceElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement evidenceItem in evidenceElement.EnumerateArray())
        {
            if (evidenceItem.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            string evidenceText = evidenceItem.GetString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(evidenceText))
            {
                supportingEvidence.Add(evidenceText.Trim());
            }
        }
    }

    request = new CommunicationTwinRewriteRequest(
        SourceKind: sourceKind,
        SourceText: sourceTextElement.GetString()?.Trim() ?? string.Empty,
        SuggestedResponse: suggestedResponseElement.GetString()?.Trim() ?? string.Empty,
        SupportingEvidence: supportingEvidence.ToArray());
    return true;
}

static string ResolvePublicBaseUrl(HttpRequest request, string? configuredPublicBaseUrl)
{
    string? environmentPublicBaseUrl = Environment.GetEnvironmentVariable("MESSAGE_SCREENER_PUBLIC_BASE_URL");
    string? selected = FirstNonEmpty(configuredPublicBaseUrl, environmentPublicBaseUrl);
    if (!string.IsNullOrWhiteSpace(selected))
    {
        return selected.TrimEnd('/');
    }

    return $"https://{request.Host}".TrimEnd('/');
}

static Dictionary<string, object?> BuildRewriteSwaggerV2Document(string host, string scheme)
{
    return new Dictionary<string, object?>
    {
        ["swagger"] = "2.0",
        ["info"] = new Dictionary<string, object?>
        {
            ["title"] = "Message Screener Rewrite API",
            ["version"] = ServiceVersion,
            ["description"] = "Rewrites a suggested response into the operating user's voice using the communication twin profile."
        },
        ["host"] = host,
        ["basePath"] = "/",
        ["schemes"] = new[] { scheme },
        ["consumes"] = new[] { "application/json" },
        ["produces"] = new[] { "application/json" },
        ["paths"] = new Dictionary<string, object?>
        {
            ["/api/voice/rewrite"] = new Dictionary<string, object?>
            {
                ["post"] = new Dictionary<string, object?>
                {
                    ["summary"] = "Rewrite suggested response in the operating user's voice",
                    ["description"] = "Accepts the original question/message, a suggested response, and supporting evidence, then rewrites the response to match the operating user's voice.",
                    ["operationId"] = "rewriteInUserVoice",
                    ["consumes"] = new[] { "application/json" },
                    ["produces"] = new[] { "application/json" },
                    ["parameters"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["name"] = "body",
                            ["@in"] = "body",
                            ["required"] = true,
                            ["description"] = "Rewrite request payload.",
                            ["schema"] = new Dictionary<string, object?>
                            {
                                ["$ref"] = "#/definitions/CommunicationTwinRewriteRequest"
                            }
                        }
                    },
                    ["responses"] = new Dictionary<string, object?>
                    {
                        ["200"] = new Dictionary<string, object?>
                        {
                            ["description"] = "Successfully rewritten response.",
                            ["schema"] = new Dictionary<string, object?>
                            {
                                ["$ref"] = "#/definitions/CommunicationTwinRewriteResponse"
                            }
                        },
                        ["400"] = new Dictionary<string, object?>
                        {
                            ["description"] = "Invalid request payload."
                        }
                    }
                }
            }
        },
        ["definitions"] = new Dictionary<string, object?>
        {
            ["CommunicationTwinRewriteRequest"] = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["description"] = "Input used to rewrite a suggested response into the operating user's voice.",
                ["required"] = new[] { "sourceText", "suggestedResponse" },
                ["properties"] = new Dictionary<string, object?>
                {
                    ["sourceKind"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string",
                        ["description"] = "Whether the prompt came from an explicit query or from a received message.",
                        ["enum"] = new[] { "Query", "Message" }
                    },
                    ["sourceText"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string",
                        ["description"] = "The original question or received message that the rewritten reply should answer."
                    },
                    ["suggestedResponse"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string",
                        ["description"] = "The draft answer that should be rewritten to sound like the operating user."
                    },
                    ["supportingEvidence"] = new Dictionary<string, object?>
                    {
                        ["type"] = "array",
                        ["description"] = "Optional supporting facts, context, or citations that the rewrite should preserve.",
                        ["items"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["description"] = "One piece of supporting evidence or contextual guidance."
                        }
                    }
                }
            },
            ["CommunicationTwinRewriteResponse"] = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["description"] = "Result returned after the response has been rewritten in the operating user's voice.",
                ["required"] = new[] { "rewrittenResponse", "ownerDisplayName", "tone" },
                ["properties"] = new Dictionary<string, object?>
                {
                    ["rewrittenResponse"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string",
                        ["description"] = "The final rewritten text that should be used as the reply draft."
                    },
                    ["ownerDisplayName"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string",
                        ["description"] = "The display name of the operating user whose voice was used."
                    },
                    ["tone"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string",
                        ["description"] = "The tone profile used while rewriting the response."
                    }
                }
            }
        }
    };
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
    object cardContent = new
    {
        title = "Message Screener",
        text,
    };

    return new
    {
        composeExtension = new
        {
            type = "result",
            attachmentLayout = "list",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.hero",
                    content = cardContent,
                    preview = new
                    {
                        contentType = "application/vnd.microsoft.card.hero",
                        content = cardContent,
                    },
                },
            },
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
