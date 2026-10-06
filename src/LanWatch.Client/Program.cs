using LanWatch.Client;
using LanWatch.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<Session>();
builder.Services.AddScoped<Api>();
builder.Services.AddScoped<ScopeState>();
builder.Services.AddScoped<LiveConnection>();
builder.Services.AddScoped<ExceptionsState>();

await builder.Build().RunAsync();
