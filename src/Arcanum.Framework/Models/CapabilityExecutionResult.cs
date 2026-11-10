using System.Text.Json.Serialization;

namespace Arcanum.Framework;

public sealed record CapabilityExecutionResult(
    string Capability,
    int Version,
    string Terminal,
    string? Intention,
    int Steps,
    object? Data)
{
    public Guid ExecutionId { get; init; }

    // Internal diagnostics: never automatically expose inputs/outputs to HTTP clients.
    [JsonIgnore]
    public IReadOnlyList<ExecutionBreadcrumb> Breadcrumbs { get; init; } = [];
}
