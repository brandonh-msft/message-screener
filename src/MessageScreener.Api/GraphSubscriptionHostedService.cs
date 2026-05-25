using MessageScreener.Api.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace MessageScreener.Api;

public sealed class GraphSubscriptionHostedService(
    GraphServiceClient graphServiceClient,
    IOptions<GraphWebhookOptions> options,
    ILogger<GraphSubscriptionHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        GraphWebhookOptions config = options.Value;

        if (!config.Enabled)
        {
            AppLog.GraphSubscriptionSkipped(logger, "graph_webhook_disabled");
            return;
        }

        if (!config.AutoProvisionSubscription)
        {
            AppLog.GraphSubscriptionSkipped(logger, "auto_provision_disabled");
            return;
        }

        if (string.IsNullOrWhiteSpace(config.Resource))
        {
            AppLog.GraphSubscriptionSkipped(logger, "missing_resource");
            return;
        }

        string? clientState = FirstNonEmpty(
            config.ClientState,
            Environment.GetEnvironmentVariable("MessageScreener__GraphWebhook__ClientState"),
            Environment.GetEnvironmentVariable("MESSAGE_SCREENER_GRAPH_WEBHOOK_CLIENT_STATE"));

        if (string.IsNullOrWhiteSpace(clientState))
        {
            // Require explicit client state to avoid accepting spoofed webhook events.
            AppLog.GraphSubscriptionSkipped(logger, "missing_client_state");
            return;
        }

        string? notificationUrl = ResolveNotificationUrl(config);
        if (string.IsNullOrWhiteSpace(notificationUrl))
        {
            AppLog.GraphSubscriptionSkipped(logger, "missing_notification_url_or_base_url");
            return;
        }

        var expiration = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(config.SubscriptionDurationMinutes, 15, 60));

        try
        {
            if (!string.IsNullOrWhiteSpace(config.SubscriptionId))
            {
                var patch = new Subscription
                {
                    NotificationUrl = notificationUrl,
                    ExpirationDateTime = expiration,
                    ClientState = clientState,
                };

                await graphServiceClient
                    .Subscriptions[config.SubscriptionId]
                    .PatchAsync(patch, cancellationToken: cancellationToken);

                AppLog.GraphSubscriptionRenewed(logger, config.SubscriptionId, expiration);
                return;
            }

            var create = new Subscription
            {
                ChangeType = config.ChangeType,
                Resource = config.Resource,
                NotificationUrl = notificationUrl,
                ExpirationDateTime = expiration,
                ClientState = clientState,
                LatestSupportedTlsVersion = "v1_2",
            };

            Subscription? created = await graphServiceClient
                .Subscriptions
                .PostAsync(create, cancellationToken: cancellationToken);

            if (created is null || string.IsNullOrWhiteSpace(created.Id))
            {
                AppLog.GraphSubscriptionSkipped(logger, "subscription_create_returned_null");
                return;
            }

            AppLog.GraphSubscriptionCreated(
                logger,
                created.Id,
                created.Resource ?? config.Resource,
                notificationUrl,
                created.ExpirationDateTime ?? expiration);
        }
        catch (Exception ex)
        {
            AppLog.GraphSubscriptionSkipped(logger, $"subscription_error:{ex.Message}");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string? ResolveNotificationUrl(GraphWebhookOptions config)
    {
        string? notificationUrl = FirstNonEmpty(
            config.NotificationUrl,
            Environment.GetEnvironmentVariable("MessageScreener__GraphWebhook__NotificationUrl"),
            Environment.GetEnvironmentVariable("MESSAGE_SCREENER_GRAPH_WEBHOOK_NOTIFICATION_URL"));

        if (!string.IsNullOrWhiteSpace(notificationUrl))
        {
            return notificationUrl.Trim();
        }

        string? publicBaseUrl = FirstNonEmpty(
            config.PublicBaseUrl,
            Environment.GetEnvironmentVariable("MessageScreener__GraphWebhook__PublicBaseUrl"),
            Environment.GetEnvironmentVariable("MESSAGE_SCREENER_PUBLIC_BASE_URL"));

        if (string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            string? containerAppName = Environment.GetEnvironmentVariable("CONTAINER_APP_NAME");
            string? containerAppDnsSuffix = Environment.GetEnvironmentVariable("CONTAINER_APP_ENV_DNS_SUFFIX");

            if (!string.IsNullOrWhiteSpace(containerAppName) &&
                !string.IsNullOrWhiteSpace(containerAppDnsSuffix))
            {
                publicBaseUrl = $"https://{containerAppName}.{containerAppDnsSuffix}";
            }
        }

        if (string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            return null;
        }

        return $"{publicBaseUrl.TrimEnd('/')}/webhooks/graph";
    }

    private static string? FirstNonEmpty(params string?[] values)
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
}
