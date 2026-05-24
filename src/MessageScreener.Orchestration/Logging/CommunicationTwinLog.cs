using Microsoft.Extensions.Logging;

namespace MessageScreener.Orchestration
{
    public static partial class CommunicationTwinLog
    {
        [LoggerMessage(
            EventId = 1010,
            Level = LogLevel.Warning,
            Message = "Communication twin file was not found at {Path}. Falling back to app configuration defaults.")]
        public static partial void TwinFileMissing(string path, ILogger logger);

        [LoggerMessage(
            EventId = 1011,
            Level = LogLevel.Warning,
            Message = "Communication twin file at {Path} was invalid. Falling back to app configuration defaults.")]
        public static partial void TwinFileInvalid(string path, ILogger logger);

        [LoggerMessage(
            EventId = 1012,
            Level = LogLevel.Information,
            Message = "Loaded communication twin profile from {Path}.")]
        public static partial void TwinFileLoaded(string path, ILogger logger);
    }
}