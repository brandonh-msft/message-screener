using MessageScreener.Contracts;
using Microsoft.Extensions.Options;

namespace MessageScreener.Orchestration;

public sealed record CopilotReadinessCheck(string Name, bool Passed, string Detail);

public sealed record CopilotReadinessReport(
    bool Ready,
    DateTimeOffset CheckedAtUtc,
    IReadOnlyList<CopilotReadinessCheck> Checks);

public interface ICopilotReadinessService
{
    ValueTask<CopilotReadinessReport> EvaluateAsync(CancellationToken cancellationToken);
}

public sealed class CopilotReadinessService(
    IOptions<MessageScreenerCopilotOptions> copilotOptions,
    ICommunicationTwinService communicationTwinService,
    IGhcpAgentHarness ghcpAgentHarness,
    ICopilotReplyDraftingService copilotReplyDraftingService) : ICopilotReadinessService
{
    public async ValueTask<CopilotReadinessReport> EvaluateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<CopilotReadinessCheck> checks = [];

        checks.Add(await EvaluateCommunicationTwinPromptCheckAsync(cancellationToken));
        checks.Add(EvaluateTokenCheck(copilotOptions.Value));

        IReadOnlyList<McpServerRegistration> mcpServers = await ghcpAgentHarness.GetMcpServersAsync(cancellationToken);
        bool anyConnectedMcp = mcpServers.Any(server => server.Enabled);
        checks.Add(new CopilotReadinessCheck(
            Name: "mcp_servers_connected",
            Passed: anyConnectedMcp,
            Detail: $"discovered={mcpServers.Count}; connected={mcpServers.Count(server => server.Enabled)}"));

        IReadOnlyList<GhcpSkillDefinition> skills = await ghcpAgentHarness.GetSkillCatalogAsync(cancellationToken);
        bool hasEnabledSkill = skills.Any(skill => skill.Enabled);
        bool hasMessageScreenerSkill = skills.Any(skill =>
            skill.Enabled && skill.SkillName.Contains("message-screener", StringComparison.OrdinalIgnoreCase));
        checks.Add(new CopilotReadinessCheck(
            Name: "skills_loaded",
            Passed: hasEnabledSkill && hasMessageScreenerSkill,
            Detail: $"enabled={skills.Count(skill => skill.Enabled)}; message_screener_enabled={hasMessageScreenerSkill}"));

        bool tokenConfigured = checks.First(check => check.Name == "github_token_configured").Passed;
        if (!tokenConfigured)
        {
            checks.Add(new CopilotReadinessCheck(
                Name: "draft_probe",
                Passed: false,
                Detail: "skipped_missing_github_token"));
        }
        else
        {
            CommunicationTwinProfile profile = communicationTwinService.GetInitialProfile();
            string? skillContent = await ghcpAgentHarness.GetCommunicationTwinPromptContentAsync(cancellationToken);
            TeamsInboundMessage probeMessage = CreateProbeMessage();

            CopilotDraftProbeResult probe = await copilotReplyDraftingService.ProbeDraftAsync(
                probeMessage,
                profile,
                skillContent,
                cancellationToken);

            checks.Add(new CopilotReadinessCheck(
                Name: "draft_probe",
                Passed: probe.Success,
                Detail: probe.ReasonCode));
        }

        bool ready = checks.All(check => check.Passed);
        return new CopilotReadinessReport(
            Ready: ready,
            CheckedAtUtc: DateTimeOffset.UtcNow,
            Checks: checks);
    }

    private async ValueTask<CopilotReadinessCheck> EvaluateCommunicationTwinPromptCheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? skillContent = await ghcpAgentHarness.GetCommunicationTwinPromptContentAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(skillContent))
        {
            return new CopilotReadinessCheck(
                Name: "communication_twin_prompt_loaded",
                Passed: false,
                Detail: "missing_prompt");
        }

        return new CopilotReadinessCheck(
            Name: "communication_twin_prompt_loaded",
            Passed: true,
            Detail: "ok");
    }

    private static CopilotReadinessCheck EvaluateTokenCheck(MessageScreenerCopilotOptions options)
    {
        bool hasToken = !string.IsNullOrWhiteSpace(options.GitHubToken);
        return new CopilotReadinessCheck(
            Name: "github_token_configured",
            Passed: hasToken,
            Detail: hasToken ? "ok" : "missing_token");
    }

    private static TeamsInboundMessage CreateProbeMessage()
    {
        return new TeamsInboundMessage(
            EventId: $"readiness-probe-{Guid.NewGuid():N}",
            TenantId: "readiness",
            ConversationId: "readiness",
            SourceMessageId: "readiness",
            SenderDisplayName: "readiness-probe",
            SenderIdentityKey: "readiness-probe",
            SenderIdentityKeyKind: SenderIdentityKeyKind.Unresolved,
            BodyPlainText: "Please draft a concise acknowledgement reply.",
            Scope: ConversationScope.OneOnOne,
            IsAtMention: true,
            OccurredAtUtc: DateTimeOffset.UtcNow);
    }

}
