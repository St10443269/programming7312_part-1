using System.Text;
using SmartX.Shared.Dto;
using SmartX.Shared.Search;

namespace SmartX.Client.Services;

public enum LiveConnectionState
{
    Connecting,
    Connected,
    Reconnecting
}

/// <summary>
/// Holds the live Part 2 data for the whole app and keeps the Server-Sent
/// Events connection to the API open.
/// </summary>
/// <remarks>
/// <para>
/// It is registered once for the app (not per page), so the SSE connection,
/// the latest device states and the user's search/filter settings survive
/// moving between pages - navigation never has to reload or reconnect.
/// </para>
/// <para>
/// <b>Reconnection:</b> if the stream drops (API restarted, network blip) or
/// goes silent for longer than the server's heartbeat allows, it reconnects
/// with a growing delay (1s, 2s, 4s ... up to 10s). The API sends a full
/// snapshot at the start of every connection, so nothing is lost.
/// </para>
/// <para>
/// <b>Render throttling:</b> updates can arrive many times per second.
/// Instead of re-rendering on each one, incoming data only sets a "dirty"
/// flag and pages are notified at most every 250 ms.
/// </para>
/// </remarks>
public sealed class LiveDashboardState(SmartXApiClient api) : IAsyncDisposable
{
    private const int MaxRecentEvents = 60;
    private const int MaxRecentCommands = 30;
    private static readonly TimeSpan SilenceTimeout = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan NotifyInterval = TimeSpan.FromMilliseconds(250);

    private readonly Dictionary<string, DeviceDto> _devices = new();
    private readonly List<HistoryEntryDto> _recentEvents = new();
    private readonly List<CommandRecordDto> _recentCommands = new();
    private CancellationTokenSource? _cts;
    private bool _dirty;

    /// <summary>Raised (throttled) whenever live data changed. Handlers should call InvokeAsync(StateHasChanged).</summary>
    public event Action? Changed;

    public LiveConnectionState ConnectionState { get; private set; } = LiveConnectionState.Connecting;
    public string? ConnectionError { get; private set; }
    public DateTimeOffset? LastMessageAt { get; private set; }
    public int ReconnectAttempts { get; private set; }
    public long MessagesReceived { get; private set; }

    public IReadOnlyList<DeviceDto> Devices =>
        _devices.Values.OrderBy(d => d.ZoneName).ThenBy(d => d.Name).ToList();

    /// <summary>Newest first.</summary>
    public IReadOnlyList<HistoryEntryDto> RecentEvents => _recentEvents;

    /// <summary>Newest first; commands seen on the live stream since this tab opened.</summary>
    public IReadOnlyList<CommandRecordDto> RecentCommands => _recentCommands;

    public List<ActiveErrorDto> ActiveErrors { get; private set; } = [];
    public ProcessingSnapshotDto? Processing { get; private set; }

    // UI state kept here (rather than in a page) so it survives navigation.
    public DeviceQuery DeviceFilter { get; } = new();
    public string? HistoryDeviceMac { get; set; }

    public DeviceDto? FindDevice(string? macAddress) =>
        macAddress is not null && _devices.TryGetValue(macAddress, out var device) ? device : null;

    /// <summary>Starts the live connection once; later calls do nothing.</summary>
    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _ = RunConnectionLoopAsync(_cts.Token);
        _ = RunNotifyLoopAsync(_cts.Token);
    }

    /// <summary>Applies a command result immediately (the SSE echo may arrive a moment later).</summary>
    public void ApplyLocal(CommandRecordDto command) => Apply(new LiveUpdateDto { Type = LiveUpdateDto.CommandType, Command = command });

    private async Task RunConnectionLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ConnectionState = ReconnectAttempts == 0 ? LiveConnectionState.Connecting : LiveConnectionState.Reconnecting;
            MarkDirty();

            try
            {
                using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                watchdog.CancelAfter(SilenceTimeout);

                using var response = await api.OpenLiveStreamAsync(watchdog.Token);
                ConnectionState = LiveConnectionState.Connected;
                ConnectionError = null;
                ReconnectAttempts = 0;
                MarkDirty();

                await ReadEventsAsync(response, watchdog, cancellationToken);
                ConnectionError = "The live stream was closed by the API.";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                ConnectionError = $"No data from the API for {SilenceTimeout.TotalSeconds:0}s.";
            }
            catch (Exception ex)
            {
                ConnectionError = SmartXApiClient.DescribeError(ex);
            }

            ReconnectAttempts++;
            ConnectionState = LiveConnectionState.Reconnecting;
            MarkDirty();

            var delay = TimeSpan.FromSeconds(Math.Min(10, Math.Pow(2, ReconnectAttempts - 1)));
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Minimal SSE parser following the event-stream format (WHATWG, n.d.):
    /// "data:" lines are collected until a blank line
    /// ends the event. "event:" names are not needed because the JSON itself
    /// carries a Type field.
    /// </summary>
    private async Task ReadEventsAsync(HttpResponseMessage response, CancellationTokenSource watchdog, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(watchdog.Token);
        using var reader = new StreamReader(stream);
        var data = new StringBuilder();

        while (await reader.ReadLineAsync(watchdog.Token) is { } line)
        {
            // Any line (including the server's heartbeats) proves the
            // connection is alive, so push the silence deadline back.
            watchdog.CancelAfter(SilenceTimeout);

            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    Dispatch(data.ToString());
                    data.Clear();
                }

                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.Append(line.AsSpan(5).TrimStart());
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private void Dispatch(string json)
    {
        LiveUpdateDto? update;
        try
        {
            update = SmartXApiClient.ParseLiveUpdate(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return; // Ignore a malformed event rather than dropping the connection.
        }

        if (update is not null)
        {
            Apply(update);
        }
    }

    private void Apply(LiveUpdateDto update)
    {
        MessagesReceived++;
        LastMessageAt = DateTimeOffset.Now;

        switch (update.Type)
        {
            case LiveUpdateDto.DeviceType when update.Device is { } device:
                _devices[device.MacAddress] = device;
                break;

            case LiveUpdateDto.HistoryType when update.Entry is { } entry:
                _recentEvents.Insert(0, entry);
                if (_recentEvents.Count > MaxRecentEvents) _recentEvents.RemoveAt(_recentEvents.Count - 1);
                break;

            case LiveUpdateDto.CommandType when update.Command is { } command:
                if (_recentCommands.All(c => c.Id != command.Id))
                {
                    _recentCommands.Insert(0, command);
                    if (_recentCommands.Count > MaxRecentCommands) _recentCommands.RemoveAt(_recentCommands.Count - 1);
                }

                break;

            case LiveUpdateDto.ErrorsType when update.Errors is { } errors:
                ActiveErrors = errors;
                break;

            case LiveUpdateDto.ProcessingType when update.Processing is { } processing:
                Processing = processing;
                break;

            default:
                return; // Heartbeat - nothing to re-render.
        }

        MarkDirty();
    }

    private void MarkDirty() => _dirty = true;

    private async Task RunNotifyLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(NotifyInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!_dirty)
                {
                    continue;
                }

                _dirty = false;
                Changed?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            // App shutting down.
        }
    }

    public ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        return ValueTask.CompletedTask;
    }
}
