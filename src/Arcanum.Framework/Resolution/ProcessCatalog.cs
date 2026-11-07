using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanum.Framework;

public sealed class ProcessCatalog
{
    private readonly Dictionary<string, ProcessDescriptor> _descriptors;
    private readonly Dictionary<string, Type> _dataTypes = new(StringComparer.Ordinal);

    public ProcessCatalog(IEnumerable<Type> processTypes)
    {
        _descriptors = new Dictionary<string, ProcessDescriptor>(StringComparer.Ordinal);
        foreach (var type in processTypes.Distinct())
        {
            var key = type.FullName ?? throw new CapabilityDefinitionException("Unnamed Process type.");
            var descriptor = ProcessDescriptor.Create(type);
            if (!_descriptors.TryAdd(key, descriptor))
                throw new CapabilityDefinitionException($"Duplicate Process type name '{key}'.");

            foreach (var candidate in descriptor.Parameters.Where(p => !p.IsCancellationToken)
                         .Select(p => p.ParameterType).Append(descriptor.ReturnDataType))
            {
                var typeKey = candidate.FullName
                    ?? throw new CapabilityDefinitionException("Unnamed Process data type.");
                if (_dataTypes.TryGetValue(typeKey, out var previous) && previous != candidate)
                    throw new CapabilityDefinitionException($"Ambiguous data type '{typeKey}'.");
                _dataTypes[typeKey] = candidate;
            }
        }
    }

    public ProcessDescriptor Get(string fullName) =>
        _descriptors.TryGetValue(fullName, out var value)
            ? value
            : throw new CapabilityDefinitionException($"Unknown Process '{fullName}'.");
    public Type GetDataType(string fullName) =>
        _dataTypes.TryGetValue(fullName, out var type)
            ? type
            : throw new CapabilityDefinitionException(
                $"Unknown contract data type '{fullName}' (not an Execute argument or result).");
}
