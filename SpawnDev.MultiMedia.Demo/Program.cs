using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SpawnDev.BlazorJS;
using SpawnDev.MultiMedia.Demo;
using SpawnDev.MultiMedia.Demo.UnitTests;

// Print build timestamp
var buildTimestamp = typeof(Program).Assembly
    .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
    .OfType<System.Reflection.AssemblyMetadataAttribute>()
    .FirstOrDefault(a => a.Key == "BuildTimestamp")?.Value ?? "unknown";
Console.WriteLine($"SpawnDev.MultiMedia.Demo build: {buildTimestamp}");

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddBlazorJSRuntime();

// Unit tests
builder.Services.AddSingleton<WasmMultiMediaTests>();

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

await builder.Build().BlazorJSRunAsync();
