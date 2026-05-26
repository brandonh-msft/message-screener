using MessageScreener.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MessageScreener.Orchestration
{
    public sealed class MessageScreenerAgentOptions
    {
        public const string SectionName = "MessageScreener";

        public string CommunicationTwinSkillPath { get; init; } = "copilot-config/skills/communication-twin/SKILL.md";
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
            var communicationTwinSkillPath = ResolveCommunicationTwinSkillPath(current.CommunicationTwinSkillPath);

            if (!File.Exists(communicationTwinSkillPath))
            {
                CommunicationTwinLog.SkillFileMissing(communicationTwinSkillPath, logger);
                return BuildDefaultProfile();
            }

            var skill = ParseSkillContent(File.ReadAllText(communicationTwinSkillPath));

            if (skill is null || string.IsNullOrWhiteSpace(skill.OwnerDisplayName) || string.IsNullOrWhiteSpace(skill.PersonaSummary))
            {
                CommunicationTwinLog.SkillFileInvalid(communicationTwinSkillPath, logger);
                return BuildDefaultProfile();
            }

            var preferredPhrases = skill.PreferredPhrases.Length > 0 ? skill.PreferredPhrases : DefaultPreferredPhrases;
            var avoidPhrases = skill.AvoidPhrases.Length > 0 ? skill.AvoidPhrases : DefaultAvoidPhrases;

            CommunicationTwinLog.SkillFileLoaded(communicationTwinSkillPath, logger);

            return new CommunicationTwinProfile(
                OwnerDisplayName: skill.OwnerDisplayName,
                PersonaSummary: skill.PersonaSummary ?? DefaultPersonaSummary,
                PreferredPhrases: preferredPhrases,
                AvoidPhrases: avoidPhrases,
                Tone: skill.Tone ?? DefaultTone);
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

        private static string ResolveCommunicationTwinSkillPath(string configuredPath)
        {
            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configuredPath));
        }

        private static CommunicationTwinSkillModel? ParseSkillContent(string content)
        {
            string? ownerDisplayName = null;
            string? personaSummary = null;
            string? tone = null;
            List<string> preferredPhrases = [];
            List<string> avoidPhrases = [];
            SkillSection section = SkillSection.None;
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
                    section = SkillSection.PersonaSummary;
                    continue;
                }

                if (trimmed.Equals("preferred phrases:", StringComparison.OrdinalIgnoreCase))
                {
                    section = SkillSection.PreferredPhrases;
                    continue;
                }

                if (trimmed.Equals("avoid phrases:", StringComparison.OrdinalIgnoreCase))
                {
                    section = SkillSection.AvoidPhrases;
                    continue;
                }

                if (trimmed.EndsWith(":", StringComparison.OrdinalIgnoreCase))
                {
                    section = SkillSection.None;
                    continue;
                }

                switch (section)
                {
                    case SkillSection.PersonaSummary:
                        if (!string.IsNullOrWhiteSpace(trimmed))
                        {
                            personaSummaryLines.Add(trimmed);
                        }

                        break;
                    case SkillSection.PreferredPhrases:
                        if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                        {
                            preferredPhrases.Add(trimmed[2..].Trim());
                        }

                        break;
                    case SkillSection.AvoidPhrases:
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

            return new CommunicationTwinSkillModel(
                OwnerDisplayName: ownerDisplayName,
                PersonaSummary: personaSummary,
                PreferredPhrases: preferredPhrases.ToArray(),
                AvoidPhrases: avoidPhrases.ToArray(),
                Tone: tone);
        }
    }

    internal sealed record CommunicationTwinSkillModel(
        string OwnerDisplayName,
        string? PersonaSummary,
        string[] PreferredPhrases,
        string[] AvoidPhrases,
        string? Tone);

    internal enum SkillSection
    {
        None = 0,
        PersonaSummary = 1,
        PreferredPhrases = 2,
        AvoidPhrases = 3,
    }
}