using System.Collections.Concurrent;
using System.Threading.Channels;
using SmartX.Shared.Dto;

namespace SmartX.Api.RealTime;

/// <summary>
/// Fans live updates out to every connected dashboard over Server-Sent
/// Events (WHATWG, n.d.). Each browser connection gets its own bounded channel (a small
/// thread-safe buffer; Microsoft, n.d.h); <see cref="Publish"/> drops an update into every
/// channel and the SSE endpoint streams that connection's channel out.
/// </summary>
/// <remarks>
/// The channels are bounded and drop their oldest item when full, so one
/// slow or stalled browser can never make the API buffer updates without
/// limit or block the processor. A disconnected client is removed when its
/// request is cancelled (see <c>StreamEndpoints</c>).
/// </remarks>
public sealed class LiveUpdateBroadcaster
{
    private const int PerClientBuffer = 256;
    private readonly ConcurrentDictionary<Guid, Channel<LiveUpdateDto>> _subscribers = new();

    public (Guid Id, ChannelReader<LiveUpdateDto> Reader) Subscribe()
    {
        var channel = Channel.CreateBounded<LiveUpdateDto>(new BoundedChannelOptions(PerClientBuffer)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

        var id = Guid.NewGuid();
        _subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public void Publish(LiveUpdateDto update)
    {
        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(update);
        }
    }

    public int SubscriberCount => _subscribers.Count;
}
