using Microsoft.Extensions.Logging;

namespace MessageScreener.Orchestration
{
    public static partial class CommunicationTwinLog
    {
        [LoggerMessage(
            EventId = 1010,
            Level = LogLevel.Warning,
            Message = "Communication twin skill was not found at {Path}. Falling back to app configuration defaults.")]
        public static partial void SkillFileMissing(string path, ILogger logger);

        [LoggerMessage(
            EventId = 1011,
            Level = LogLevel.Warning,
            Message = "Communication twin skill at {Path} was invalid. Falling back to app configuration defaults.")]
        public static partial void SkillFileInvalid(string path, ILogger logger);

        [LoggerMessage(
            EventId = 1012,
            Level = LogLevel.Information,
            Message = "Loaded communication twin skill profile from {Path}.")]
        public static partial void SkillFileLoaded(string path, ILogger logger);
    }
}