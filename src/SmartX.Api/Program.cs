using System.Text.Json.Serialization;
using SmartX.Api.Endpoints;
using SmartX.Api.Services;

// HTTP/JSON via Minimal APIs (Microsoft, n.d.) rather than a broker protocol
// such as MQTT (OASIS, 2019) or CoAP (Internet Engineering Task Force, 2014):
// this gateway's clients are a browser dashboard and simulated ingestion
// calls, not battery-powered devices on a lossy radio link, so the lower
// per-message overhead those protocols offer isn't the binding constraint here.
var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

const string ClientCorsPolicy = "SmartXClient";
var clientOrigins = builder.Configuration.GetSection("ClientOrigins").Get<string[]>()
    ?? ["https://localhost:7163", "http://localhost:5122"];

builder.Services.AddCors(options =>
{
    options.AddPolicy(ClientCorsPolicy, policy =>
        policy.WithOrigins(clientOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services.AddOpenApi();

builder.Services.AddSingleton<ISensorRepository, SensorRepository>();
builder.Services.AddSingleton<ITelemetryStore, TelemetryStore>();
builder.Services.AddSingleton<MockTelemetrySeeder>();

var app = builder.Build();

app.UseCors(ClientCorsPolicy);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => Results.Ok(new { service = "Smart-X Ingestion API", status = "healthy" }));

app.MapGroup("/api/sensors").MapSensorEndpoints();
app.MapGroup("/api/telemetry").MapTelemetryEndpoints();
app.MapGroup("/api/deployment").MapDeploymentEndpoints();

app.Run();
