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

    public object? ResolveOutput(ExecutionDataScope data, CapabilityResponseContract response)
    {
        var item = data.Read(response.Name);
        if (item.DataType.FullName != response.Type)
            throw new CapabilityDataException(
                $"Contract response '{response.Name}' does not match declared type '{response.Type}'.");
        return item.Value;
    }

    public void Store(ExecutionDataScope data, string name, IProcessResult result)
    {
        data.Store(name, result.DataType, result.Data);
    }
}
