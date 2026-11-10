namespace Arcanum.Framework;

/// <summary>Node-local mapping from method input/result names to context variable names.</summary>
public sealed class NodeBindings
{
    public Dictionary<string, string> Inputs { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Outputs { get; set; } = new(StringComparer.Ordinal);
}
