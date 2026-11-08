using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcanum.Framework;

// Process results select intentions. Edges define topology; the resolver schedules every
// matching destination and dynamically synchronizes nodes with several incoming edges.
public sealed class CapabilityResolver(
    CapabilityCatalog capabilities,
    ProcessResolver processes,
    DataResolver dataResolver,
    IOptions<CapabilityOptions> options,
    ILogger<CapabilityResolver> logger)
{
    private static readonly ActivitySource Traces = new("Arcanum.Framework.Capabilities");

    private sealed record RunningNode(string Node, Guid InvocationId, Task<IProcessResult> Work);
    private sealed record TerminalArrival(string Terminal, string Intention, object? Data);

    public async Task<CapabilityExecutionResult> ExecuteAsync(
        string name,
        IEnumerable<ExecutionDatum> initialData,
        IServiceProvider resourceScope,
        CancellationToken cancellationToken = default)
    {
        var definition = capabilities.Get(name);
        var maxSteps = definition.MaxSteps ?? options.Value.DefaultMaxSteps;
        var data = dataResolver.CreateScope(initialData);
        var executionId = Guid.NewGuid();
        var trail = new ExecutionTrail(options.Value, logger.IsEnabled(LogLevel.Trace));
        var pending = new Dictionary<string, List<string?>>(StringComparer.Ordinal);
        var running = new List<RunningNode>();
        var terminals = new List<TerminalArrival>();
        var waiting = new HashSet<string>(StringComparer.Ordinal);
        var steps = 0;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var trace = Traces.StartActivity("capability.execute");
        trace?.SetTag("capability.name", definition.Name);
        trace?.SetTag("capability.version", definition.Version);
        trace?.SetTag("capability.execution_id", executionId);
        trail.Add("CapabilityStarted", definition.Graph.Entry);
        Enqueue(definition.Graph.Entry, null);

        try
        {
            while (running.Count > 0 || pending.Count > 0)
            {
                linked.Token.ThrowIfCancellationRequested();

                // All incoming arrivals for a node are combined into ONE invocation.
                // Running processes and pending nodes are both potential future arrivals.
                var ready = pending.Keys
                    .Where(node => definition.Graph.IncomingCount(node) <= 1 ||
                        (!running.Any(active => definition.Graph.CanReach(active.Node, node)) &&
                         !pending.Keys.Any(other => other != node && definition.Graph.CanReach(other, node))))
                    .ToArray();

                if (steps + ready.Length > maxSteps)
                {
                    var blockedNode = ready.FirstOrDefault() ?? pending.Keys.FirstOrDefault()
                        ?? running.FirstOrDefault()?.Node ?? definition.Graph.Entry;
                    throw new CapabilityExecutionLimitExceededException(
                        name, definition.Version, blockedNode, maxSteps);
                }

                foreach (var node in ready)
                {
                    var arrivals = pending[node];
                    pending.Remove(node);
                    if (waiting.Remove(node))
                        trail.Add("Ready", node, detail: $"Joined {arrivals.Count} arrivals.");
                    var descriptor = processes.GetDescriptor(node);
                    var args = dataResolver.Bind(descriptor, data, linked.Token);
                    var invocationId = Guid.NewGuid();
                    trail.Add("Scheduled", node, invocationId, descriptor.MethodName);
                    var work = Task.Run(
                        () => InvokeNodeAsync(node, invocationId, descriptor, args, resourceScope,
                            trail, linked.Token), linked.Token);
                    running.Add(new RunningNode(node, invocationId, work));
                    steps++;
                }

                foreach (var node in pending.Keys)
                    if (waiting.Add(node))
                        trail.Add("Waiting", node, detail: "An active or queued execution can still reach this node.");

                if (running.Count == 0)
                    throw new CapabilityTransitionException(
                        "Graph synchronization has no runnable Process; check cyclic wait dependencies.");

                var completedTask = await Task.WhenAny(running.Select(item => item.Work));
                var completed = running.Single(item => ReferenceEquals(item.Work, completedTask));
                running.Remove(completed);
                var result = await completedTask;
                if (string.IsNullOrWhiteSpace(result.Intention))
                    throw new CapabilityTransitionException(
                        $"Process '{completed.Node}' returned an empty intention.");

                var descriptorOfResult = processes.GetDescriptor(completed.Node);
                var metadata = definition.Processes[completed.Node];
                dataResolver.Store(data, metadata.SaveResultAs ?? descriptorOfResult.ReturnName!, result);

                var targets = definition.Graph.Matching(completed.Node, result.Intention);
                if (targets.Length == 0)
                    throw new CapabilityTransitionException(
                        $"Process '{completed.Node}' returned unmapped intention '{result.Intention}'.");

                // Publish ALL selected successors before reconsidering any waiting join.
                foreach (var edge in targets)
                {
                    trail.Add("Transition", completed.Node, completed.InvocationId,
                        intention: result.Intention, relatedNodeId: edge.To);
                    if (edge.To.StartsWith('@'))
                        terminals.Add(new TerminalArrival(edge.To, result.Intention, result.Data));
                    else
                        Enqueue(edge.To, completed.Node);
                }
            }

            if (terminals.Count == 0)
                throw new CapabilityTransitionException("Capability completed without a terminal edge.");

            var first = terminals[0];
            if (terminals.Any(t => t.Terminal != first.Terminal || t.Intention != first.Intention))
                throw new CapabilityTransitionException(
                    "Concurrent branches produced conflicting terminal results.");

            var output = definition.Contract.Responses.TryGetValue(first.Terminal, out var response)
                ? dataResolver.ResolveOutput(data, response)
                : first.Data;
            trail.Add("CapabilityCompleted", intention: first.Intention, relatedNodeId: first.Terminal);
            return new CapabilityExecutionResult(
                name, definition.Version, first.Terminal, first.Intention, steps, output)
            {
                ExecutionId = executionId,
                Breadcrumbs = trail.Snapshot()
            };
        }
        catch (Exception exception)
        {
            linked.Cancel();
            try
            {
                await Task.WhenAll(running.Select(item => item.Work));
            }
            catch
            {
                // Preserve the initiating failure; all scheduled work is observed.
            }
            trail.Add("CapabilityFailed", detail: exception.GetType().Name);
            logger.LogError(exception, "Capability {Capability} execution {ExecutionId} failed",
                name, executionId);
            throw;
        }

        void Enqueue(string node, string? source)
        {
            if (!pending.TryGetValue(node, out var arrivals))
                pending[node] = arrivals = [];
            arrivals.Add(source);
            trail.Add("Arrival", node, relatedNodeId: source);
        }
    }

    private async Task<IProcessResult> InvokeNodeAsync(
        string node,
        Guid invocationId,
        ProcessDescriptor descriptor,
        object?[] args,
        IServiceProvider resourceScope,
        ExecutionTrail trail,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var process = processes.Resolve(descriptor, resourceScope);
        var inputs = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < descriptor.Parameters.Count; i++)
            if (!descriptor.Parameters[i].IsCancellationToken)
                inputs.Add(descriptor.Parameters[i].ParameterName, args[i]);

        // Started is recorded in the worker immediately before the CLR method call.
        trail.Add("Started", node, invocationId, descriptor.MethodName,
            snapshot: inputs, captureSnapshot: true);
        var clock = Stopwatch.StartNew();
        using var activity = Traces.StartActivity("capability.process");
        activity?.SetTag("process.type", node);
        activity?.SetTag("process.invocation_id", invocationId);
        try
        {
            var result = await processes.InvokeAsync(descriptor, process, args);
            trail.Add("Completed", node, invocationId, descriptor.MethodName,
                intention: result.Intention, durationMs: clock.Elapsed.TotalMilliseconds,
                snapshot: result.Data, captureSnapshot: true);
            logger.LogInformation(
                "Capability Process {Process} invocation {InvocationId} returned {Intention}",
                node, invocationId, result.Intention);
            return result;
        }
        catch (OperationCanceledException)
        {
            trail.Add("Cancelled", node, invocationId, descriptor.MethodName,
                durationMs: clock.Elapsed.TotalMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            trail.Add("Failed", node, invocationId, descriptor.MethodName,
                durationMs: clock.Elapsed.TotalMilliseconds, detail: exception.GetType().Name);
            throw;
        }
    }
}
