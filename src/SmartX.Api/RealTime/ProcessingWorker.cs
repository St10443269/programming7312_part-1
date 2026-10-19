namespace SmartX.Api.RealTime;

/// <summary>
/// Background loop that drains the telemetry queues every few hundred
/// milliseconds, so HTTP requests only have to validate and enqueue a packet
/// and can return immediately (202 Accepted) instead of waiting for it to be
/// processed. It also checks for devices that have gone silent.
/// </summary>
public sealed class ProcessingWorker(TelemetryProcessor processor, RealTimeOptions options, ILogger<ProcessingWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(50, options.Processing.IntervalMilliseconds)));
        var lastQueueDepths = (-1, -1);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var processed = processor.IsPaused ? 0 : processor.ProcessPending(options.Processing.BatchSize);
                processor.SweepDisconnected();

                // Only push a queue snapshot when something changed, so idle
                // ticks don't make every dashboard re-render.
                var snapshot = processor.GetSnapshot();
                var depths = (snapshot.NormalQueueDepth, snapshot.PriorityQueueDepth);
                if (processed > 0 || depths != lastQueueDepths)
                {
                    processor.PublishSnapshot();
                    lastQueueDepths = depths;
                }
            }
            catch (Exception ex)
            {
                // One bad message must not kill the worker for the rest of the run.
                logger.LogError(ex, "Telemetry processing tick failed; continuing.");
            }
        }
    }
}
