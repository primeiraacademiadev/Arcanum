using Arcanum.Demo.Api;
using Arcanum.Framework;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging();
builder.Services.AddScoped<IExecutionProbe, ExecutionProbe>();
builder.Services.AddScoped<ParallelFaultGate>();
builder.Services.AddArcanumFramework(typeof(ValidateProcess).Assembly);
builder.Services.Configure<CapabilityOptions>(options =>
    options.DefaultMaxSteps = builder.Configuration.GetValue("Arcanum:DefaultMaxSteps", 100));
builder.Services.AddArcanumCapabilitiesFromDirectory(
    Path.Combine(AppContext.BaseDirectory, "capabilities"));

var app = builder.Build();

// Fail at startup if a JSON graph references a missing or malformed Process.
_ = app.Services.GetRequiredService<CapabilityCatalog>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Local read-only Capability Atlas. No arbitrary filesystem route is exposed.
// Browser-selected folders are read in the browser, without uploading files.
// Route matching ignores a trailing slash; normalize before endpoint selection
// to avoid two ambiguous Minimal API endpoints for /viewer and /viewer/.
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/viewer")
    {
        context.Response.Redirect("/viewer/");
        return;
    }
    await next();
});
app.MapGet("/viewer/", () =>
    Results.File(Path.Combine(AppContext.BaseDirectory, "viewer", "index.html"), "text/html; charset=utf-8"));
app.MapGet("/viewer/{asset}", (string asset) =>
{
    var contentType = asset switch
    {
        "viewer.mjs" or "model.mjs" => "text/javascript; charset=utf-8",
        "styles.css" => "text/css; charset=utf-8",
        _ => null
    };
    return contentType is null ? Results.NotFound() :
        Results.File(Path.Combine(AppContext.BaseDirectory, "viewer", asset), contentType);
});

app.MapGet("/viewer/api/snapshot", (ProcessResolver processResolver) =>
{
    var directory = Path.Combine(AppContext.BaseDirectory, "capabilities");
    var paths = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
        .OrderBy(path => path, StringComparer.Ordinal).ToArray();
    var definitions = paths.Select(path => (
        FileName: Path.GetFileName(path),
        Definition: CapabilityCatalog.ParseJson(File.ReadAllText(path))))
        .ToArray();

    // The manifest is derived from the *actual trusted CLR signatures*, never
    // from an untrusted template or client-provided class name.
    var processNames = definitions
        .SelectMany(item =>
            item.Definition.Processes.Keys.Concat(
                item.Definition.Templates.Values.Select(template => template.Process)))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.Ordinal);
    var signatures = new Dictionary<string, object>(StringComparer.Ordinal);
    foreach (var processName in processNames)
    {
        var descriptor = processResolver.GetDescriptor(processName);
        var constructor = descriptor.ProcessType.GetConstructors()
            .OrderByDescending(ctor => ctor.GetParameters().Length).FirstOrDefault();
        signatures[processName] = new
        {
            inputs = descriptor.Parameters
                .Where(p => !p.IsCancellationToken)
                .Select(p => new
                {
                    name = p.ParameterName,
                    type = p.ParameterType.FullName,
                    dataName = p.DataName
                }).ToArray(),
            outputType = descriptor.ReturnDataType.FullName,
            returnName = descriptor.ReturnName,
            resources = constructor?.GetParameters()
                .Where(parameter =>
                    !(parameter.ParameterType.IsGenericType &&
                      parameter.ParameterType.GetGenericTypeDefinition() == typeof(ILogger<>)))
                .Select(parameter => new
                {
                    name = parameter.Name,
                    type = parameter.ParameterType.FullName,
                    inferred = true,
                    automatic = false
                }).ToArray() ?? []
        };
    }

    var files = paths.Select(path => new
    {
        name = Path.GetFileName(path),
        document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone()
    }).ToArray();
    return Results.Json(new { files, processContracts = signatures });
});

// The framework adapter owns contract conversion and HTTP result mapping.
app.MapArcanumCapabilityEndpoint(namePrefix: "demo.");

app.Run();

// Kept public so an eventual integration test host can reference the entry point.
public partial class Program { }
