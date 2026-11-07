using System.Text.Json.Serialization;

namespace Arcanum.Framework;

public sealed class CapabilityOptions
{
    public int DefaultMaxSteps { get; set; } = 100;
}
