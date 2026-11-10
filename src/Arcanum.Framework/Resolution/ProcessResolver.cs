using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanum.Framework;

public sealed class ProcessResolver(ProcessCatalog catalog)
{
    public ProcessDescriptor GetDescriptor(string fullName) => catalog.Get(fullName);
    public Type GetDataType(string fullName) => catalog.GetDataType(fullName);

    public object Resolve(ProcessDescriptor descriptor, IServiceProvider resourceScope) =>
        resourceScope.GetRequiredService(descriptor.ProcessType);

    public ValueTask<IProcessResult> InvokeAsync(
        ProcessDescriptor descriptor, object process, object?[] arguments) =>
        descriptor.InvokeAsync(process, arguments);
}
