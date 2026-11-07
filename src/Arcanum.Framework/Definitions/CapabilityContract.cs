namespace Arcanum.Framework;

/// <summary>
/// A transport-independent declaration of input objects and terminal outputs.
/// The HTTP adapter reads this declaration instead of knowing domain DTOs.
/// </summary>
public sealed class CapabilityContract
{
    public Dictionary<string, CapabilityInputContract> Inputs { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, CapabilityResponseContract> Responses { get; set; } =
        new(StringComparer.Ordinal);
}
