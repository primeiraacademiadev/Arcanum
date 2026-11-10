using System.Text.Json;
using System.Text.Json.Nodes;

namespace Arcanum.Framework;

/// <summary>
/// Proposes JSON template contract changes after independently inspecting CLR signatures.
/// Functional graph topology, descriptions and user bindings are never silently rewritten.
/// </summary>
public sealed class CapabilityContractSynchronizer(ProcessCatalog processes)
{
    public sealed record Report(bool HasDrift, string ProposedJson, IReadOnlyList<string> Differences);

    public Report Analyze(string capabilityJson)
    {
        var root = JsonNode.Parse(capabilityJson) as JsonObject
            ?? throw new CapabilityDefinitionException("Capability JSON must be an object.");
        var models = root["models"] as JsonObject
            ?? throw new CapabilityDefinitionException("Capability JSON synchronizer requires models.");
        var templates = root["templates"] as JsonObject
            ?? throw new CapabilityDefinitionException("Capability JSON synchronizer requires templates.");
        var changes = new List<string>();

        bool Matches(string? alias, Type type) =>
            alias is not null && models[alias] is JsonObject model &&
            (string?)model["type"]?.GetValue<string>() == type.FullName;

        string FindOrCreateModel(Type type)
        {
            foreach (var (alias, _) in models)
                if (Matches(alias, type))
                    return alias;
            var prefix = type.Name.Replace('`', '_');
            var name = prefix;
            for (var suffix = 2; models.ContainsKey(name); suffix++)
                name = prefix + suffix;
            models[name] = new JsonObject { ["type"] = type.FullName };
            changes.Add($"Created Model '{name}' for CLR type '{type.FullName}'.");
            return name;
        }

        foreach (var (templateName, templateNode) in templates)
        {
            if (templateNode is not JsonObject template ||
                template["process"] is not JsonNode processNode)
                throw new CapabilityDefinitionException(
                    $"NodeTemplate '{templateName}' has no Process.");
            var descriptor = processes.Get(processNode.GetValue<string>());

            var oldInputs = template["inputs"] as JsonObject ?? new JsonObject();
            var proposedInputs = new JsonObject();
            foreach (var parameter in descriptor.Parameters.Where(p => !p.IsCancellationToken))
            {
                var previous = oldInputs[parameter.ParameterName]?.GetValue<string>();
                proposedInputs[parameter.ParameterName] = Matches(previous, parameter.ParameterType)
                    ? previous : FindOrCreateModel(parameter.ParameterType);
            }
            if (!JsonNode.DeepEquals(oldInputs, proposedInputs))
            {
                template["inputs"] = proposedInputs;
                changes.Add($"Template '{templateName}' Execute inputs differ from CLR signature.");
            }

            var oldOutputs = template["outputs"] as JsonObject ?? new JsonObject();
            var resultName = oldOutputs.Count == 1 ? oldOutputs.First().Key : "result";
            var previousResultModel = oldOutputs[resultName]?.GetValue<string>();
            var outputModel = Matches(previousResultModel, descriptor.ReturnDataType)
                ? previousResultModel : FindOrCreateModel(descriptor.ReturnDataType);
            var proposedOutputs = new JsonObject { [resultName] = outputModel };
            if (!JsonNode.DeepEquals(oldOutputs, proposedOutputs))
            {
                template["outputs"] = proposedOutputs;
                changes.Add($"Template '{templateName}' ProcessResult<T> type differs from CLR signature.");
            }

            // Resources can be omitted, including ILogger<T>. Existing explicit
            // declarations are kept in sync where the constructor still has a match.
            if (template["resources"] is JsonObject resources)
            {
                var parameters = descriptor.ProcessType.GetConstructors()
                    .SelectMany(ctor => ctor.GetParameters()).ToArray();
                foreach (var (resourceName, declared) in resources.ToArray())
                {
                    var parameter = parameters.FirstOrDefault(p => p.Name == resourceName);
                    if (parameter is null)
                    {
                        resources.Remove(resourceName);
                        changes.Add($"Template '{templateName}' constructor no longer has '{resourceName}'.");
                    }
                    else if (declared?.GetValue<string>() != parameter.ParameterType.FullName)
                    {
                        resources[resourceName] = parameter.ParameterType.FullName;
                        changes.Add($"Template '{templateName}' resource '{resourceName}' differs from constructor.");
                    }
                }
            }
        }

        return new Report(changes.Count > 0,
            changes.Count == 0 ? capabilityJson :
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            changes);
    }
}
