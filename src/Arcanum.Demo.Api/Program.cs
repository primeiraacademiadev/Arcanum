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

app.MapPost("/capabilities/{name}/execute",
    async (string name, DemoInput request, HttpContext context,
        CapabilityCatalog catalog, CapabilityResolver resolver) =>
    {
        if (!name.StartsWith("demo.", StringComparison.Ordinal) || !catalog.Contains(name))
            return Results.NotFound();

        var result = await resolver.ExecuteAsync(
            name,
            new[]
            {
                ExecutionDatum.Of("request", request),
                ExecutionDatum.Of("counter", new CounterState(0))
            },
            context.RequestServices,
            context.RequestAborted);

        return result.Terminal switch
        {
            "@complete" => Results.Ok(result),
            "@stop" => Results.UnprocessableEntity(result),
            "@fail" => Results.Problem(title: "Capability failed", statusCode: 500),
            _ => Results.Problem(title: "Unknown terminal directive", statusCode: 500)
        };
    });

app.Run();

// Kept public so an eventual integration test host can reference the entry point.
public partial class Program;
