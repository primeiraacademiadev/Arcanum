using System.Text.Json.Serialization;

namespace Arcanum.Framework;

// All edges use the same declaration. @spawn denotes an unconditional,
// graph-directed parallel successor and is never returned by a Process.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CapabilityEdge
{
    public string From { get; set; } = "";
    public string Intention { get; set; } = "";
    public string To { get; set; } = "";
}
