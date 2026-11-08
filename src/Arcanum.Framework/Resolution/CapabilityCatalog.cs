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
        if (options.DefaultMaxDepth <= 0)
            throw new CapabilityDefinitionException("CapabilityOptions.DefaultMaxDepth must be positive.");

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
        if (definition.MaxDepth is <= 0)
            throw new CapabilityDefinitionException($"Capability '{definition.Name}' maxDepth must be positive.");

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

        if (definition.Graph.Nodes is null || definition.Graph.Nodes.Count == 0)
            throw new CapabilityDefinitionException("Capability graph requires uniquely identified nodes.");
        if (!definition.Graph.Nodes.ContainsKey(definition.Graph.Entry))
            throw new CapabilityDefinitionException($"Unknown entry node '{definition.Graph.Entry}'.");
        if (definition.Graph.Edges is null)
            throw new CapabilityDefinitionException("Capability graph requires edges.");

        foreach (var (id, node) in definition.Graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(id) || id.StartsWith('@') || node is null ||
                string.IsNullOrWhiteSpace(node.Process) || !definition.Processes.ContainsKey(node.Process))
                throw new CapabilityDefinitionException($"Invalid graph node '{id}' or undeclared Process.");
            if (node.SaveResultAs is not null && string.IsNullOrWhiteSpace(node.SaveResultAs))
                throw new CapabilityDefinitionException($"Node '{id}' has an empty saveResultAs.");
            if (!definition.Graph.Edges.Any(edge => edge.From == id && !edge.Parallel))
                throw new CapabilityDefinitionException(
                    $"Node '{id}' needs at least one intention-based continuation.");
        }

        var conditional = new HashSet<(string Node, string Intention)>();
        foreach (var edge in definition.Graph.Edges)
        {
            if (!definition.Graph.Nodes.ContainsKey(edge.From) || string.IsNullOrWhiteSpace(edge.To))
                throw new CapabilityDefinitionException($"Invalid edge source or destination on '{edge.From}'.");
            if (edge.Parallel)
            {
                if (edge.Intention is not null)
                    throw new CapabilityDefinitionException(
                        $"Parallel edge from '{edge.From}' cannot declare an intention.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(edge.Intention) || edge.Intention.StartsWith('@'))
                    throw new CapabilityDefinitionException($"Invalid intention on '{edge.From}'.");
                if (!conditional.Add((edge.From, edge.Intention)))
                    throw new CapabilityDefinitionException(
                        $"Node '{edge.From}' declares intention '{edge.Intention}' more than once.");
            }

            if (edge.To.StartsWith('@'))
            {
                if (edge.Parallel || edge.To is not ("@stop" or "@complete" or "@fail"))
                    throw new CapabilityDefinitionException($"Unsupported terminal edge '{edge.To}'.");
            }
            else if (!definition.Graph.Nodes.ContainsKey(edge.To))
                throw new CapabilityDefinitionException(
                    $"Unknown target node '{edge.To}' from '{edge.From}'.");
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
        foreach (var (nodeId, node) in definition.Graph.Nodes)
        {
            var descriptor = catalog.Get(node.Process);
            var metadata = definition.Processes[node.Process];
            var key = node.SaveResultAs ?? metadata.SaveResultAs ?? descriptor.ReturnName!;
            if (produced.TryGetValue(key, out var previous) && previous != descriptor.ReturnDataType)
                throw new CapabilityDefinitionException(
                    $"Different output types use the same result name '{key}' (node '{nodeId}').");
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

        foreach (var terminal in definition.Graph.Edges.Select(edge => edge.To)
                     .Where(target => target is "@complete" or "@stop")
                     .Distinct(StringComparer.Ordinal))
            if (!contract.Responses.ContainsKey(terminal))
                throw new CapabilityDefinitionException(
                    $"Capability '{definition.Name}' requires a response declaration for '{terminal}'.");
    }
}
