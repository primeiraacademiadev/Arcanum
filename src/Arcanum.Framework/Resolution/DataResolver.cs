namespace Arcanum.Framework;

public sealed class DataResolver
{
    public ExecutionDataScope CreateScope(IEnumerable<ExecutionDatum> initialData)
    {
        var data = new ExecutionDataScope();
        foreach (var item in initialData)
            data.Store(item.Name, item.DataType, item.Value);
        return data;
    }

    public object?[] Bind(
        ProcessDescriptor descriptor, ExecutionDataScope data, CancellationToken cancellationToken)
    {
        return descriptor.Parameters.Select(binding =>
            binding.IsCancellationToken
                ? (object)cancellationToken
                : data.Resolve(binding.ParameterType, binding.DataName)).ToArray();
    }

    public void Store(ExecutionDataScope data, string name, IProcessResult result)
    {
        data.Store(name, result.DataType, result.Data);
    }
}
