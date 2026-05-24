using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace MessageScreener.ReviewDelivery
{
    public interface ITeamsMessageClient
    {
        ValueTask SendMessageAsync(string conversationId, string messageText, CancellationToken cancellationToken);
    }

    public sealed class TeamsGraphMessageClient(GraphServiceClient graphServiceClient) : ITeamsMessageClient
    {
        public async ValueTask SendMessageAsync(string conversationId, string messageText, CancellationToken cancellationToken)
        {
            var outboundMessage = new ChatMessage
            {
                Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = messageText,
                },
            };

            await graphServiceClient
                .Chats[conversationId]
                .Messages
                .PostAsync(outboundMessage, cancellationToken: cancellationToken);
        }
    }
}