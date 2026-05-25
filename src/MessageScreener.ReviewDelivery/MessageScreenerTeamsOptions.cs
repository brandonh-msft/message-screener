namespace MessageScreener.ReviewDelivery
{
    public sealed class MessageScreenerTeamsOptions
    {
        public const string SectionName = "MessageScreener:Teams";

        public bool SendAutomaticCallerReply { get; init; } = true;

        public string? ManagedIdentityClientId { get; init; }

        public string? PersonalReviewConversationId { get; init; }

        public string? BotServiceUrl { get; init; }
    }
}