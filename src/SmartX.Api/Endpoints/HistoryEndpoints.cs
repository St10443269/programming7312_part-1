using SmartX.Api.RealTime;
using SmartX.Shared.Enums;

namespace SmartX.Api.Endpoints;

/// <summary>Read access to the chronological history log and the active error set.</summary>
public static class HistoryEndpoints
{
    public static RouteGroupBuilder MapHistoryEndpoints(this RouteGroupBuilder group)
    {
        // e.g. /api/history?mac=24:6F:28:B1:00:01&kind=Reading&minutes=10&take=100
        group.MapGet("/history", (string? mac, string? kind, int? minutes, int? take, HistoryLog history, TimeProvider clock) =>
        {
            HistoryEntryKind? kindFilter = null;
            if (!string.IsNullOrWhiteSpace(kind))
            {
                if (!Enum.TryParse<HistoryEntryKind>(kind, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                {
                    return Results.BadRequest(new { message = $"'{kind}' is not a valid kind. Use one of: {string.Join(", ", Enum.GetNames<HistoryEntryKind>())}." });
                }

                kindFilter = parsed;
            }

            DateTimeOffset? from = minutes is > 0 ? clock.GetUtcNow().AddMinutes(-minutes.Value) : null;
            return Results.Ok(history.Query(mac, kindFilter, from, Math.Clamp(take ?? 200, 1, 2000)));
        });

        group.MapGet("/errors", (ActiveErrorTracker errors) => Results.Ok(errors.GetAll()));

        return group;
    }
}
