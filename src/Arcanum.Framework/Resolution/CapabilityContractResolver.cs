using System.Text.Json;
using System.Text.RegularExpressions;

namespace Arcanum.Framework;

/// <summary>Converts a transport JSON document into typed, named Capability input objects.</summary>
public sealed class CapabilityContractResolver(ProcessCatalog processes)
{
    internal static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    internal static bool IsValidSource(string source) =>
        Regex.IsMatch(source, @"^\$(\.[A-Za-z_][A-Za-z0-9_]*)*$",
            RegexOptions.CultureInvariant);

    public IReadOnlyList<ExecutionDatum> BindInput(CapabilityDefinition definition, JsonElement payload)
    {
        var items = new List<ExecutionDatum>(definition.Contract.Inputs.Count);
        foreach (var (name, input) in definition.Contract.Inputs)
        {
            var type = processes.GetDataType(input.Type);
            JsonElement raw;

            if (input.Source is not null)
                raw = ReadSource(payload, input.Source);
            else
                raw = input.Default;

            try
            {
                var value = raw.Deserialize(type, JsonOptions);
                if (value is null)
                    throw new CapabilityContractException(
                        $"Contract input '{name}' must not deserialize to null.");

                items.Add(new ExecutionDatum(name, type, value));
            }
            catch (JsonException)
            {
                // Do not include request values in error messages.
                throw new CapabilityContractException(
                    $"Contract input '{name}' is not valid for type '{type.Name}'.");
            }
        }
        return items;
    }

    private static JsonElement ReadSource(JsonElement payload, string source)
    {
        if (source == "$") return payload;

        var element = payload;
        foreach (var part in source[2..].Split('.'))
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(part, out var property))
                throw new CapabilityContractException(
                    $"Request does not contain required contract source '{source}'.");
            element = property;
        }
        return element;
    }
}
