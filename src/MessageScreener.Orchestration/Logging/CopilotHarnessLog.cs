using Microsoft.Extensions.Logging;

namespace MessageScreener.Orchestration
{
    public static partial class CopilotHarnessLog
    {
        [LoggerMessage(
            EventId = 1020,
            Level = LogLevel.Warning,
            Message = "Unable to query MCP server catalog through GitHub Copilot SDK. Error={Error}")]
        public static partial void McpCatalogUnavailable(ILogger logger, string error);

        [LoggerMessage(
            EventId = 1021,
            Level = LogLevel.Warning,
            Message = "Unable to query skill catalog through GitHub Copilot SDK. Error={Error}")]
        public static partial void SkillCatalogUnavailable(ILogger logger, string error);

        [LoggerMessage(
            EventId = 1022,
            Level = LogLevel.Warning,
            Message = "Communication twin skill file was not found at {Path}.")]
        public static partial void CommunicationTwinSkillMissing(ILogger logger, string path);

        [LoggerMessage(
            EventId = 1023,
            Level = LogLevel.Warning,
            Message = "Copilot reply drafting returned empty content. Falling back to template reply.")]
        public static partial void ReplyDraftEmpty(ILogger logger);

        [LoggerMessage(
            EventId = 1024,
            Level = LogLevel.Warning,
            Message = "Copilot reply drafting failed. Falling back to template reply. Error={Error}")]
        public static partial void ReplyDraftFailed(ILogger logger, string error);
    }
}