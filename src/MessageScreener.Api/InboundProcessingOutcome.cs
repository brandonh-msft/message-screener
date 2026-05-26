using MessageScreener.Contracts;
using MessageScreener.ReviewDelivery;

namespace MessageScreener.Api;

public sealed record InboundProcessingOutcome(
    MessageIntakeResult IntakeResult,
    ReviewDeliveryResult ReviewDeliveryResult);
