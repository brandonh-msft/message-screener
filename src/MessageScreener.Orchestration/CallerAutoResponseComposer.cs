using MessageScreener.Contracts;

namespace MessageScreener.Orchestration
{
    public interface ICallerAutoResponseComposer
    {
        ValueTask<string> ComposePendingApprovalReplyAsync(
            TeamsInboundMessage message,
            CommunicationTwinProfile profile,
            CancellationToken cancellationToken);
    }

    public sealed class CallerAutoResponseComposer(
        ICopilotReplyDraftingService copilotReplyDraftingService,
        IGhcpAgentHarness ghcpAgentHarness) : ICallerAutoResponseComposer
    {
        public async ValueTask<string> ComposePendingApprovalReplyAsync(
            TeamsInboundMessage message,
            CommunicationTwinProfile profile,
            CancellationToken cancellationToken)
        {
            string? communicationTwinSkillContent = await ghcpAgentHarness.GetCommunicationTwinSkillContentAsync(cancellationToken);
            return await copilotReplyDraftingService.DraftReplyAsync(
                message,
                profile,
                communicationTwinSkillContent,
                cancellationToken);
        }
    }
}