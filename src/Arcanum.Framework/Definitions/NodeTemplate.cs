namespace Arcanum.Framework;

/// <summary>Reusable Process contract. CLR method signatures are the technical authority.</summary>
public sealed class NodeTemplate
{
    public string Process { get; set; } = "";
    public string? Description { get; set; }
    public Dictionary<string, string> Resources { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Inputs { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Outputs { get; set; } = new(StringComparer.Ordinal);
}
