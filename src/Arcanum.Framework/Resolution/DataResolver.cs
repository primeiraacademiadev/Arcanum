namespace Arcanum.Framework;

public sealed class DataResolver
{
    public ExecutionDataScope CreateScope(IEnumerable<ExecutionDatum> initialData, CancellationToken cancellationToken = default)
    {
        var data = new ExecutionDataScope(cancellationToken);
        foreach (var item in initialData)
            data.Store(item.Name, item.DataType, item.Value);
        return data;
    }

    public object?[] Bind(
        ProcessDescriptor descriptor, ExecutionDataScope data)
    {
        return descriptor.Parameters.Select(binding =>
            binding.IsCancellationToken
                ? (object)data.CancellationToken
                : data.Resolve(binding.ParameterType, binding.DataName, binding.ParameterName)).ToArray();
    }

    public object?[] Bind(
        ProcessDescriptor descriptor,
        ExecutionDataScope data,
        IReadOnlyDictionary<string, string> bindings)
    {
        return descriptor.Parameters.Select(parameter =>
            parameter.IsCancellationToken
                ? (object)data.CancellationToken
                : data.Resolve(parameter.ParameterType,
                    bindings.TryGetValue(parameter.ParameterName, out var name)
                        ? name
                        : throw new CapabilityDefinitionException(
                            $"No binding for Process parameter '{parameter.ParameterName}'.")))
            .ToArray();
    }

    public object? ResolveOutput(ExecutionDataScope data, CapabilityResponseContract response)
    {
        var item = data.Read(response.Name);
        var expectedType = Type.GetType(response.Type, throwOnError: false)
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(response.Type, throwOnError: false))
                .FirstOrDefault(type => type is not null);
        if (expectedType is null || !expectedType.IsAssignableFrom(item.DataType))
            throw new CapabilityDataException(
                $"Contract response '{response.Name}' does not match declared type '{response.Type}'.");
        return item.Value;
    }

    public void Store(
        ExecutionDataScope data, string name, IProcessResult result, Type? declaredType = null)
    {
        if (declaredType is not null && !declaredType.IsAssignableFrom(result.DataType))
            throw new CapabilityDataException(
                $"Result type '{result.DataType.FullName}' is incompatible with '{declaredType.FullName}'.");
        data.Store(name, declaredType ?? result.DataType, result.Data);
    }
}
