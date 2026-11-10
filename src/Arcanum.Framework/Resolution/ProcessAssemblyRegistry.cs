using System.Reflection;

namespace Arcanum.Framework;

/// <summary>
/// Resolves CLR types strictly by full name within explicitly authorized assemblies.
/// Only names referenced by Capability JSON are activated as Processes.
/// </summary>
public sealed class ProcessAssemblyRegistry
{
    private readonly Assembly[] _assemblies;

    public ProcessAssemblyRegistry(IEnumerable<Assembly> assemblies)
    {
        _assemblies = assemblies.Distinct().ToArray();
        if (_assemblies.Length == 0)
            throw new ArgumentException("At least one trusted Process assembly is required.", nameof(assemblies));
    }

    public Type Resolve(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new CapabilityDefinitionException("Process class name cannot be blank.");

        var matches = _assemblies
            .Select(assembly => assembly.GetType(fullName, throwOnError: false, ignoreCase: false))
            .OfType<Type>()
            .Distinct()
            .ToArray();

        if (matches.Length != 1)
            throw new CapabilityDefinitionException(
                $"Process '{fullName}' must resolve to exactly one type in trusted assemblies.");

        return matches[0];
    }
}
