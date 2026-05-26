using System.Threading.Channels;
using MessageScreener.Contracts;

namespace MessageScreener.Api;

public sealed record ForwardActionBootstrapContext(
    string ServiceUrl,
    string TenantId,
    string InvokingUserId,
    string InvokingUserDisplayName,
    string BotId);

public sealed record ForwardActionWorkItem(
    TeamsInboundMessage Message,
    ForwardActionBootstrapContext? BootstrapContext);

public interface IForwardActionQueue
{
    ValueTask EnqueueAsync(ForwardActionWorkItem workItem, CancellationToken cancellationToken);

    IAsyncEnumerable<ForwardActionWorkItem> DequeueAllAsync(CancellationToken cancellationToken);
}

public sealed class ForwardActionQueue : IForwardActionQueue
{
    private readonly Channel<ForwardActionWorkItem> _channel = Channel.CreateUnbounded<ForwardActionWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

    public ValueTask EnqueueAsync(ForwardActionWorkItem workItem, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(workItem, cancellationToken);

    public IAsyncEnumerable<ForwardActionWorkItem> DequeueAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
