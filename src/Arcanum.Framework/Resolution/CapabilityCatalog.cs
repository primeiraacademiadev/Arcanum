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
            var descriptor = catalog.Get(name);
            if (metadata is null || string.IsNullOrWhiteSpace(metadata.DisplayName))
                throw new CapabilityDefinitionException(
                    $"Process '{name}' requires displayName.");
            if (metadata.SaveResultAs is not null && string.IsNullOrWhiteSpace(metadata.SaveResultAs))
                throw new CapabilityDefinitionException($"Process '{name}' has empty saveResultAs.");
            if (metadata.SaveResultAs is null && descriptor.ReturnName is null)
                throw new CapabilityDefinitionException(
                    $"Process '{name}' requires [ReturnName] or saveResultAs.");
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

        ValidateContract(definition, catalog);
    }

    private static void ValidateContract(CapabilityDefinition definition, ProcessCatalog catalog)
    {
        var contract = definition.Contract;
        if (contract is null || contract.Inputs is null || contract.Inputs.Count == 0)
            throw new CapabilityDefinitionException(
                $"Capability '{definition.Name}' requires a contract with inputs.");
        if (contract.Responses is null)
            throw new CapabilityDefinitionException(
                $"Capability '{definition.Name}' requires response declarations.");

        foreach (var (name, input) in contract.Inputs)
        {
            if (string.IsNullOrWhiteSpace(name) || input is null ||
                string.IsNullOrWhiteSpace(input.Type))
                throw new CapabilityDefinitionException("Contract inputs need a name and type.");

            var type = catalog.GetDataType(input.Type);
            var fromPayload = input.Source is not null;
            var fromDefault = input.Default.ValueKind != JsonValueKind.Undefined;
            if (fromPayload == fromDefault)
                throw new CapabilityDefinitionException(
                    $"Contract input '{name}' must have exactly one of source or default.");

            if (fromPayload && !CapabilityContractResolver.IsValidSource(input.Source!))
                throw new CapabilityDefinitionException(
                    $"Contract input '{name}' has unsupported source '{input.Source}'.");

            if (fromDefault)
            {
                try
                {
                    var value = input.Default.Deserialize(type, CapabilityContractResolver.JsonOptions);
                    if (value is null)
                        throw new CapabilityDefinitionException(
                            $"Contract input '{name}' has a null default.");
                }
                catch (JsonException e)
                {
                    throw new CapabilityDefinitionException(
                        $"Contract input '{name}' has invalid default data: {e.Message}");
                }
            }
        }

        var produced = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var (name, metadata) in definition.Processes)
        {
            var descriptor = catalog.Get(name);
            var key = metadata.SaveResultAs ?? descriptor.ReturnName!;
            if (produced.TryGetValue(key, out var previous) && previous != descriptor.ReturnDataType)
                throw new CapabilityDefinitionException(
                    $"Different output types use the same result name '{key}'.");
            produced[key] = descriptor.ReturnDataType;
        }

        foreach (var (terminal, response) in contract.Responses)
        {
            if (terminal is not ("@complete" or "@stop" or "@fail") ||
                response is null || string.IsNullOrWhiteSpace(response.Name) ||
                string.IsNullOrWhiteSpace(response.Type))
                throw new CapabilityDefinitionException(
                    $"Invalid contract response for terminal '{terminal}'.");
            if (!produced.TryGetValue(response.Name, out var actual) ||
                actual.FullName != response.Type)
                throw new CapabilityDefinitionException(
                    $"Contract response '{terminal}' must reference a produced result with its exact type.");
        }

        foreach (var terminal in definition.Graph.Transitions.Values
                     .SelectMany(mapping => mapping.Values)
                     .Where(target => target is "@complete" or "@stop")
                     .Distinct(StringComparer.Ordinal))
            if (!contract.Responses.ContainsKey(terminal))
                throw new CapabilityDefinitionException(
                    $"Capability '{definition.Name}' requires a response declaration for '{terminal}'.");
    }
}
