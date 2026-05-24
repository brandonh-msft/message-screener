using GitHub.Copilot.SDK;
using MessageScreener.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MessageScreener.Orchestration;

public interface IGhcpAgentHarness
{
    ValueTask<IReadOnlyList<McpServerRegistration>> GetMcpServersAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<GhcpSkillDefinition>> GetSkillCatalogAsync(CancellationToken cancellationToken);

    ValueTask<string?> GetCommunicationTwinSkillContentAsync(CancellationToken cancellationToken);
}

public sealed class GhcpAgentHarness(
    IOptions<MessageScreenerAgentOptions> options,
    ILogger<GhcpAgentHarness> logger) : IGhcpAgentHarness
{
    public async ValueTask<IReadOnlyList<McpServerRegistration>> GetMcpServersAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using CopilotClient client = new();
            await using CopilotSession session = await client.CreateSessionAsync(new SessionConfig
            {
                OnPermissionRequest = PermissionHandler.ApproveAll,
            }, cancellationToken);

            var mcpList = await session.Rpc.Mcp.ListAsync(cancellationToken);

            var mapped = mcpList.Servers
                .Select(server => new McpServerRegistration(
                    server.Name,
                    $"source={server.Source}; status={server.Status}",
                    string.Equals(server.Status.ToString(), "connected", StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            return mapped;
        }
        catch (Exception ex)
        {
            CopilotHarnessLog.McpCatalogUnavailable(logger, ex.Message);
            return Array.Empty<McpServerRegistration>();
        }
    }

    public async ValueTask<IReadOnlyList<GhcpSkillDefinition>> GetSkillCatalogAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using CopilotClient client = new();
            await using CopilotSession session = await client.CreateSessionAsync(new SessionConfig
            {
                OnPermissionRequest = PermissionHandler.ApproveAll,
            }, cancellationToken);

            await session.Rpc.Skills.ReloadAsync(cancellationToken);
            var skillList = await session.Rpc.Skills.ListAsync(cancellationToken);

            var mapped = skillList.Skills
                .Select(skill => new GhcpSkillDefinition(
                    skill.Name,
                    skill.Description ?? string.Empty,
                    Array.Empty<string>(),
                    skill.Enabled))
                .ToArray();

            return mapped;
        }
        catch (Exception ex)
        {
            CopilotHarnessLog.SkillCatalogUnavailable(logger, ex.Message);
            return Array.Empty<GhcpSkillDefinition>();
        }
    }

    public ValueTask<string?> GetCommunicationTwinSkillContentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var configuredPath = options.Value.CommunicationTwinSkillPath;
        var fullPath = ResolvePath(configuredPath);

        if (!File.Exists(fullPath))
        {
            CopilotHarnessLog.CommunicationTwinSkillMissing(logger, fullPath);
            return ValueTask.FromResult<string?>(null);
        }

        var content = File.ReadAllText(fullPath);
        return ValueTask.FromResult<string?>(content);
    }

    private static string ResolvePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configuredPath));
    }
}
