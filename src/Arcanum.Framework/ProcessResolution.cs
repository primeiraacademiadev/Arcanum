using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanum.Framework;

public sealed record ArgumentBinding(Type ParameterType, string? DataName, bool IsCancellationToken);

public sealed class ProcessDescriptor
{
    private readonly Func<object, object?[], ValueTask<IProcessResult>> _invoke;

    private ProcessDescriptor(
        Type processType,
        ArgumentBinding[] parameters,
        Func<object, object?[], ValueTask<IProcessResult>> invoke)
    {
        ProcessType = processType;
        Parameters = parameters;
        _invoke = invoke;
    }

    public Type ProcessType { get; }
    public IReadOnlyList<ArgumentBinding> Parameters { get; }
    public ValueTask<IProcessResult> InvokeAsync(object instance, object?[] arguments) =>
        _invoke(instance, arguments);

    public static ProcessDescriptor Create(Type type)
    {
        if (!type.IsClass || type.IsAbstract || !typeof(IProcess).IsAssignableFrom(type))
            throw new CapabilityDefinitionException($"'{type.FullName}' is not a concrete IProcess.");

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name is "Execute" or "ExecuteAsync")
            .ToArray();

        if (methods.Length != 1 || methods[0].IsGenericMethod)
            throw new CapabilityDefinitionException(
                $"Process '{type.FullName}' must have exactly one non-generic Execute or ExecuteAsync method.");

        var method = methods[0];
        var parameters = method.GetParameters().Select(p =>
        {
            if (p.ParameterType.IsByRef || p.IsOut || p.IsOptional)
                throw new CapabilityDefinitionException($"Unsupported argument '{p.Name}' in '{type.FullName}'.");

            var key = p.GetCustomAttribute<FromCapabilityDataAttribute>()?.Name;
            if (key is not null && string.IsNullOrWhiteSpace(key))
                throw new CapabilityDefinitionException($"Empty data key in '{type.FullName}'.");

            if (p.ParameterType == typeof(CancellationToken) && key is not null)
                throw new CapabilityDefinitionException("CancellationToken must be supplied by the runtime.");

            return new ArgumentBinding(
                p.ParameterType,
                key,
                p.ParameterType == typeof(CancellationToken));
        }).ToArray();

        var returnType = method.ReturnType;
        var mode = method.Name == "Execute" ? "sync" : "";
        Type resultType;

        if (mode == "sync")
        {
            if (!returnType.IsGenericType ||
                returnType.GetGenericTypeDefinition() != typeof(ProcessResult<>))
                throw new CapabilityDefinitionException($"Execute on '{type.FullName}' must return ProcessResult<T>.");
            resultType = returnType.GenericTypeArguments[0];
        }
        else
        {
            if (!returnType.IsGenericType ||
                (returnType.GetGenericTypeDefinition() != typeof(Task<>) &&
                 returnType.GetGenericTypeDefinition() != typeof(ValueTask<>)))
                throw new CapabilityDefinitionException(
                    $"ExecuteAsync on '{type.FullName}' must return Task<ProcessResult<T>> or ValueTask<ProcessResult<T>>.");

            mode = returnType.GetGenericTypeDefinition() == typeof(Task<>) ? "task" : "valuetask";
            var inner = returnType.GenericTypeArguments[0];
            if (!inner.IsGenericType || inner.GetGenericTypeDefinition() != typeof(ProcessResult<>))
                throw new CapabilityDefinitionException($"Invalid result type on '{type.FullName}'.");
            resultType = inner.GenericTypeArguments[0];
        }

        // Compile the method call ONCE at host startup, not MethodInfo.Invoke on every request.
        var target = Expression.Parameter(typeof(object), "process");
        var args = Expression.Parameter(typeof(object[]), "args");
        var expressions = method.GetParameters()
            .Select((p, i) => (Expression)Expression.Convert(
                Expression.ArrayIndex(args, Expression.Constant(i)), p.ParameterType))
            .ToArray();

        var call = Expression.Call(Expression.Convert(target, type), method, expressions);
        var boxed = Expression.Convert(call, typeof(object));
        var raw = Expression.Lambda<Func<object, object?[], object>>(boxed, target, args).Compile();

        var adapter = typeof(ProcessDescriptor)
            .GetMethod(nameof(Adapt), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(resultType)
            .Invoke(null, new object[] { raw, mode });

        return new ProcessDescriptor(
            type,
            parameters,
            (Func<object, object?[], ValueTask<IProcessResult>>)adapter!);
    }

    private static Func<object, object?[], ValueTask<IProcessResult>> Adapt<T>(
        Func<object, object?[], object> call, string mode) => mode switch
    {
        "sync" => (instance, args) => new ValueTask<IProcessResult>(
            (ProcessResult<T>)call(instance, args)),
        "task" => async (instance, args) =>
            (IProcessResult)await (Task<ProcessResult<T>>)call(instance, args),
        "valuetask" => async (instance, args) =>
            (IProcessResult)await (ValueTask<ProcessResult<T>>)call(instance, args),
        _ => throw new CapabilityDefinitionException($"Unknown Process method mode '{mode}'.")
    };
}

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

public sealed class ProcessResolver(ProcessCatalog catalog)
{
    public ProcessDescriptor GetDescriptor(string fullName) => catalog.Get(fullName);

    public IProcess Resolve(ProcessDescriptor descriptor, IServiceProvider resourceScope) =>
        (IProcess)resourceScope.GetRequiredService(descriptor.ProcessType);

    public ValueTask<IProcessResult> InvokeAsync(
        ProcessDescriptor descriptor, IProcess process, object?[] arguments) =>
        descriptor.InvokeAsync(process, arguments);
}
