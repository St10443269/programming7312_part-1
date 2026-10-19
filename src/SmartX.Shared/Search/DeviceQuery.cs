using SmartX.Shared.Dto;
using SmartX.Shared.Enums;

namespace SmartX.Shared.Search;

/// <summary>
/// Search text plus optional filters for the device list. Lives in the
/// shared library so the API (which records the search for the
/// recommendation engine) and the dashboard (which re-applies it as live
/// updates arrive) use exactly the same matching rules.
/// </summary>
public sealed class DeviceQuery
{
    /// <summary>Matched against device name, MAC address, zone, kind and active error codes.</summary>
    public string? Search { get; set; }
    public DeviceKind? Kind { get; set; }
    public SensorCategory? Category { get; set; }
    public DeviceStatus? Status { get; set; }

    /// <summary>Only devices with at least one active error.</summary>
    public bool AlertsOnly { get; set; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Search) && Kind is null && Category is null && Status is null && !AlertsOnly;

    public bool Matches(DeviceDto device)
    {
        if (Kind is { } kind && device.Kind != kind) return false;
        if (Category is { } category && device.Category != category) return false;
        if (Status is { } status && device.Status != status) return false;
        if (AlertsOnly && device.ActiveErrorCodes.Count == 0) return false;

        if (string.IsNullOrWhiteSpace(Search))
        {
            return true;
        }

        var text = Search.Trim();
        return Contains(device.Name, text)
            || Contains(device.MacAddress, text)
            || Contains(device.ZoneName, text)
            || Contains(device.Kind.ToString(), text)
            || device.ActiveErrorCodes.Any(code => Contains(code, text));
    }

    /// <summary>Short human-readable description, e.g. "'pump' + status=Active", stored in search history.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Search)) parts.Add($"'{Search.Trim()}'");
        if (Kind is not null) parts.Add($"kind={Kind}");
        if (Category is not null) parts.Add($"category={Category}");
        if (Status is not null) parts.Add($"status={Status}");
        if (AlertsOnly) parts.Add("alerts only");
        return parts.Count == 0 ? "(all devices)" : string.Join(" + ", parts);
    }

    private static bool Contains(string value, string text) =>
        value.Contains(text, StringComparison.OrdinalIgnoreCase);
}
