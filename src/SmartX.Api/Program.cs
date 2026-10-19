using System.Text.Json.Serialization;
using SmartX.Api.Endpoints;
using SmartX.Api.RealTime;
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

// Part 2 - real-time processing and device management. All singletons:
// the state is in-memory and shared by every request and background worker.
var realTimeOptions = builder.Configuration.GetSection("RealTime").Get<RealTimeOptions>() ?? new RealTimeOptions();
builder.Services.AddSingleton(realTimeOptions);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DeviceRegistry>();
builder.Services.AddSingleton<HistoryLog>();
builder.Services.AddSingleton<ActiveErrorTracker>();
builder.Services.AddSingleton<TelemetryClassifier>();
builder.Services.AddSingleton<LiveUpdateBroadcaster>();
builder.Services.AddSingleton<TelemetryProcessor>();
builder.Services.AddHostedService<ProcessingWorker>();

var app = builder.Build();

DemoNetworkSeeder.Seed(app.Services.GetRequiredService<DeviceRegistry>(), app.Services.GetRequiredService<ISensorRepository>());

app.UseCors(ClientCorsPolicy);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => Results.Ok(new { service = "Smart-X Ingestion API", status = "healthy" }));

app.MapGroup("/api/sensors").MapSensorEndpoints();
app.MapGroup("/api/telemetry").MapTelemetryEndpoints();
app.MapGroup("/api/deployment").MapDeploymentEndpoints();
app.MapGroup("/api/stream").MapStreamEndpoints();

app.Run();
