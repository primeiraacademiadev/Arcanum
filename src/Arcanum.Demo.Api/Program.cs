using Arcanum.Demo.Api;
using Arcanum.Framework;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging();
builder.Services.AddScoped<IExecutionProbe, ExecutionProbe>();
builder.Services.AddArcanumFramework(typeof(ValidateProcess).Assembly);
builder.Services.Configure<CapabilityOptions>(options =>
    options.DefaultMaxSteps = builder.Configuration.GetValue("Arcanum:DefaultMaxSteps", 100));
builder.Services.AddArcanumCapabilitiesFromDirectory(
    Path.Combine(AppContext.BaseDirectory, "capabilities"));

var app = builder.Build();

// Fail at startup if a JSON graph references a missing or malformed Process.
_ = app.Services.GetRequiredService<CapabilityCatalog>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// The framework adapter owns contract conversion and HTTP result mapping.
app.MapArcanumCapabilityEndpoint(namePrefix: "demo.");

app.Run();

// Kept public so an eventual integration test host can reference the entry point.
public partial class Program { }
