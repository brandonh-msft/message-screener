namespace MessageScreener.ReviewDelivery
{
    public sealed class MessageScreenerTeamsOptions
    {
        public const string SectionName = "MessageScreener:Teams";

        public bool SendAutomaticCallerReply { get; init; } = true;

        public string? ManagedIdentityClientId { get; init; }
    }
}