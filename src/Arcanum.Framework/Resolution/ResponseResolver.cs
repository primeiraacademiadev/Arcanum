namespace Arcanum.Framework;

/// <summary>Resolves a transport-neutral response from the declared Data Context contract.</summary>
public sealed class ResponseResolver(DataResolver dataResolver)
{
    public object? Resolve(
        CapabilityDefinition definition,
        ExecutionDataScope data,
        string terminal,
        object? fallback = null)
    {
        var response = definition.Templates.Count > 0
            ? definition.Contract.Response
            : definition.Contract.Responses.GetValueOrDefault(terminal);
        if (response is null)
        {
            if (definition.Templates.Count > 0)
                throw new CapabilityContractException(
                    $"Capability '{definition.Name}' has no implicit response contract.");
            return fallback;
        }

        return dataResolver.ResolveOutput(data, response);
    }
}
