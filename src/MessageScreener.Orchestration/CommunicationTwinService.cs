using MessageScreener.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MessageScreener.Orchestration
{
    public sealed class MessageScreenerAgentOptions
    {
        public const string SectionName = "MessageScreener";

        public string CommunicationTwinPromptPath { get; init; } = "copilot-config/prompts/communication-twin.prompt.md";
    }

    public interface ICommunicationTwinService
    {
        CommunicationTwinProfile GetInitialProfile();
    }

    public sealed class CommunicationTwinService(
        IOptions<MessageScreenerAgentOptions> options,
        ILogger<CommunicationTwinService> logger) : ICommunicationTwinService
    {
        private const string DefaultOwnerDisplayName = "the owner";
        private const string DefaultPersonaSummary = "Professional, concise, and direct.";
        private const string DefaultTone = "professional";
        private static readonly string[] DefaultPreferredPhrases =
        [
            "Thanks for reaching out.",
            "I can help with that.",
            "Here is the fastest path forward.",
        ];
        private static readonly string[] DefaultAvoidPhrases =
        [
            "Just looping back",
            "Per my previous email",
            "No worries",
        ];

        public CommunicationTwinProfile GetInitialProfile()
        {
            MessageScreenerAgentOptions current = options.Value;
            var communicationTwinPromptPath = ResolveCommunicationTwinPromptPath(current.CommunicationTwinPromptPath);

            if (!File.Exists(communicationTwinPromptPath))
            {
                CommunicationTwinLog.PromptFileMissing(communicationTwinPromptPath, logger);
                return BuildDefaultProfile();
            }

            var prompt = ParsePromptContent(File.ReadAllText(communicationTwinPromptPath));

            if (prompt is null || string.IsNullOrWhiteSpace(prompt.OwnerDisplayName) || string.IsNullOrWhiteSpace(prompt.PersonaSummary))
            {
                CommunicationTwinLog.PromptFileInvalid(communicationTwinPromptPath, logger);
                return BuildDefaultProfile();
            }

            var preferredPhrases = prompt.PreferredPhrases.Length > 0 ? prompt.PreferredPhrases : DefaultPreferredPhrases;
            var avoidPhrases = prompt.AvoidPhrases.Length > 0 ? prompt.AvoidPhrases : DefaultAvoidPhrases;

            CommunicationTwinLog.PromptFileLoaded(communicationTwinPromptPath, logger);

            return new CommunicationTwinProfile(
                OwnerDisplayName: prompt.OwnerDisplayName,
                PersonaSummary: prompt.PersonaSummary ?? DefaultPersonaSummary,
                PreferredPhrases: preferredPhrases,
                AvoidPhrases: avoidPhrases,
                Tone: prompt.Tone ?? DefaultTone);
        }

        private static CommunicationTwinProfile BuildDefaultProfile()
        {
            return new CommunicationTwinProfile(
                OwnerDisplayName: DefaultOwnerDisplayName,
                PersonaSummary: DefaultPersonaSummary,
                PreferredPhrases: DefaultPreferredPhrases,
                AvoidPhrases: DefaultAvoidPhrases,
                Tone: DefaultTone);
        }

        private static string ResolveCommunicationTwinPromptPath(string configuredPath)
        {
            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configuredPath));
        }

        private static CommunicationTwinPromptModel? ParsePromptContent(string content)
        {
            string? ownerDisplayName = null;
            string? personaSummary = null;
            string? tone = null;
            List<string> preferredPhrases = [];
            List<string> avoidPhrases = [];
            PromptSection section = PromptSection.None;
            List<string> personaSummaryLines = [];

            using StringReader reader = new(content);
            while (reader.ReadLine() is { } rawLine)
            {
                string trimmed = rawLine.Trim();

                if (trimmed.StartsWith("owner:", StringComparison.OrdinalIgnoreCase))
                {
                    ownerDisplayName = trimmed["owner:".Length..].Trim();
                    continue;
                }

                if (trimmed.StartsWith("tone:", StringComparison.OrdinalIgnoreCase))
                {
                    tone = trimmed["tone:".Length..].Trim();
                    continue;
                }

                if (trimmed.Equals("persona summary:", StringComparison.OrdinalIgnoreCase))
                {
                    section = PromptSection.PersonaSummary;
                    continue;
                }

                if (trimmed.Equals("preferred phrases:", StringComparison.OrdinalIgnoreCase))
                {
                    section = PromptSection.PreferredPhrases;
                    continue;
                }

                if (trimmed.Equals("avoid phrases:", StringComparison.OrdinalIgnoreCase))
                {
                    section = PromptSection.AvoidPhrases;
                    continue;
                }

                if (trimmed.EndsWith(":", StringComparison.OrdinalIgnoreCase))
                {
                    section = PromptSection.None;
                    continue;
                }

                switch (section)
                {
                    case PromptSection.PersonaSummary:
                        if (!string.IsNullOrWhiteSpace(trimmed))
                        {
                            personaSummaryLines.Add(trimmed);
                        }

                        break;
                    case PromptSection.PreferredPhrases:
                        if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                        {
                            preferredPhrases.Add(trimmed[2..].Trim());
                        }

                        break;
                    case PromptSection.AvoidPhrases:
                        if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                        {
                            avoidPhrases.Add(trimmed[2..].Trim());
                        }

                        break;
                }
            }

            if (personaSummaryLines.Count > 0)
            {
                personaSummary = string.Join(" ", personaSummaryLines);
            }

            if (string.IsNullOrWhiteSpace(ownerDisplayName))
            {
                return null;
            }

            return new CommunicationTwinPromptModel(
                OwnerDisplayName: ownerDisplayName,
                PersonaSummary: personaSummary,
                PreferredPhrases: preferredPhrases.ToArray(),
                AvoidPhrases: avoidPhrases.ToArray(),
                Tone: tone);
        }
    }

    internal sealed record CommunicationTwinPromptModel(
        string OwnerDisplayName,
        string? PersonaSummary,
        string[] PreferredPhrases,
        string[] AvoidPhrases,
        string? Tone);

    internal enum PromptSection
    {
        None = 0,
        PersonaSummary = 1,
        PreferredPhrases = 2,
        AvoidPhrases = 3,
    }
}