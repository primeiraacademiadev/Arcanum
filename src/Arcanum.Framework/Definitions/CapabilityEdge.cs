namespace Arcanum.Framework;

// Conditional edges select one continuation by intention. Parallel edges do not
// inspect the result intention and may be activated together with the continuation.
public sealed class CapabilityEdge
{
    public string From { get; set; } = "";
    public string? Intention { get; set; }
    public string To { get; set; } = "";
    public bool Parallel { get; set; }
}
