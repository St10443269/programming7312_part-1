using SmartX.Api.RealTime;
using SmartX.Shared.Enums;
using SmartX.Shared.Search;

namespace SmartX.Api.Endpoints;

/// <summary>Device search/filter over the real-time registry.</summary>
public static class DeviceEndpoints
{
    public static RouteGroupBuilder MapDeviceEndpoints(this RouteGroupBuilder group)
    {
        // e.g. /api/devices?search=pump&status=Active&alertsOnly=false
        // Filters arrive as strings and are parsed here so a bad value gets a
        // clear 400 message rather than a binding exception.
        group.MapGet("/", (string? search, string? kind, string? category, string? status, bool? alertsOnly, bool? record,
            DeviceRegistry registry, ActiveErrorTracker errors, SearchHistory searches) =>
        {
            if (!TryParse<DeviceKind>(kind, out var kindFilter, out var error)
                || !TryParse<SensorCategory>(category, out var categoryFilter, out error)
                || !TryParse<DeviceStatus>(status, out var statusFilter, out error))
            {
                return Results.BadRequest(new { message = error });
            }

            var query = new DeviceQuery
            {
                Search = search,
                Kind = kindFilter,
                Category = categoryFilter,
                Status = statusFilter,
                AlertsOnly = alertsOnly ?? false
            };

            var matches = registry.GetAll()
                .Select(device => device.ToDto(errors))
                .Where(query.Matches)
                .ToList();

            // Searches the user actually ran are remembered for the
            // recommendation engine (record=false skips this, e.g. for the
            // dashboard's own background refreshes).
            if ((record ?? true) && !query.IsEmpty)
            {
                searches.Record(query, matches.Select(d => d.MacAddress).ToList());
            }

            return Results.Ok(matches);
        });

        group.MapGet("/{mac}", (string mac, DeviceRegistry registry, ActiveErrorTracker errors) =>
            registry.TryGet(mac, out var device)
                ? Results.Ok(device.ToDto(errors))
                : Results.NotFound(new { message = $"Unknown device '{mac}'." }));

        group.MapGet("/searches", (SearchHistory searches, int? take) =>
            Results.Ok(Enumerable.Reverse(searches.GetAll()).Take(Math.Clamp(take ?? 20, 1, 200))));

        return group;
    }

    private static bool TryParse<TEnum>(string? raw, out TEnum? value, out string? error) where TEnum : struct, Enum
    {
        value = null;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            value = parsed;
            return true;
        }

        error = $"'{raw}' is not a valid {typeof(TEnum).Name}. Use one of: {string.Join(", ", Enum.GetNames<TEnum>())}.";
        return false;
    }
}
