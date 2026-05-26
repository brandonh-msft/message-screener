using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MessageScreener.Api;

public sealed class CommunicationTwinMcpStreamStore
{
    private readonly ConcurrentDictionary<string, Channel<string>> _channels = new(StringComparer.Ordinal);

    public (string OperationId, ChannelWriter<string> Writer) Create()
    {
        string operationId = Guid.NewGuid().ToString("N");
        Channel<string> channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        _channels[operationId] = channel;
        return (operationId, channel.Writer);
    }

    public bool TryGetReader(string operationId, out ChannelReader<string>? reader)
    {
        if (_channels.TryGetValue(operationId, out Channel<string>? channel))
        {
            reader = channel.Reader;
            return true;
        }

        reader = null;
        return false;
    }

    public void Remove(string operationId)
    {
        _channels.TryRemove(operationId, out _);
    }
}
