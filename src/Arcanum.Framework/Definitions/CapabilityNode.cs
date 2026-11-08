namespace Arcanum.Framework;

// Graph-local identity. The same CLR Process can be used in several distinct nodes.
public sealed class CapabilityNode
{
    public string Process { get; set; } = "";
    public string? SaveResultAs { get; set; }
}
