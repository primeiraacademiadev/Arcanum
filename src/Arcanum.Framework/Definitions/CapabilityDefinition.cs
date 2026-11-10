using System.Text.Json.Serialization;

namespace Arcanum.Framework;

public sealed class CapabilityDefinition
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public int Version { get; set; }
    public int? MaxSteps { get; set; }
    public int? MaxDepth { get; set; }
    public CapabilityContract Contract { get; set; } = new();
    // vNext: declared contract aliases and reusable Process templates.
    public Dictionary<string, CapabilityModel> Models { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, NodeTemplate> Templates { get; set; } = new(StringComparer.Ordinal);
    public CapabilityPolicies Policies { get; set; } = new();

    // Legacy PoC metadata remains readable during the migration.
    public Dictionary<string, ProcessMetadata> Processes { get; set; } = new(StringComparer.Ordinal);
    public CapabilityGraph Graph { get; set; } = new();
}
