using System.Text.Json.Serialization;

namespace Arcanum.Framework;

public sealed class CapabilityDefinition
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public int Version { get; set; }
    public int? MaxSteps { get; set; }
    public CapabilityContract Contract { get; set; } = new();
    public Dictionary<string, ProcessMetadata> Processes { get; set; } = new(StringComparer.Ordinal);
    public CapabilityGraph Graph { get; set; } = new();
}
