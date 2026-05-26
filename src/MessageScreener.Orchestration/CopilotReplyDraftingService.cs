using System.Text;
using GitHub.Copilot.SDK;
using MessageScreener.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MessageScreener.Orchestration;

public sealed class MessageScreenerCopilotOptions
{
    public const string SectionName = "MessageScreener:Copilot";

    public string? GitHubToken { get; init; }

    public string? Model { get; init; }

    public string? Agent { get; init; }

    public string ConfigDirectory { get; init; } = "copilot-config";

    public string[] SkillDirectories { get; init; } = ["copilot-config/skills"];

    public string SystemPromptPath { get; init; } = "copilot-config/prompts/copilot-reply.system.prompt.md";

    public string MessageMode { get; init; } = "interactive";

    public int ResponseTimeoutSeconds { get; init; } = 45;

    public bool EnableConfigDiscovery { get; init; } = true;
}

public interface ICopilotReplyDraftingService
{
    ValueTask<string> DraftReplyAsync(
        TeamsInboundMessage message,
        CommunicationTwinProfile profile,
        string? communicationTwinSkillContent,
        CancellationToken cancellationToken);

    ValueTask<CopilotDraftProbeResult> ProbeDraftAsync(
        TeamsInboundMessage message,
        CommunicationTwinProfile profile,
        string? communicationTwinSkillContent,
        CancellationToken cancellationToken);

    ValueTask<string> RewriteInUserVoiceAsync(
        CommunicationTwinRewriteRequest request,
        CommunicationTwinProfile profile,
        string? communicationTwinSkillContent,
        CancellationToken cancellationToken);
}

public sealed record CopilotDraftProbeResult(bool Success, string DraftReply, string ReasonCode);

public sealed class CopilotReplyDraftingService(
    IOptions<MessageScreenerCopilotOptions> options,
    ILogger<CopilotReplyDraftingService> logger) : ICopilotReplyDraftingService
{
    public async ValueTask<string> DraftReplyAsync(
        TeamsInboundMessage message,
        CommunicationTwinProfile profile,
        string? communicationTwinSkillContent,
        CancellationToken cancellationToken)
    {
        CopilotDraftProbeResult probe = await ProbeDraftAsync(
            message,
            profile,
            communicationTwinSkillContent,
            cancellationToken);

        if (probe.Success)
        {
            return probe.DraftReply;
        }

        return BuildFallbackReply(profile.OwnerDisplayName);
    }

    public async ValueTask<CopilotDraftProbeResult> ProbeDraftAsync(
        TeamsInboundMessage message,
        CommunicationTwinProfile profile,
        string? communicationTwinSkillContent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string systemPrompt = LoadSystemPrompt(options.Value.SystemPromptPath);
        string userPrompt = BuildUserPrompt(message, profile, communicationTwinSkillContent);
        string configDirectory = ResolvePath(options.Value.ConfigDirectory);
        List<string> skillDirectories = ResolveSkillDirectories(options.Value.SkillDirectories);

        try
        {
            await using CopilotClient client = new();
            await using CopilotSession session = await client.CreateSessionAsync(new SessionConfig
            {
                OnPermissionRequest = PermissionHandler.ApproveAll,
                Model = options.Value.Model,
                Agent = options.Value.Agent,
                GitHubToken = options.Value.GitHubToken,
                EnableConfigDiscovery = options.Value.EnableConfigDiscovery,
                ConfigDir = configDirectory,
                SkillDirectories = skillDirectories,
                SystemMessage = new SystemMessageConfig
                {
                    Mode = SystemMessageMode.Replace,
                    Content = systemPrompt,
                },
            }, cancellationToken);

            var response = await session.SendAndWaitAsync(
                new MessageOptions
                {
                    Prompt = userPrompt,
                    Mode = options.Value.MessageMode,
                },
                TimeSpan.FromSeconds(Math.Max(5, options.Value.ResponseTimeoutSeconds)),
                cancellationToken);

            if (response is null)
            {
                CopilotHarnessLog.ReplyDraftEmpty(logger);
                return new CopilotDraftProbeResult(false, string.Empty, "empty_response");
            }

            string? content = response.Data?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                CopilotHarnessLog.ReplyDraftEmpty(logger);
                return new CopilotDraftProbeResult(false, string.Empty, "empty_content");
            }

            return new CopilotDraftProbeResult(true, content.Trim(), "ok");
        }
        catch (Exception ex)
        {
            CopilotHarnessLog.ReplyDraftFailed(logger, ex.Message);
            return new CopilotDraftProbeResult(false, string.Empty, "exception");
        }
    }

    public async ValueTask<string> RewriteInUserVoiceAsync(
        CommunicationTwinRewriteRequest request,
        CommunicationTwinProfile profile,
        string? communicationTwinSkillContent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string systemPrompt = LoadSystemPrompt(options.Value.SystemPromptPath);
        string userPrompt = BuildRewriteUserPrompt(request, profile, communicationTwinSkillContent);
        string configDirectory = ResolvePath(options.Value.ConfigDirectory);
        List<string> skillDirectories = ResolveSkillDirectories(options.Value.SkillDirectories);

        try
        {
            await using CopilotClient client = new();
            await using CopilotSession session = await client.CreateSessionAsync(new SessionConfig
            {
                OnPermissionRequest = PermissionHandler.ApproveAll,
                Model = options.Value.Model,
                Agent = options.Value.Agent,
                GitHubToken = options.Value.GitHubToken,
                EnableConfigDiscovery = options.Value.EnableConfigDiscovery,
                ConfigDir = configDirectory,
                SkillDirectories = skillDirectories,
                SystemMessage = new SystemMessageConfig
                {
                    Mode = SystemMessageMode.Replace,
                    Content = systemPrompt,
                },
            }, cancellationToken);

            var response = await session.SendAndWaitAsync(
                new MessageOptions
                {
                    Prompt = userPrompt,
                    Mode = options.Value.MessageMode,
                },
                TimeSpan.FromSeconds(Math.Max(5, options.Value.ResponseTimeoutSeconds)),
                cancellationToken);

            if (response is null || string.IsNullOrWhiteSpace(response.Data?.Content))
            {
                CopilotHarnessLog.RewriteFallbackSuggestedResponse(logger);
                return request.SuggestedResponse.Trim();
            }

            return response.Data.Content.Trim();
        }
        catch (Exception ex)
        {
            CopilotHarnessLog.RewriteFailed(logger, ex.Message);
            return request.SuggestedResponse.Trim();
        }
    }

    private static string LoadSystemPrompt(string configuredPath)
    {
        string resolvedPath = ResolvePath(configuredPath);
        if (!File.Exists(resolvedPath))
        {
            return "You draft concise, accurate replies in the operating user's voice. Never claim actions were taken unless stated in provided context. Return only the draft reply text.";
        }

        return File.ReadAllText(resolvedPath);
    }

    private static string BuildUserPrompt(
        TeamsInboundMessage message,
        CommunicationTwinProfile profile,
        string? communicationTwinSkillContent)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Draft a response the operating user can send after review.");
        builder.AppendLine();
        builder.AppendLine("Operating user profile:");
        builder.AppendLine($"- owner_display_name: {profile.OwnerDisplayName}");
        builder.AppendLine($"- tone: {profile.Tone}");
        builder.AppendLine($"- persona_summary: {profile.PersonaSummary}");
        builder.AppendLine($"- preferred_phrases: {string.Join(", ", profile.PreferredPhrases)}");
        builder.AppendLine($"- avoid_phrases: {string.Join(", ", profile.AvoidPhrases)}");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(communicationTwinSkillContent))
        {
            builder.AppendLine("Communication twin skill content:");
            builder.AppendLine(communicationTwinSkillContent);
            builder.AppendLine();
        }

        builder.AppendLine("Inbound sender message context:");
        builder.AppendLine($"- sender_display_name: {message.SenderDisplayName}");
        builder.AppendLine($"- scope: {message.Scope}");
        builder.AppendLine($"- body: {message.BodyPlainText}");
        builder.AppendLine();
        builder.AppendLine("Requirements:");
        builder.AppendLine("1) Use configured skills, agents, and MCP tools to gather relevant knowledge about the operating user and request context when available.");
        builder.AppendLine("2) Produce a practical, personalized draft reply in the operating user's voice.");
        builder.AppendLine("3) Keep it concise and professional.");
        builder.AppendLine("4) Return only the draft reply text.");

        return builder.ToString();
    }

    private static string BuildRewriteUserPrompt(
        CommunicationTwinRewriteRequest request,
        CommunicationTwinProfile profile,
        string? communicationTwinSkillContent)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Rewrite the suggested response so it sounds like the operating user while preserving facts and intent.");
        builder.AppendLine();
        builder.AppendLine("Operating user profile:");
        builder.AppendLine($"- owner_display_name: {profile.OwnerDisplayName}");
        builder.AppendLine($"- tone: {profile.Tone}");
        builder.AppendLine($"- persona_summary: {profile.PersonaSummary}");
        builder.AppendLine($"- preferred_phrases: {string.Join(", ", profile.PreferredPhrases)}");
        builder.AppendLine($"- avoid_phrases: {string.Join(", ", profile.AvoidPhrases)}");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(communicationTwinSkillContent))
        {
            builder.AppendLine("Communication twin skill content:");
            builder.AppendLine(communicationTwinSkillContent);
            builder.AppendLine();
        }

        builder.AppendLine("Inbound context:");
        builder.AppendLine($"- source_kind: {request.SourceKind}");
        builder.AppendLine($"- source_text: {request.SourceText}");
        builder.AppendLine();
        builder.AppendLine("Supporting evidence:");
        if (request.SupportingEvidence.Length == 0)
        {
            builder.AppendLine("- none provided");
        }
        else
        {
            foreach (string evidence in request.SupportingEvidence)
            {
                builder.AppendLine($"- {evidence}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Suggested response to rewrite:");
        builder.AppendLine(request.SuggestedResponse);
        builder.AppendLine();
        builder.AppendLine("Requirements:");
        builder.AppendLine("1) Keep the same meaning and factual claims as the suggested response.");
        builder.AppendLine("2) Align wording to the operating user's voice.");
        builder.AppendLine("3) Keep it concise and professional.");
        builder.AppendLine("4) Return only the rewritten response text.");

        return builder.ToString();
    }

    private static List<string> ResolveSkillDirectories(IEnumerable<string> configuredDirectories)
    {
        return configuredDirectories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(ResolvePath)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolvePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configuredPath));
    }

    private static string BuildFallbackReply(string ownerDisplayName)
    {
        return $"Hi! {ownerDisplayName} is using Message Screener. Please wait while I prepare a response for {ownerDisplayName} to review.";
    }
}
