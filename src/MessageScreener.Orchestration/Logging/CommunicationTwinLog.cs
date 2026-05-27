using Microsoft.Extensions.Logging;

namespace MessageScreener.Orchestration
{
    public static partial class CommunicationTwinLog
    {
        [LoggerMessage(
            EventId = 1010,
            Level = LogLevel.Warning,
            Message = "Communication twin prompt was not found at {Path}. Falling back to app configuration defaults.")]
        public static partial void PromptFileMissing(string path, ILogger logger);

        [LoggerMessage(
            EventId = 1011,
            Level = LogLevel.Warning,
            Message = "Communication twin prompt at {Path} was invalid. Falling back to app configuration defaults.")]
        public static partial void PromptFileInvalid(string path, ILogger logger);

        [LoggerMessage(
            EventId = 1012,
            Level = LogLevel.Information,
            Message = "Loaded communication twin prompt profile from {Path}.")]
        public static partial void PromptFileLoaded(string path, ILogger logger);
    }
}