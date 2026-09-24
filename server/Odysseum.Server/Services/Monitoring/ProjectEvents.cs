using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Odysseum.Server.Services.Monitoring;

public sealed class ProjectEvents
{
    private readonly ConcurrentDictionary<Guid, Channel<long>> _listeners = new();
    private long _version;

    public (Guid Id, ChannelReader<long> Reader) Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<long>(new BoundedChannelOptions(1)
        { FullMode = BoundedChannelFullMode.DropOldest });
        _listeners[id] = channel;
        channel.Writer.TryWrite(Interlocked.Read(ref _version));
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_listeners.TryRemove(id, out var channel)) channel.Writer.TryComplete();
    }

    public void Publish()
    {
        var version = Interlocked.Increment(ref _version);
        foreach (var channel in _listeners.Values) channel.Writer.TryWrite(version);
    }
}
