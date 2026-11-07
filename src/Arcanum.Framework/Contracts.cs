using System.Text.Json.Serialization;

namespace Arcanum.Framework;

/// <summary>Marker for executable processes discoverable only in explicitly registered assemblies.</summary>
public interface IProcess { }

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromCapabilityDataAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

public interface IProcessResult
{
    object? Data { get; }
    Type DataType { get; }
    string Intention { get; }
}

public sealed record ProcessResult<T>(T? Data, string Intention) : IProcessResult
{
    object? IProcessResult.Data => Data;
    Type IProcessResult.DataType => typeof(T);
}

public sealed record ExecutionDatum(string Name, Type DataType, object? Value)
{
    public static ExecutionDatum Of<T>(string name, T value) => new(name, typeof(T), value);
}

public sealed class CapabilityDefinition
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public int Version { get; set; }
    public int? MaxSteps { get; set; }
    public Dictionary<string, ProcessMetadata> Processes { get; set; } = new(StringComparer.Ordinal);
    public CapabilityGraph Graph { get; set; } = new();
}

public sealed class ProcessMetadata
{
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string SaveResultAs { get; set; } = "";
}

public sealed class CapabilityGraph
{
    public string Entry { get; set; } = "";
    public Dictionary<string, Dictionary<string, string>> Transitions { get; set; } = new(StringComparer.Ordinal);
}

public sealed class CapabilityOptions
{
    public int DefaultMaxSteps { get; set; } = 100;
}

public sealed record CapabilityExecutionResult(
    string Capability,
    int Version,
    string Terminal,
    string Intention,
    int Steps,
    object? Data);

public class CapabilityDefinitionException(string message) : InvalidOperationException(message) { }

public class CapabilityDataException(string message) : InvalidOperationException(message) { }

public class CapabilityTransitionException(string message) : InvalidOperationException(message) { }

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
