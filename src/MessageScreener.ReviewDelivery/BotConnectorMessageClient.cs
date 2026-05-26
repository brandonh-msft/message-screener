using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace MessageScreener.ReviewDelivery
{
    public interface ITeamsMessageClient
    {
        ValueTask<PersonalReviewConversationContext> CreatePersonalConversationAsync(
            string serviceUrl,
            string tenantId,
            string botId,
            string userId,
            string userDisplayName,
            CancellationToken cancellationToken);

        ValueTask SendMessageAsync(
            string serviceUrl,
            string conversationId,
            string messageText,
            CancellationToken cancellationToken);
    }

    public sealed class BotConnectorMessageClient(
        IHttpClientFactory httpClientFactory,
        IOptions<MessageScreenerTeamsOptions> options) : ITeamsMessageClient
    {
        public async ValueTask<PersonalReviewConversationContext> CreatePersonalConversationAsync(
            string serviceUrl,
            string tenantId,
            string botId,
            string userId,
            string userDisplayName,
            CancellationToken cancellationToken)
        {
            var credential = CreateCredential();
            AccessToken token = await credential.GetTokenAsync(
                new TokenRequestContext(["https://api.botframework.com/.default"]),
                cancellationToken);

            var conversationRequest = new
            {
                bot = new { id = botId },
                isGroup = false,
                serviceUrl,
                channelData = new
                {
                    tenant = new
                    {
                        id = tenantId,
                    },
                },
                members = new[]
                {
                    new
                    {
                        id = userId,
                        name = userDisplayName,
                    },
                },
            };

            string connectorUrl = $"{serviceUrl.TrimEnd('/')}/v3/conversations";
            string jsonPayload = JsonSerializer.Serialize(conversationRequest);
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, connectorUrl)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            using HttpClient httpClient = httpClientFactory.CreateClient();
            using HttpResponseMessage response = await httpClient.SendAsync(requestMessage, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Bot connector create conversation failed: {(int)response.StatusCode} {response.ReasonPhrase}; body={responseBody}");
            }

            using JsonDocument document = JsonDocument.Parse(responseBody);
            string? conversationId = document.RootElement.TryGetProperty("id", out JsonElement idElement)
                ? idElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(conversationId))
            {
                throw new InvalidOperationException("Bot connector did not return a conversation id for the personal review chat.");
            }

            return new PersonalReviewConversationContext(conversationId, serviceUrl);
        }

        public async ValueTask SendMessageAsync(
            string serviceUrl,
            string conversationId,
            string messageText,
            CancellationToken cancellationToken)
        {
            var credential = CreateCredential();
            AccessToken token = await credential.GetTokenAsync(
                new TokenRequestContext(["https://api.botframework.com/.default"]),
                cancellationToken);

            var outboundMessage = new
            {
                type = "message",
                text = messageText,
            };

            string connectorUrl =
                $"{serviceUrl.TrimEnd('/')}/v3/conversations/{Uri.EscapeDataString(conversationId)}/activities";

            string jsonPayload = JsonSerializer.Serialize(outboundMessage);
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, connectorUrl)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            using HttpClient httpClient = httpClientFactory.CreateClient();
            using HttpResponseMessage response = await httpClient.SendAsync(requestMessage, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        private DefaultAzureCredential CreateCredential()
        {
            var credentialOptions = new DefaultAzureCredentialOptions();
            if (!string.IsNullOrWhiteSpace(options.Value.ManagedIdentityClientId))
            {
                credentialOptions.ManagedIdentityClientId = options.Value.ManagedIdentityClientId;
            }

            return new DefaultAzureCredential(credentialOptions);
        }
    }
}