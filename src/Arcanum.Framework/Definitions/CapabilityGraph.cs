using System.Text.Json.Serialization;

namespace Arcanum.Framework;

public sealed class CapabilityGraph
{
    public string Entry { get; set; } = "";
    public Dictionary<string, Dictionary<string, string>> Transitions { get; set; } = new(StringComparer.Ordinal);
}
