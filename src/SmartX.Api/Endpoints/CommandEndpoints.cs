using SmartX.Api.RealTime;
using SmartX.Shared.Dto;
using SmartX.Shared.Enums;

namespace SmartX.Api.Endpoints;

/// <summary>Manual device commands (normal HTTP requests) plus command history and undo.</summary>
public static class CommandEndpoints
{
    public static RouteGroupBuilder MapCommandEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (HttpRequest request, CommandService commands) =>
        {
            var (command, error) = await RequestBody.TryReadAsync<DeviceCommandRequest>(request);
            if (command is null)
            {
                return Results.BadRequest(new { message = error });
            }

            return ToHttpResult(commands.Execute(command));
        });

        group.MapPost("/undo", (CommandService commands) => ToHttpResult(commands.Undo()));

        group.MapGet("/", (CommandService commands, int? take) => Results.Ok(commands.GetLog(take ?? 50)));

        group.MapGet("/stack", (CommandService commands) => Results.Ok(commands.GetUndoStack()));

        return group;
    }

    // The full record is always returned so the UI can show what happened;
    // the status code tells it whether the command took effect.
    private static IResult ToHttpResult(CommandRecordDto record) => record.Outcome switch
    {
        CommandOutcome.Accepted => Results.Ok(record),
        CommandOutcome.Failed => Results.Json(record, statusCode: StatusCodes.Status503ServiceUnavailable),
        _ when record.Message.StartsWith("Unknown device", StringComparison.Ordinal) => Results.NotFound(record),
        _ when record.IsUndo => Results.Conflict(record),
        _ => Results.BadRequest(record)
    };
}
