using System.Diagnostics;
using Azure.Identity;
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

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

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

app.MapPost("/webhooks/graph", async (
    TeamsInboundMessage message,
    IMessageIntakeService intakeService,
    ICommunicationTwinService communicationTwinService,
    ICallerAutoResponseComposer callerAutoResponseComposer,
    IReviewDeliveryService reviewDeliveryService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    using var activity = new Activity(ServiceName + ".GraphWebhook").Start();
    var logger = loggerFactory.CreateLogger("MessageScreener.GraphWebhook");
    var intakeResult = await intakeService.IntakeAsync(message, cancellationToken);

    AppLog.GraphWebhookProcessed(
        logger,
        intakeResult.Accepted,
        intakeResult.Duplicate,
        intakeResult.Trigger.ShouldCreateReview,
        intakeResult.ReasonCode,
        intakeResult.Trigger.ReasonCode);

    if (intakeResult.Accepted && intakeResult.Trigger.ShouldCreateReview)
    {
        var twinProfile = communicationTwinService.GetInitialProfile();
        var pendingApprovalReply = callerAutoResponseComposer.ComposePendingApprovalReply(twinProfile.OwnerDisplayName);

        await reviewDeliveryService.SendPendingApprovalReplyAsync(message, pendingApprovalReply, cancellationToken);
    }

    return Results.Ok(intakeResult);
});

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MessageScreener.Startup");
AppLog.ServiceStarted(startupLogger, ServiceName, ServiceVersion, app.Environment.EnvironmentName);

app.Run();
