using System.Text.Json;

namespace Arcanum.Framework;

/// <summary>Transport-neutral dispatcher for JSON-hosted Capability contracts.</summary>
public sealed class CapabilityJsonDispatcher(
    CapabilityCatalog catalog,
    CapabilityContractResolver binder,
    CapabilityResolver resolver)
{
    public Task<CapabilityExecutionResult> ExecuteAsync(
        string capability,
        JsonElement payload,
        IServiceProvider resourceScope,
        CancellationToken cancellationToken = default)
    {
        var definition = catalog.Get(capability);
        var inputs = binder.BindInput(definition, payload);
        return resolver.ExecuteAsync(capability, inputs, resourceScope, cancellationToken);
    }
}
