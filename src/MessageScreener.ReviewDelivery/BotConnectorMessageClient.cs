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
        public async ValueTask SendMessageAsync(
            string serviceUrl,
            string conversationId,
            string messageText,
            CancellationToken cancellationToken)
        {
            var credentialOptions = new DefaultAzureCredentialOptions();
            if (!string.IsNullOrWhiteSpace(options.Value.ManagedIdentityClientId))
            {
                credentialOptions.ManagedIdentityClientId = options.Value.ManagedIdentityClientId;
            }

            var credential = new DefaultAzureCredential(credentialOptions);
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
    }
}