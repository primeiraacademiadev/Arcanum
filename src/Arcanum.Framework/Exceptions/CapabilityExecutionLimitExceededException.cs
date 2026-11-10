using System.Text.Json.Serialization;

namespace Arcanum.Framework;

public sealed class CapabilityExecutionLimitExceededException(
    string capability, int version, string nextProcess, int limit)
    : InvalidOperationException(
        $"Capability '{capability}' version {version} reached its maximum of {limit} Process executions before '{nextProcess}'.")
{
    public string Capability { get; } = capability;
    public int Version { get; } = version;
    public string NextProcess { get; } = nextProcess;
    public int Limit { get; } = limit;
}
