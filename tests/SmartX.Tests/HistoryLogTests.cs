using SmartX.Api.RealTime;
using SmartX.Shared.Enums;

namespace SmartX.Tests;

/// <summary>SortedDictionary history: chronological order and same-timestamp handling.</summary>
public class HistoryLogTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Entries_come_back_in_chronological_order_even_when_added_out_of_order()
    {
        var log = new HistoryLog(new RealTimeOptions());

        log.Add(T0.AddSeconds(30), HistoryEntryKind.Reading, "MAC", "Dev", "third");
        log.Add(T0.AddSeconds(10), HistoryEntryKind.Reading, "MAC", "Dev", "first");
        log.Add(T0.AddSeconds(20), HistoryEntryKind.Reading, "MAC", "Dev", "second");

        Assert.Equal(["first", "second", "third"], log.Query().Select(e => e.Text));
    }

    [Fact]
    public void Readings_with_the_same_timestamp_are_all_kept_in_arrival_order()
    {
        var log = new HistoryLog(new RealTimeOptions());

        log.Add(T0, HistoryEntryKind.Reading, "MAC", "Dev", "A", value: 1);
        log.Add(T0, HistoryEntryKind.Reading, "MAC", "Dev", "B", value: 2);
        log.Add(T0, HistoryEntryKind.Alert, "MAC", "Dev", "C");

        var entries = log.Query();
        Assert.Equal(3, entries.Count);
        Assert.Equal(["A", "B", "C"], entries.Select(e => e.Text));
        Assert.All(entries, e => Assert.Equal(T0, e.TimestampUtc));
        Assert.Equal(entries.Select(e => e.Sequence).Order(), entries.Select(e => e.Sequence));
    }

    [Fact]
    public void Query_filters_by_device_kind_and_time_and_take_keeps_the_latest_oldest_first()
    {
        var log = new HistoryLog(new RealTimeOptions());
        for (var i = 0; i < 5; i++)
        {
            log.Add(T0.AddSeconds(i), HistoryEntryKind.Reading, "24:6F:28:00:00:01", "A", $"a{i}");
            log.Add(T0.AddSeconds(i), HistoryEntryKind.Reading, "24:6F:28:00:00:02", "B", $"b{i}");
        }

        log.Add(T0.AddSeconds(2), HistoryEntryKind.Alert, "24:6F:28:00:00:01", "A", "alert");

        Assert.Equal(["a2", "alert", "a3", "a4"], log.Query("24-6f-28-00-00-01", fromUtc: T0.AddSeconds(2)).Select(e => e.Text));
        Assert.Equal(["alert"], log.Query(kind: HistoryEntryKind.Alert).Select(e => e.Text));
        Assert.Equal(["b3", "b4"], log.Query("24:6F:28:00:00:02", take: 2).Select(e => e.Text));
    }

    [Fact]
    public void Oldest_entries_are_dropped_once_the_cap_is_reached()
    {
        var options = new RealTimeOptions();
        options.Processing.MaxHistoryEntries = 3;
        var log = new HistoryLog(options);

        for (var i = 0; i < 5; i++)
        {
            log.Add(T0.AddSeconds(i), HistoryEntryKind.Reading, "MAC", "Dev", $"r{i}");
        }

        Assert.Equal(3, log.Count);
        Assert.Equal(["r2", "r3", "r4"], log.Query().Select(e => e.Text));
    }

    [Fact]
    public void Late_buffered_packet_is_placed_by_capture_time_not_arrival_time()
    {
        var net = new TestNetwork().WithDemoDevices();

        net.SendAndProcess(TestNetwork.MoistureMac, 45);
        net.Clock.Advance(TimeSpan.FromSeconds(60));
        net.SendAndProcess(TestNetwork.MoistureMac, 44);

        // Arrives last, but was captured between the two readings above.
        net.Send(TestNetwork.MoistureMac, 50, capturedAt: net.Clock.Now.AddSeconds(-30));
        net.ProcessAll();

        var values = net.History.Query(TestNetwork.MoistureMac, HistoryEntryKind.Reading).Select(e => e.Value);
        Assert.Equal([45.0, 50.0, 44.0], values);
        var times = net.History.Query(TestNetwork.MoistureMac, HistoryEntryKind.Reading).Select(e => e.TimestampUtc).ToList();
        Assert.Equal(times.Order().ToList(), times);
        Assert.Equal(net.Clock.Now.AddSeconds(-30), times[1]);
    }
}
