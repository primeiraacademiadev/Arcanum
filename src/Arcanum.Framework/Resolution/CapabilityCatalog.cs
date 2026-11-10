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

        if (definition.Templates is { Count: > 0 })
        {
            ValidateNext(definition, catalog);
            return;
        }

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
            if (!definition.Graph.Edges.Any(edge =>
                edge.From == id && edge.Intention != CapabilityGraph.SpawnIntention))
                throw new CapabilityDefinitionException(
                    $"Node '{id}' needs at least one intention-based continuation.");
        }

        var conditional = new HashSet<(string Node, string Intention)>();
        foreach (var edge in definition.Graph.Edges)
        {
            if (!definition.Graph.Nodes.ContainsKey(edge.From) || string.IsNullOrWhiteSpace(edge.To))
                throw new CapabilityDefinitionException($"Invalid edge source or destination on '{edge.From}'.");
            if (string.IsNullOrWhiteSpace(edge.Intention))
                throw new CapabilityDefinitionException($"Missing intention on edge from '{edge.From}'.");

            var isSpawn = edge.Intention == CapabilityGraph.SpawnIntention;
            if (!isSpawn)
            {
                if (edge.Intention.StartsWith('@'))
                    throw new CapabilityDefinitionException(
                        $"Reserved system intention '{edge.Intention}' is not supported on '{edge.From}'.");
                if (!conditional.Add((edge.From, edge.Intention)))
                    throw new CapabilityDefinitionException(
                        $"Node '{edge.From}' declares intention '{edge.Intention}' more than once.");
            }

            if (edge.To.StartsWith('@'))
            {
                if (isSpawn || edge.To is not ("@stop" or "@complete" or "@fail"))
                    throw new CapabilityDefinitionException($"Unsupported terminal edge '{edge.To}'.");
            }
            else if (!definition.Graph.Nodes.ContainsKey(edge.To))
                throw new CapabilityDefinitionException(
                    $"Unknown target node '{edge.To}' from '{edge.From}'.");
        }

        ValidateContract(definition, catalog);
    }


    /// <summary>
    /// Compile the vNext declarative contract from trusted CLR Process signatures.
    /// It fills runtime-only Process/output bindings while rejecting drift at startup.
    /// </summary>
    private static void ValidateNext(CapabilityDefinition definition, ProcessCatalog catalog)
    {
        if (definition.Models is null || definition.Models.Count == 0 ||
            definition.Graph is null || definition.Templates is null)
            throw new CapabilityDefinitionException("Capability JSON vNext requires models, templates and graph.");
        if (definition.Policies is null)
            throw new CapabilityDefinitionException("Capability policies cannot be null.");
        if (definition.Policies.MaxSteps is <= 0 || definition.Policies.MaxDepth is <= 0)
            throw new CapabilityDefinitionException("Capability policies maxSteps/maxDepth must be positive.");
        if (definition.Policies.Timeout is { } duration)
        {
            try
            {
                if (System.Xml.XmlConvert.ToTimeSpan(duration) <= TimeSpan.Zero)
                    throw new CapabilityDefinitionException("Capability timeout must be positive.");
            }
            catch (FormatException)
            {
                throw new CapabilityDefinitionException(
                    $"Capability '{definition.Name}' has an invalid ISO 8601 timeout.");
            }
        }

        Type ModelType(string alias)
        {
            if (!definition.Models.TryGetValue(alias, out var model) ||
                model is null || string.IsNullOrWhiteSpace(model.Type))
                throw new CapabilityDefinitionException($"Unknown or invalid Model '{alias}'.");
            return catalog.GetDataType(model.Type);
        }

        foreach (var (alias, model) in definition.Models)
        {
            if (string.IsNullOrWhiteSpace(alias) || model is null ||
                string.IsNullOrWhiteSpace(model.Type))
                throw new CapabilityDefinitionException($"Invalid Model '{alias}'.");
            _ = ModelType(alias);
        }

        if (definition.Graph.Nodes is null || definition.Graph.Nodes.Count == 0 ||
            !definition.Graph.Nodes.ContainsKey(definition.Graph.Entry) ||
            definition.Graph.Edges is null)
            throw new CapabilityDefinitionException("Capability graph requires entry, nodes and edges.");

        foreach (var (id, template) in definition.Templates)
        {
            if (string.IsNullOrWhiteSpace(id) || template is null ||
                string.IsNullOrWhiteSpace(template.Process))
                throw new CapabilityDefinitionException($"Invalid NodeTemplate '{id}'.");
            var descriptor = catalog.Get(template.Process);
            if (template.Inputs is null || template.Outputs is null || template.Resources is null)
                throw new CapabilityDefinitionException($"NodeTemplate '{id}' has missing contracts.");

            var functional = descriptor.Parameters.Where(p => !p.IsCancellationToken).ToArray();
            if (functional.Length != template.Inputs.Count)
                throw new CapabilityDefinitionException(
                    $"NodeTemplate '{id}' input contract differs from Execute signature.");
            foreach (var parameter in functional)
            {
                if (!template.Inputs.TryGetValue(parameter.ParameterName, out var modelName) ||
                    !parameter.ParameterType.IsAssignableFrom(ModelType(modelName)))
                    throw new CapabilityDefinitionException(
                        $"NodeTemplate '{id}' parameter '{parameter.ParameterName}' differs from CLR signature.");
            }

            if (template.Outputs.Count != 1 ||
                !ModelType(template.Outputs.Single().Value).IsAssignableFrom(descriptor.ReturnDataType))
                throw new CapabilityDefinitionException(
                    $"NodeTemplate '{id}' output differs from ProcessResult<T>.");

            var constructorParameters = descriptor.ProcessType.GetConstructors()
                .SelectMany(ctor => ctor.GetParameters()).ToArray();
            foreach (var (name, resourceType) in template.Resources)
                if (!constructorParameters.Any(parameter =>
                    parameter.Name == name && parameter.ParameterType.FullName == resourceType))
                    throw new CapabilityDefinitionException(
                        $"NodeTemplate '{id}' resource '{name}' differs from constructor signature.");
            // Resources omitted from JSON (for example ILogger<T>) are resolved by Host DI.
        }

        var available = new Dictionary<string, Type>(StringComparer.Ordinal);
        void Register(string name, Type type)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new CapabilityDefinitionException("Execution data variable name must not be blank.");
            if (available.TryGetValue(name, out var existing) &&
                !existing.IsAssignableFrom(type) && !type.IsAssignableFrom(existing))
                throw new CapabilityDefinitionException(
                    $"Execution data variable '{name}' has incompatible contracts '{existing}' and '{type}'.");
            available.TryAdd(name, type);
        }

        if (definition.Contract is null || definition.Contract.Inputs is null ||
            definition.Contract.Inputs.Count == 0 || definition.Contract.Response is null)
            throw new CapabilityDefinitionException(
                "Capability JSON vNext requires contract.inputs and a single contract.response.");

        foreach (var (name, input) in definition.Contract.Inputs)
        {
            if (input is null || string.IsNullOrWhiteSpace(input.Model))
                throw new CapabilityDefinitionException($"Input '{name}' requires a Model.");
            var type = ModelType(input.Model);
            input.Type = type.FullName!;
            if (input.Source is null && input.Default.ValueKind == JsonValueKind.Undefined)
                input.Source = "$";
            var fromPayload = input.Source is not null;
            var fromDefault = input.Default.ValueKind != JsonValueKind.Undefined;
            if (fromPayload == fromDefault ||
                (fromPayload && !CapabilityContractResolver.IsValidSource(input.Source!)))
                throw new CapabilityDefinitionException($"Input '{name}' has invalid source/default.");
            if (fromDefault)
            {
                try
                {
                    if (input.Default.Deserialize(type, CapabilityContractResolver.JsonOptions) is null)
                        throw new CapabilityDefinitionException($"Input '{name}' has a null default.");
                }
                catch (JsonException e)
                {
                    throw new CapabilityDefinitionException(
                        $"Input '{name}' has invalid default data: {e.Message}");
                }
            }
            Register(name, type);
        }

        foreach (var (id, node) in definition.Graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(id) || id.StartsWith('@') || node is null ||
                string.IsNullOrWhiteSpace(node.Template) ||
                !definition.Templates.TryGetValue(node.Template, out var template) ||
                node.Bindings is null || node.Bindings.Inputs is null ||
                node.Bindings.Outputs is null)
                throw new CapabilityDefinitionException($"Node '{id}' requires a valid template and bindings.");

            var descriptor = catalog.Get(template.Process);
            if (node.Bindings.Inputs.Count != template.Inputs.Count ||
                node.Bindings.Outputs.Count != 1 ||
                node.Bindings.Outputs.Keys.Single() != template.Outputs.Keys.Single())
                throw new CapabilityDefinitionException($"Node '{id}' bindings differ from template.");
            foreach (var (parameter, modelName) in template.Inputs)
            {
                if (!node.Bindings.Inputs.TryGetValue(parameter, out var dataName) ||
                    string.IsNullOrWhiteSpace(dataName))
                    throw new CapabilityDefinitionException($"Node '{id}' is missing binding '{parameter}'.");
                // The variable might be supplied later; type compatibility is checked
                // against all known declarations after collecting graph outputs.
            }

            var output = node.Bindings.Outputs.Single();
            Register(output.Value, ModelType(template.Outputs[output.Key]));
            node.Process = template.Process;
            node.SaveResultAs = output.Value;
        }

        foreach (var (id, node) in definition.Graph.Nodes)
        {
            var template = definition.Templates[node.Template!];
            foreach (var (parameter, modelName) in template.Inputs)
            {
                var name = node.Bindings.Inputs[parameter];
                if (!available.TryGetValue(name, out var actual) ||
                    !ModelType(modelName).IsAssignableFrom(actual))
                    throw new CapabilityDefinitionException(
                        $"Node '{id}' input '{parameter}' references missing or incompatible data '{name}'.");
            }
        }

        var conditional = new HashSet<(string Node, string Intention)>();
        foreach (var edge in definition.Graph.Edges)
        {
            if (edge is null || !definition.Graph.Nodes.ContainsKey(edge.From) ||
                string.IsNullOrWhiteSpace(edge.To) ||
                !definition.Graph.Nodes.ContainsKey(edge.To) ||
                string.IsNullOrWhiteSpace(edge.Intention))
                throw new CapabilityDefinitionException("Invalid vNext edge source, destination or intention.");
            if (edge.Intention != CapabilityGraph.SpawnIntention)
            {
                if (edge.Intention.StartsWith('@') ||
                    !conditional.Add((edge.From, edge.Intention)))
                    throw new CapabilityDefinitionException(
                        $"Invalid or duplicate functional intention '{edge.Intention}' on '{edge.From}'.");
            }
        }

        var response = definition.Contract.Response;
        if (string.IsNullOrWhiteSpace(response.Name) || string.IsNullOrWhiteSpace(response.Model))
            throw new CapabilityDefinitionException("Implicit response requires name and model.");
        var responseType = ModelType(response.Model);
        if (!available.TryGetValue(response.Name, out var declared) ||
            !responseType.IsAssignableFrom(declared))
            throw new CapabilityDefinitionException(
                $"Response variable '{response.Name}' is not produced with the declared Model.");
        response.Type = responseType.FullName!;
        definition.Contract.Responses["@complete"] = response;
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
