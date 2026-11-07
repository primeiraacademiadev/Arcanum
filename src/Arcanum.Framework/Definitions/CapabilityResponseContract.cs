namespace Arcanum.Framework;

public sealed class CapabilityResponseContract
{
    /// <summary>Name of the execution datum to return for a terminal directive.</summary>
    public string Name { get; set; } = "";

    /// <summary>Declared CLR result type for the chosen execution datum.</summary>
    public string Type { get; set; } = "";
}
