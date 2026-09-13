using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SmartX.Client;
using SmartX.Client.Services;

// Standalone Blazor WebAssembly: the dashboard runs entirely client-side and
// calls the API over HTTP, rather than Blazor Server's SignalR circuit model
// - chosen so the compiled app can be served as static files (e.g. by nginx
// in the Docker image) with no .NET server process of its own (Microsoft, 2025).
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress;

builder.Services.AddScoped<HttpClient>(_ => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
builder.Services.AddScoped<SmartXApiClient>();

await builder.Build().RunAsync();
