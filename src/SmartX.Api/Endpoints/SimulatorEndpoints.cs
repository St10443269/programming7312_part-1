using SmartX.Api.RealTime;

namespace SmartX.Api.Endpoints;

/// <summary>Controls for the device simulator (there is no physical hardware in this project).</summary>
public static class SimulatorEndpoints
{
    public static RouteGroupBuilder MapSimulatorEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", (DeviceSimulator simulator) => Results.Ok(simulator.GetStatus()));

        group.MapPost("/start", (DeviceSimulator simulator) =>
        {
            simulator.SetRunning(true);
            return Results.Ok(simulator.GetStatus());
        });

        group.MapPost("/stop", (DeviceSimulator simulator) =>
        {
            simulator.SetRunning(false);
            return Results.Ok(simulator.GetStatus());
        });

        // Simulates a device losing power/Wi-Fi: it stops reporting, and once
        // the disconnect timeout passes the API marks it Disconnected.
        group.MapPost("/devices/{mac}/disconnect", (string mac, int? seconds, DeviceSimulator simulator) =>
        {
            var duration = Math.Clamp(seconds ?? 20, 1, 300);
            return simulator.Disconnect(mac, duration)
                ? Results.Ok(new { message = $"{mac} will stop reporting for {duration}s (simulated outage)." })
                : Results.NotFound(new { message = $"Unknown device '{mac}'." });
        });

        return group;
    }
}
