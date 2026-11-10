namespace Arcanum.Framework;

/// <summary>
/// A transport-independent declaration of input objects and terminal outputs.
/// The HTTP adapter reads this declaration instead of knowing domain DTOs.
/// </summary>
public sealed class CapabilityContract
{
    public Dictionary<string, CapabilityInputContract> Inputs { get; set; } =
        new(StringComparer.Ordinal);

    // vNext: single implicit result resolved from Data Context after global completion.
    public CapabilityResponseContract? Response { get; set; }
    public Dictionary<string, CapabilityResponseContract> Responses { get; set; } =
        new(StringComparer.Ordinal);
}
