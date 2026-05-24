using Microsoft.Extensions.Logging;

namespace MessageScreener.Orchestration;

public static partial class IntakeLog
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Processed inbound Teams message event {EventId}; shouldCreateReview={ShouldCreateReview}; reason={ReasonCode}.")]
    public static partial void InboundEventProcessed(
        ILogger logger,
        string eventId,
        bool shouldCreateReview,
        string reasonCode);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Ignored duplicate inbound Teams message event {EventId}; reason={ReasonCode}.")]
    public static partial void DuplicateInboundEvent(
        ILogger logger,
        string eventId,
        string reasonCode);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Warning,
        Message = "Rejected inbound Teams message because event id was missing.")]
    public static partial void InvalidEventId(ILogger logger);
}
