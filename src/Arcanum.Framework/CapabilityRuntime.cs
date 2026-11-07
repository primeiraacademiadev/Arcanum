using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcanum.Framework;

public sealed class CapabilityCatalog
{
    private readonly Dictionary<string, CapabilityDefinition> _capabilities;

    public CapabilityCatalog(
        IEnumerable<CapabilityDefinition> definitions,
        ProcessCatalog processCatalog,
        CapabilityOptions options)
    {
        if (options.DefaultMaxSteps <= 0)
            throw new CapabilityDefinitionException("CapabilityOptions.DefaultMaxSteps must be positive.");

        _capabilities = new Dictionary<string, CapabilityDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            Validate(definition, processCatalog);
            if (!_capabilities.TryAdd(definition.Name, definition))
                throw new CapabilityDefinitionException($"Duplicate Capability '{definition.Name}'.");
        }

        if (_capabilities.Count == 0)
            throw new CapabilityDefinitionException("No Capability definitions were registered.");
    }

    public CapabilityDefinition Get(string name) =>
        _capabilities.TryGetValue(name, out var definition)
            ? definition
            : throw new CapabilityDefinitionException($"Unknown Capability '{name}'.");

    public bool Contains(string name) => _capabilities.ContainsKey(name);

    public static CapabilityCatalog LoadFromDirectory(
        string directory,
        ProcessCatalog processes,
        CapabilityOptions options)
    {
        if (!Directory.Exists(directory))
            throw new CapabilityDefinitionException($"Capability directory does not exist: {directory}");

        var definitions = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(file => ParseJson(File.ReadAllText(file)));
        return new CapabilityCatalog(definitions, processes, options);
    }

    public static CapabilityDefinition ParseJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<CapabilityDefinition>(
                json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new CapabilityDefinitionException("Empty Capability JSON.");
        }
        catch (JsonException exception)
        {
            throw new CapabilityDefinitionException($"Invalid Capability JSON: {exception.Message}");
        }
    }

    private static void Validate(CapabilityDefinition definition, ProcessCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(definition.Name) ||
            string.IsNullOrWhiteSpace(definition.DisplayName) ||
            definition.Version <= 0)
            throw new CapabilityDefinitionException("Capability requires name, displayName and positive version.");

        if (definition.MaxSteps is <= 0)
            throw new CapabilityDefinitionException($"Capability '{definition.Name}' maxSteps must be positive.");

        if (definition.Processes is null || definition.Processes.Count == 0 || definition.Graph is null)
            throw new CapabilityDefinitionException($"Capability '{definition.Name}' requires Processes and Graph.");

        foreach (var (name, metadata) in definition.Processes)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new CapabilityDefinitionException("Process type name cannot be empty.");
            catalog.Get(name);
            if (metadata is null || string.IsNullOrWhiteSpace(metadata.DisplayName) ||
                string.IsNullOrWhiteSpace(metadata.SaveResultAs))
                throw new CapabilityDefinitionException(
                    $"Process '{name}' requires displayName and saveResultAs.");
        }

        if (!definition.Processes.ContainsKey(definition.Graph.Entry))
            throw new CapabilityDefinitionException($"Unknown entry Process '{definition.Graph.Entry}'.");

        var transitions = definition.Graph.Transitions ??
            throw new CapabilityDefinitionException("Missing graph transitions.");

        foreach (var name in definition.Processes.Keys)
            if (!transitions.ContainsKey(name) || transitions[name] is null ||
                transitions[name].Count == 0)
                throw new CapabilityDefinitionException($"Process '{name}' has no transition mapping.");

        foreach (var (source, mapping) in transitions)
        {
            if (!definition.Processes.ContainsKey(source))
                throw new CapabilityDefinitionException($"Transition source '{source}' is not a Process.");

            foreach (var (intention, target) in mapping)
            {
                if (string.IsNullOrWhiteSpace(intention) || intention.StartsWith('@') ||
                    string.IsNullOrWhiteSpace(target))
                    throw new CapabilityDefinitionException($"Invalid intention mapping on '{source}'.");

                if (target.StartsWith('@'))
                {
                    if (target is not ("@stop" or "@complete" or "@fail"))
                        throw new CapabilityDefinitionException($"Unsupported graph directive '{target}'.");
                }
                else if (!definition.Processes.ContainsKey(target))
                    throw new CapabilityDefinitionException(
                        $"Target Process '{target}' of '{source}' is not registered.");
            }
        }
    }
}

public sealed class CapabilityResolver(
    CapabilityCatalog capabilities,
    ProcessResolver processes,
    DataResolver dataResolver,
    IOptions<CapabilityOptions> options,
    ILogger<CapabilityResolver> logger)
{
    private static readonly ActivitySource Traces = new("Arcanum.Framework.Capabilities");

    public async Task<CapabilityExecutionResult> ExecuteAsync(
        string name,
        IEnumerable<ExecutionDatum> initialData,
        IServiceProvider resourceScope,
        CancellationToken cancellationToken = default)
    {
        var definition = capabilities.Get(name);
        var maxSteps = definition.MaxSteps ?? options.Value.DefaultMaxSteps;
        var data = dataResolver.CreateScope(initialData);
        var processName = definition.Graph.Entry;
        var steps = 0;

        using var trace = Traces.StartActivity("capability.execute");
        trace?.SetTag("capability.name", definition.Name);
        trace?.SetTag("capability.version", definition.Version);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (steps >= maxSteps)
                throw new CapabilityExecutionLimitExceededException(
                    name, definition.Version, processName, maxSteps);
            steps++;

            var descriptor = processes.GetDescriptor(processName);
            var process = processes.Resolve(descriptor, resourceScope);
            var args = dataResolver.Bind(descriptor, data, cancellationToken);

            using var stepTrace = Traces.StartActivity("capability.process");
            stepTrace?.SetTag("process.type", processName);
            stepTrace?.SetTag("process.step", steps);

            var result = await processes.InvokeAsync(descriptor, process, args);
            if (string.IsNullOrWhiteSpace(result.Intention))
                throw new CapabilityTransitionException(
                    $"Process '{processName}' returned an empty intention.");

            var metadata = definition.Processes[processName];
            dataResolver.Store(data, metadata.SaveResultAs, result);

            if (!definition.Graph.Transitions[processName].TryGetValue(result.Intention, out var next))
                throw new CapabilityTransitionException(
                    $"Process '{processName}' returned unmapped intention '{result.Intention}'.");

            stepTrace?.SetTag("process.intention", result.Intention);
            stepTrace?.SetTag("process.next", next);
            logger.LogInformation(
                "Capability {Capability} step {Step} Process {Process} intention {Intention} next {Next}",
                name, steps, processName, result.Intention, next);

            if (next.StartsWith('@'))
                return new CapabilityExecutionResult(
                    name, definition.Version, next, result.Intention, steps, result.Data);

            processName = next;
        }
    }
}

public static class ArcanumServiceCollectionExtensions
{
    public static IServiceCollection AddArcanumFramework(
        this IServiceCollection services, params Assembly[] processAssemblies)
    {
        if (processAssemblies.Length == 0)
            throw new ArgumentException("Specify at least one Process assembly.", nameof(processAssemblies));

        var types = processAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(IProcess).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
            .Distinct()
            .ToArray();

        var catalog = new ProcessCatalog(types);
        services.AddSingleton(catalog);
        foreach (var type in types)
            services.AddTransient(type);

        services.AddOptions<CapabilityOptions>();
        services.AddSingleton<ProcessResolver>();
        services.AddSingleton<DataResolver>();
        services.AddSingleton<CapabilityResolver>();
        return services;
    }

    public static IServiceCollection AddArcanumCapabilitiesFromDirectory(
        this IServiceCollection services, string directory)
    {
        services.AddSingleton(sp => CapabilityCatalog.LoadFromDirectory(
            directory,
            sp.GetRequiredService<ProcessCatalog>(),
            sp.GetRequiredService<IOptions<CapabilityOptions>>().Value));
        return services;
    }
}
