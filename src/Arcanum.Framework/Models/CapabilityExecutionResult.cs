using System.Text.Json.Serialization;

namespace Arcanum.Framework;

public sealed record CapabilityExecutionResult(
    string Capability,
    int Version,
    string Terminal,
    string Intention,
    int Steps,
    object? Data);
