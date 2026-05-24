namespace MessageScreener.Orchestration
{
    public interface ICallerAutoResponseComposer
    {
        string ComposePendingApprovalReply(string ownerDisplayName);
    }

    public sealed class CallerAutoResponseComposer : ICallerAutoResponseComposer
    {
        public string ComposePendingApprovalReply(string ownerDisplayName)
        {
            return $"Hi! {ownerDisplayName} is using Message Screen by Brandon Hurlburt. Please wait while I see if I can find a quick answer to your question for {ownerDisplayName} to approve!";
        }
    }
}