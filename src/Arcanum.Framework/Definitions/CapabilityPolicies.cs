namespace Arcanum.Framework;

/// <summary>Behavioral policy overrides. Host configuration remains in CapabilityOptions.</summary>
public sealed class CapabilityPolicies
{
    public int? MaxSteps { get; set; }
    public int? MaxDepth { get; set; }
    /// <summary>ISO 8601 duration, e.g. PT30S; a global deadline per invocation.</summary>
    public string? Timeout { get; set; }
}
