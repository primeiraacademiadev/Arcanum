namespace Arcanum.Framework;

// An edge is topology, not a Process return value or a Process attribute.
public sealed class CapabilityEdge
{
    public string From { get; set; } = "";
    public string Intention { get; set; } = "";
    public string To { get; set; } = "";
}
