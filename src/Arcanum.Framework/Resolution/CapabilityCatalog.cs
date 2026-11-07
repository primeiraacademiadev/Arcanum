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
