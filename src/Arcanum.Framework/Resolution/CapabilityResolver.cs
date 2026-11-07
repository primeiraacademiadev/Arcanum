using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcanum.Framework;

public sealed class CapabilityResolver(
    CapabilityCatalog capabilities,
    ProcessResolver processes,
    DataResolver dataResolver,
    IOptions<CapabilityOptions> options,
    ILogger<CapabilityResolver> logger)
{
    private static readonly ActivitySource Traces = new("Arcanum.Framework.Capabilities");

    public async Task<CapabilityExecutionResult> ExecuteAsync(
        string name,
        IEnumerable<ExecutionDatum> initialData,
        IServiceProvider resourceScope,
        CancellationToken cancellationToken = default)
    {
        var definition = capabilities.Get(name);
        var maxSteps = definition.MaxSteps ?? options.Value.DefaultMaxSteps;
        var data = dataResolver.CreateScope(initialData);
        var processName = definition.Graph.Entry;
        var steps = 0;

        using var trace = Traces.StartActivity("capability.execute");
        trace?.SetTag("capability.name", definition.Name);
        trace?.SetTag("capability.version", definition.Version);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (steps >= maxSteps)
                throw new CapabilityExecutionLimitExceededException(
                    name, definition.Version, processName, maxSteps);
            steps++;

            var descriptor = processes.GetDescriptor(processName);
            var process = processes.Resolve(descriptor, resourceScope);
            var args = dataResolver.Bind(descriptor, data, cancellationToken);

            using var stepTrace = Traces.StartActivity("capability.process");
            stepTrace?.SetTag("process.type", processName);
            stepTrace?.SetTag("process.step", steps);

            var result = await processes.InvokeAsync(descriptor, process, args);
            if (string.IsNullOrWhiteSpace(result.Intention))
                throw new CapabilityTransitionException(
                    $"Process '{processName}' returned an empty intention.");

            var metadata = definition.Processes[processName];
            dataResolver.Store(data, metadata.SaveResultAs, result);

            if (!definition.Graph.Transitions[processName].TryGetValue(result.Intention, out var next))
                throw new CapabilityTransitionException(
                    $"Process '{processName}' returned unmapped intention '{result.Intention}'.");

            stepTrace?.SetTag("process.intention", result.Intention);
            stepTrace?.SetTag("process.next", next);
            logger.LogInformation(
                "Capability {Capability} step {Step} Process {Process} intention {Intention} next {Next}",
                name, steps, processName, result.Intention, next);

            if (next.StartsWith('@'))
                return new CapabilityExecutionResult(
                    name, definition.Version, next, result.Intention, steps, result.Data);

            processName = next;
        }
    }
}
