using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanum.Framework;

public sealed class ProcessCatalog
{
    private readonly Dictionary<string, ProcessDescriptor> _descriptors;

    public ProcessCatalog(IEnumerable<Type> processTypes)
    {
        _descriptors = new Dictionary<string, ProcessDescriptor>(StringComparer.Ordinal);
        foreach (var type in processTypes.Distinct())
        {
            var key = type.FullName ?? throw new CapabilityDefinitionException("Unnamed Process type.");
            if (!_descriptors.TryAdd(key, ProcessDescriptor.Create(type)))
                throw new CapabilityDefinitionException($"Duplicate Process type name '{key}'.");
        }
    }

    public ProcessDescriptor Get(string fullName) =>
        _descriptors.TryGetValue(fullName, out var value)
            ? value
            : throw new CapabilityDefinitionException($"Unknown Process '{fullName}'.");
}
