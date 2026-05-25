using System.Text.Json;
using MessageScreener.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MessageScreener.Orchestration
{
    public sealed class MessageScreenerAgentOptions
    {
        public const string SectionName = "MessageScreener";

        public string OwnerDisplayName { get; init; } = "the owner";

        public string PersonaSummary { get; init; } = "Professional, concise, and direct.";

        public string Tone { get; init; } = "professional";

        public string CommunicationTwinPath { get; init; } = "config/communication-twin.json";

        public string CommunicationTwinSkillPath { get; init; } = "config/copilot-runtime/skills/communication-twin/SKILL.md";
    }

    public interface ICommunicationTwinService
    {
        CommunicationTwinProfile GetInitialProfile();
    }

    public sealed class CommunicationTwinService(
        IOptions<MessageScreenerAgentOptions> options,
        ILogger<CommunicationTwinService> logger) : ICommunicationTwinService
    {
        private static readonly JsonSerializerOptions JsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };

        public CommunicationTwinProfile GetInitialProfile()
        {
            MessageScreenerAgentOptions current = options.Value;
            var communicationTwinPath = ResolveCommunicationTwinPath(current.CommunicationTwinPath);

            if (!File.Exists(communicationTwinPath))
            {
                CommunicationTwinLog.TwinFileMissing(communicationTwinPath, logger);
                return BuildDefaultProfile(current);
            }

            var json = File.ReadAllText(communicationTwinPath);
            CommunicationTwinFileModel? twin = JsonSerializer.Deserialize<CommunicationTwinFileModel>(json, JsonSerializerOptions);

            if (twin is null || string.IsNullOrWhiteSpace(twin.OwnerDisplayName))
            {
                CommunicationTwinLog.TwinFileInvalid(communicationTwinPath, logger);
                return BuildDefaultProfile(current);
            }

            var preferredPhrases = twin.PreferredPhrases ??
            [
                "Thanks for reaching out.",
                "I can help with that.",
                "Here is the fastest path forward.",
            ];

            var avoidPhrases = twin.AvoidPhrases ??
            [
                "Just looping back",
                "Per my previous email",
                "No worries",
            ];

            CommunicationTwinLog.TwinFileLoaded(communicationTwinPath, logger);

            return new CommunicationTwinProfile(
                OwnerDisplayName: twin.OwnerDisplayName,
                PersonaSummary: twin.PersonaSummary ?? current.PersonaSummary,
                PreferredPhrases: preferredPhrases,
                AvoidPhrases: avoidPhrases,
                Tone: twin.Tone ?? current.Tone);
        }

        private static CommunicationTwinProfile BuildDefaultProfile(MessageScreenerAgentOptions current)
        {
            return new CommunicationTwinProfile(
                OwnerDisplayName: current.OwnerDisplayName,
                PersonaSummary: current.PersonaSummary,
                PreferredPhrases:
                [
                    "Thanks for reaching out.",
                    "I can help with that.",
                    "Here is the fastest path forward.",
                ],
                AvoidPhrases:
                [
                    "Just looping back",
                    "Per my previous email",
                    "No worries",
                ],
                Tone: current.Tone);
        }

        private static string ResolveCommunicationTwinPath(string configuredPath)
        {
            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configuredPath));
        }
    }

    internal sealed class CommunicationTwinFileModel
    {
        public string OwnerDisplayName { get; init; } = string.Empty;

        public string? PersonaSummary { get; init; }

        public string[]? PreferredPhrases { get; init; }

        public string[]? AvoidPhrases { get; init; }

        public string? Tone { get; init; }
    }
}