using System.Text.Json.Serialization;

namespace Arcanum.Framework;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]

// Graph-local identity. The same CLR Process can be used in several distinct nodes.
public sealed class CapabilityNode
{
    public string Process { get; set; } = "";
    public string? Template { get; set; }
    public NodeBindings Bindings { get; set; } = new();
    public string? SaveResultAs { get; set; }
}
