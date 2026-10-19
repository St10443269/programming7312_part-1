using SmartX.Shared.Dto;
using SmartX.Shared.Enums;

namespace SmartX.Api.RealTime;

/// <summary>
/// Produces "Suggested Actions" from the data the app has actually
/// recorded: alerts in the history log, the user's commands and searches,
/// and current device readings. Nothing here is executed automatically -
/// a suggestion may carry a command, but only the user can send it.
/// </summary>
/// <remarks>
/// <para><b>1. Behaviour patterns (alert -> response).</b> For every alert
/// in the lookback window, the user's commands and searches made while
/// that alert was active (until the device recovered, at most
/// <c>PatternWindowMinutes</c>) are counted as responses to it. A
/// Dictionary keyed by (trigger device, error code, response device,
/// response type) accumulates how often each pair occurs, and a HashSet per
/// alert stops one alert counting the same response twice. A pair becomes
/// a pattern when it has enough <i>support</i> (seen at least
/// <c>MinPatternOccurrences</c> times) and <i>confidence</i> (it followed at
/// least <c>MinPatternConfidence</c> of that trigger's alerts) - the same
/// support/confidence idea used in association-rule mining.</para>
/// <para><b>2. Recurring faults.</b> Alerts grouped by (device, code); at or
/// above <c>RecurringFaultThreshold</c> in the window, the device is
/// flagged as problematic.</para>
/// <para><b>3. Out-of-range readings.</b> The latest reading of each
/// connected device is re-classified against the thresholds.</para>
/// <para><b>4. Disconnected devices.</b> Devices currently offline.</para>
/// <para>Each suggestion gets a score (patterns that apply right now score
/// highest) and the list is sorted by it.</para>
/// </remarks>
public sealed class RecommendationEngine(
    DeviceRegistry registry,
    HistoryLog history,
    ActiveErrorTracker errors,
    CommandService commands,
    SearchHistory searches,
    TelemetryClassifier classifier,
    RealTimeOptions options,
    TimeProvider clock)
{
    private const int MaxRecommendations = 20;

    /// <summary>A search is only "about" specific devices if it matched a handful of them, not the whole network.</summary>
    private const int MaxResultsForSpecificSearch = 3;

    private readonly record struct PatternKey(string TriggerMac, string Code, string ResponseMac, string ResponseType, CommandAction? Action);

    private sealed record Response(DateTimeOffset AtUtc, string MacAddress, string ResponseType, CommandAction? Action);

    public RecommendationReportDto Generate()
    {
        var settings = options.Recommendations;
        var now = clock.GetUtcNow();
        var from = now.AddMinutes(-settings.LookbackMinutes);

        var alerts = history.Query(kind: HistoryEntryKind.Alert, fromUtc: from, take: int.MaxValue);
        var recoveries = history.Query(kind: HistoryEntryKind.Recovery, fromUtc: from, take: int.MaxValue);

        var commandResponses = commands.GetLog(200)
            .Where(c => c.Outcome == CommandOutcome.Accepted && !c.IsUndo && c.PreviousIsOn != c.NewIsOn && c.IssuedAtUtc >= from)
            .Select(c => new Response(c.IssuedAtUtc, c.MacAddress, "command", c.Action))
            .ToList();

        var searchResponses = searches.GetAll()
            .Where(s => s.AtUtc >= from && s.ResultCount is > 0 and <= MaxResultsForSpecificSearch)
            .SelectMany(s => s.MatchedMacs.Select(mac => new Response(s.AtUtc, mac, "search", null)))
            .ToList();

        var historyCount = commandResponses.Count + searches.GetAll().Count(s => s.AtUtc >= from);
        var hasEnoughPatternData = historyCount >= settings.MinHistoryForPatterns;

        var recommendations = new List<RecommendationDto>();
        if (hasEnoughPatternData)
        {
            recommendations.AddRange(FindPatterns(alerts, recoveries, commandResponses.Concat(searchResponses).ToList()));
        }

        recommendations.AddRange(FindRecurringFaults(alerts));
        recommendations.AddRange(FindOutOfRangeReadings());
        recommendations.AddRange(FindDisconnectedDevices(now));

        var ordered = recommendations.OrderByDescending(r => r.Score).Take(MaxRecommendations).ToList();

        var summary = hasEnoughPatternData
            ? $"Analysed {alerts.Count} alert(s), {commandResponses.Count} command(s) and {searchResponses.Count} search match(es) from the last {settings.LookbackMinutes} min."
            : $"Not enough history to detect behaviour patterns yet: {historyCount} search(es)/command(s) recorded, at least {settings.MinHistoryForPatterns} needed. " +
              "Search for devices and send commands as you respond to alerts, and patterns will appear here. Fault-based insights are still shown.";

        return new RecommendationReportDto
        {
            HasEnoughPatternData = hasEnoughPatternData,
            Summary = summary,
            SearchesAnalysed = searches.GetAll().Count(s => s.AtUtc >= from),
            CommandsAnalysed = commandResponses.Count,
            AlertsAnalysed = alerts.Count,
            Recommendations = ordered
        };
    }

    private IEnumerable<RecommendationDto> FindPatterns(List<HistoryEntryDto> alerts, List<HistoryEntryDto> recoveries, List<Response> responses)
    {
        var settings = options.Recommendations;
        var window = TimeSpan.FromMinutes(settings.PatternWindowMinutes);

        // Everything is chronological (the history log is a SortedDictionary),
        // so the first recovery after an alert is the one that ended it.
        DateTimeOffset ResponseWindowEnd(HistoryEntryDto alert)
        {
            var end = alert.TimestampUtc + window;
            var recovery = recoveries.FirstOrDefault(r =>
                r.MacAddress == alert.MacAddress && r.Code == alert.Code && r.TimestampUtc >= alert.TimestampUtc);
            return recovery is not null && recovery.TimestampUtc < end ? recovery.TimestampUtc : end;
        }

        var alertsPerTrigger = new Dictionary<(string Mac, string Code), int>();
        var pairCounts = new Dictionary<PatternKey, int>();

        foreach (var alert in alerts)
        {
            if (alert.Code is null)
            {
                continue;
            }

            var trigger = (alert.MacAddress, alert.Code);
            alertsPerTrigger[trigger] = alertsPerTrigger.GetValueOrDefault(trigger) + 1;

            // A response only counts while the alert is still active: from the
            // alert until the device recovers (or the window runs out).
            // Something done after the recovery wasn't a response to it.
            var windowEnd = ResponseWindowEnd(alert);
            var countedForThisAlert = new HashSet<PatternKey>();
            foreach (var response in responses)
            {
                if (response.AtUtc < alert.TimestampUtc || response.AtUtc > windowEnd)
                {
                    continue;
                }

                var key = new PatternKey(alert.MacAddress, alert.Code, response.MacAddress, response.ResponseType, response.Action);
                if (countedForThisAlert.Add(key))
                {
                    pairCounts[key] = pairCounts.GetValueOrDefault(key) + 1;
                }
            }
        }

        foreach (var (key, count) in pairCounts)
        {
            var triggerAlerts = alertsPerTrigger[(key.TriggerMac, key.Code)];
            var confidence = (double)count / triggerAlerts;
            if (count < settings.MinPatternOccurrences || confidence < settings.MinPatternConfidence)
            {
                continue;
            }

            var triggerName = NameOf(key.TriggerMac);
            var responseName = NameOf(key.ResponseMac);
            var activeNow = errors.IsActive(key.TriggerMac, key.Code);
            var did = key.ResponseType == "command"
                ? $"switched {responseName} {(key.Action == CommandAction.TurnOn ? "ON" : "OFF")}"
                : $"searched for {responseName}";

            var reason = $"{triggerName} raised {key.Code} {triggerAlerts} time(s) in the last {settings.LookbackMinutes} min, " +
                         $"and {count} of those times ({confidence:P0}) you {did} within {settings.PatternWindowMinutes} min.";

            DeviceCommandRequest? suggestedCommand = null;
            string suggestedAction;

            if (key.ResponseType == "command" && key.Action is { } action)
            {
                var wantOn = action == CommandAction.TurnOn;
                var alreadyThere = registry.TryGet(key.ResponseMac, out var responseDevice) && responseDevice.IsOn == wantOn;

                if (activeNow && !alreadyThere)
                {
                    suggestedAction = $"{triggerName} is in {key.Code} again - switch {responseName} {(wantOn ? "ON" : "OFF")} as you usually do.";
                    suggestedCommand = new DeviceCommandRequest { MacAddress = key.ResponseMac, Action = action };
                }
                else
                {
                    suggestedAction = $"Consider an automation rule: when {triggerName} reports {key.Code}, switch {responseName} {(wantOn ? "ON" : "OFF")}.";
                }
            }
            else
            {
                suggestedAction = activeNow
                    ? $"{triggerName} is in {key.Code} again - you usually check {responseName} next."
                    : $"Pin {responseName} next to {triggerName} on your dashboard - you look it up after {key.Code} alerts.";
            }

            yield return new RecommendationDto
            {
                Type = "Pattern",
                MacAddress = key.TriggerMac,
                DeviceName = triggerName,
                Issue = $"Repeated response pattern: {key.Code} on {triggerName} -> {did}",
                Reason = reason,
                SuggestedAction = suggestedAction,
                Evidence = $"support {count}, confidence {confidence:P0}{(activeNow ? ", trigger active now" : "")}",
                Score = 50 + confidence * 40 + (activeNow ? 30 : 0) + Math.Min(count, 10),
                SuggestedCommand = suggestedCommand
            };
        }
    }

    private IEnumerable<RecommendationDto> FindRecurringFaults(List<HistoryEntryDto> alerts)
    {
        var settings = options.Recommendations;

        var groups = alerts
            .Where(a => a.Code is not null)
            .GroupBy(a => (a.MacAddress, Code: a.Code!));

        foreach (var group in groups)
        {
            var count = group.Count();
            if (count < settings.RecurringFaultThreshold)
            {
                continue;
            }

            var name = NameOf(group.Key.MacAddress);
            var (action, command) = RecurringFaultAdvice(group.Key.MacAddress, name, group.Key.Code);

            yield return new RecommendationDto
            {
                Type = "RecurringFault",
                MacAddress = group.Key.MacAddress,
                DeviceName = name,
                Issue = $"Recurring {group.Key.Code}: {count} alerts in the last {settings.LookbackMinutes} min",
                Reason = $"{name} has raised {group.Key.Code} {count} times (threshold {settings.RecurringFaultThreshold}), " +
                         $"most recently at {group.Last().TimestampUtc:HH:mm:ss} UTC.",
                SuggestedAction = action,
                Evidence = string.Join(", ", group.TakeLast(3).Select(a => a.Value is { } v ? $"{v:0.#}" : a.TimestampUtc.ToString("HH:mm:ss"))),
                Score = 40 + Math.Min(count, 10) * 5,
                SuggestedCommand = command
            };
        }
    }

    private (string Action, DeviceCommandRequest? Command) RecurringFaultAdvice(string mac, string name, string code)
    {
        switch (code)
        {
            case TelemetryClassifier.MoistureLow:
                var pump = PumpInSameZone(mac);
                return pump is null
                    ? ($"Inspect irrigation for {name}'s zone and check the sensor's calibration.", null)
                    : ($"Check {pump.Name}'s schedule and {name}'s calibration - the zone keeps drying out.", null);

            case TelemetryClassifier.PowerSpike:
                var isPlugOn = registry.TryGet(mac, out var plug) && plug.IsControllable && plug.IsOn;
                return isPlugOn
                    ? ($"Inspect the appliance on {name}; consider switching it OFF until it is checked.",
                        new DeviceCommandRequest { MacAddress = mac, Action = CommandAction.TurnOff })
                    : ($"Inspect the load and wiring behind {name}.", null);

            case TelemetryClassifier.Overheat:
                return ($"Check ventilation and enclosure temperature around {name}.", null);

            default:
                return ($"Check {name}'s power supply and Wi-Fi signal - it keeps dropping off the network.", null);
        }
    }

    private IEnumerable<RecommendationDto> FindOutOfRangeReadings()
    {
        foreach (var device in registry.GetAll())
        {
            if (!device.IsConnected || device.LastReading is not { } reading)
            {
                continue;
            }

            var classification = classifier.Classify(device.SensorType, reading);
            if (classification.Severity == ValidationSeverity.Info)
            {
                continue;
            }

            var critical = classification.Severity == ValidationSeverity.Critical;
            var range = ExpectedRange(device.SensorType);
            string action;
            DeviceCommandRequest? command = null;

            if (device.Kind == DeviceKind.MoistureSensor && PumpInSameZone(device.MacAddress) is { } pump)
            {
                action = pump.IsOn
                    ? $"{pump.Name} is already running - keep it on until moisture recovers above {options.Thresholds.MoistureWarningPct}%."
                    : $"Switch {pump.Name} ON to water {device.ZoneName}.";
                command = pump.IsOn ? null : new DeviceCommandRequest { MacAddress = pump.MacAddress, Action = CommandAction.TurnOn };
            }
            else if (device.Kind == DeviceKind.SmartPlug && device.IsOn && critical)
            {
                action = $"Switch {device.Name} OFF to protect the circuit.";
                command = new DeviceCommandRequest { MacAddress = device.MacAddress, Action = CommandAction.TurnOff };
            }
            else
            {
                action = $"Check {device.Name} - its reading is outside the normal range.";
            }

            yield return new RecommendationDto
            {
                Type = "OutOfRange",
                MacAddress = device.MacAddress,
                DeviceName = device.Name,
                Issue = $"{device.SensorType} {(critical ? "critically" : "slightly")} out of range: {reading:0.##} {device.Unit}".TrimEnd(),
                Reason = $"Latest reading {reading:0.##} {device.Unit} is outside the expected range ({range}).",
                SuggestedAction = action,
                Evidence = device.LastSeenUtc is { } seen ? $"read at {seen:HH:mm:ss} UTC" : null,
                Score = critical ? 45 : 25,
                SuggestedCommand = command
            };
        }
    }

    private IEnumerable<RecommendationDto> FindDisconnectedDevices(DateTimeOffset now)
    {
        foreach (var device in registry.GetAll().Where(d => !d.IsConnected))
        {
            var silentFor = device.LastSeenUtc is { } seen ? $"{(now - seen).TotalSeconds:0}s" : "since startup";
            yield return new RecommendationDto
            {
                Type = "Disconnected",
                MacAddress = device.MacAddress,
                DeviceName = device.Name,
                Issue = "Device offline",
                Reason = $"No telemetry from {device.Name} for {silentFor} (timeout {options.Thresholds.DisconnectAfterSeconds}s).",
                SuggestedAction = $"Check {device.Name}'s power supply and Wi-Fi. Commands to it will fail until it reconnects.",
                Evidence = device.LastSeenUtc is { } last ? $"last seen {last:HH:mm:ss} UTC" : null,
                Score = 35
            };
        }
    }

    private string ExpectedRange(string sensorType)
    {
        var t = options.Thresholds;
        return sensorType switch
        {
            "SoilMoisturePct" => $"at least {t.MoistureWarningPct}%",
            "PowerWatts" => $"at most {t.PowerWarningWatts} W",
            "TemperatureC" => $"at most {t.TemperatureWarningC} °C",
            _ => "normal"
        };
    }

    private SimulatedDevice? PumpInSameZone(string mac) =>
        registry.TryGet(mac, out var device)
            ? registry.GetAll().FirstOrDefault(d => d.Kind == DeviceKind.WaterPump && d.ZoneName == device.ZoneName)
            : null;

    private string NameOf(string mac) => registry.TryGet(mac, out var device) ? device.Name : mac;
}
