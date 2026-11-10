using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcanum.Framework;

// Graph-local NodeIds identify execution sites; NodeExecutionIds identify actual
// invocations. The scheduler is the sole writer of the shared execution data scope.
public sealed class CapabilityResolver(
    CapabilityCatalog capabilities,
    ProcessResolver processes,
    DataResolver dataResolver,
    IOptions<CapabilityOptions> options,
    ILogger<CapabilityResolver> logger,
    ResponseResolver? responseResolver = null)
{
    private static readonly ActivitySource Traces = new("Arcanum.Framework.Capabilities");

    private sealed record Arrival(string? SourceNodeId, Guid? SourceInvocationId, int Depth);
    private sealed record RunningNode(string NodeId, Guid InvocationId, int Depth, Task<IProcessResult> Work);
    private sealed record TerminalArrival(string Terminal, string Intention, object? Data);

    public async Task<CapabilityExecutionResult> ExecuteAsync(
        string name,
        IEnumerable<ExecutionDatum> initialData,
        IServiceProvider resourceScope,
        CancellationToken cancellationToken = default)
    {
        // Reference identity ensures that one thrown exception is logged once,
        // including when it surfaces through the worker and the scheduler.
        var logged = new ConcurrentDictionary<Exception, byte>(ReferenceEqualityComparer.Instance);
        void LogException(Exception exception, string? nodeId, Guid? invocationId)
        {
            if (logged.TryAdd(exception, 0))
                logger.LogError(exception,
                    "Capability {Capability} node {NodeId} invocation {InvocationId} raised {ExceptionType}",
                    name, nodeId, invocationId, exception.GetType().Name);
        }

        try
        {
            return await ExecuteCoreAsync(name, initialData, resourceScope,
                cancellationToken, LogException);
        }
        catch (Exception exception)
        {
            // Also covers failures before the execution context can be created.
            LogException(exception, null, null);
            throw;
        }
    }

    private async Task<CapabilityExecutionResult> ExecuteCoreAsync(
        string name,
        IEnumerable<ExecutionDatum> initialData,
        IServiceProvider resourceScope,
        CancellationToken cancellationToken,
        Action<Exception, string?, Guid?> logException)
    {
        var definition = capabilities.Get(name);
        var maxSteps = definition.Policies?.MaxSteps ?? definition.MaxSteps ?? options.Value.DefaultMaxSteps;
        var maxDepth = definition.Policies?.MaxDepth ?? definition.MaxDepth ?? options.Value.DefaultMaxDepth;
        var timeout = definition.Policies?.Timeout is { } timeoutIso
            ? System.Xml.XmlConvert.ToTimeSpan(timeoutIso)
            : options.Value.DefaultTimeout;
        using var deadline = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        // CancellationToken is held in runtime context, not in business data entries.
        var data = dataResolver.CreateScope(initialData, linked.Token);
        var executionId = Guid.NewGuid();
        var trail = new ExecutionTrail(options.Value, logger.IsEnabled(LogLevel.Trace), logger);
        var pending = new Dictionary<string, List<Arrival>>(StringComparer.Ordinal);
        var running = new List<RunningNode>();
        var terminals = new List<TerminalArrival>();
        var leafIntentions = new List<string>();
        var waiting = new HashSet<string>(StringComparer.Ordinal);
        var steps = 0;
        Exception? firstProcessFailure = null;

        using var trace = Traces.StartActivity("capability.execute");
        trace?.SetTag("capability.name", definition.Name);
        trace?.SetTag("capability.version", definition.Version);
        trace?.SetTag("capability.execution_id", executionId);
        trail.Add("CapabilityStarted", definition.Graph.Entry);
        Enqueue(definition.Graph.Entry, null, null, 1);

        try
        {
            while (running.Count > 0 || pending.Count > 0)
            {
                linked.Token.ThrowIfCancellationRequested();

                // No special Fork/Join nodes. A pending arrival may proceed once
                // no OTHER running or pending execution can still reach this node.
                var ready = pending.Keys
                    .Where(node => definition.Graph.IncomingCount(node) <= 1 ||
                        (!running.Any(active => definition.Graph.CanReach(active.NodeId, node)) &&
                         !pending.Keys.Any(other => other != node && definition.Graph.CanReach(other, node))))
                    .ToArray();

                if (steps + ready.Length > maxSteps)
                {
                    var blocked = ready.FirstOrDefault() ?? pending.Keys.FirstOrDefault()
                        ?? running.FirstOrDefault()?.NodeId ?? definition.Graph.Entry;
                    throw new CapabilityExecutionLimitExceededException(
                        name, definition.Version, blocked, maxSteps);
                }

                foreach (var nodeId in ready)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    var arrivals = pending[nodeId];
                    var depth = arrivals.Max(arrival => arrival.Depth);
                    if (depth > maxDepth)
                        throw new CapabilityDepthLimitExceededException(
                            name, definition.Version, nodeId, maxDepth);

                    pending.Remove(nodeId);
                    var node = definition.Graph.Nodes[nodeId];
                    var descriptor = processes.GetDescriptor(node.Process);
                    var args = node.Template is not null
                        ? dataResolver.Bind(descriptor, data, node.Bindings.Inputs)
                        : dataResolver.Bind(descriptor, data);
                    var invocationId = Guid.NewGuid();

                    if (waiting.Remove(nodeId))
                        trail.Add("Ready", nodeId, invocationId, detail: $"Joined {arrivals.Count} arrivals.");
                    trail.Add("Scheduled", nodeId, invocationId, descriptor.MethodName,
                        detail: $"Process={node.Process}; depth={depth}");
                    foreach (var arrival in arrivals)
                        if (arrival.SourceInvocationId is not null)
                            trail.Add("ArrivalConsumed", nodeId, invocationId,
                                relatedNodeId: arrival.SourceNodeId,
                                causedByNodeExecutionId: arrival.SourceInvocationId);
                    var work = Task.Run(
                        () => InvokeNodeAsync(nodeId, node.Process, invocationId, descriptor,
                            args, resourceScope, trail, linked, logException,
                            exception =>
                            {
                                if (exception is not OperationCanceledException)
                                    Interlocked.CompareExchange(ref firstProcessFailure, exception, null);
                            }));
                    running.Add(new RunningNode(nodeId, invocationId, depth, work));
                    steps++;
                }

                foreach (var node in pending.Keys)
                    if (waiting.Add(node))
                        trail.Add("Waiting", node,
                            detail: "Another running or queued execution can still reach this node.");

                if (running.Count == 0)
                    throw new CapabilityTransitionException(
                        "Graph synchronization has no runnable Process; check cyclic wait dependencies.");

                var completedTask = await Task.WhenAny(running.Select(item => item.Work));
                var completed = running.Single(item => ReferenceEquals(item.Work, completedTask));
                running.Remove(completed);
                var result = await completedTask;
                linked.Token.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(result.Intention) || result.Intention.StartsWith('@'))
                    throw new CapabilityTransitionException(
                        $"Node '{completed.NodeId}' returned an empty or reserved system intention.");

                var nodeConfig = definition.Graph.Nodes[completed.NodeId];
                var descriptorOfResult = processes.GetDescriptor(nodeConfig.Process);
                var metadata = definition.Processes[nodeConfig.Process];
                dataResolver.Store(data, nodeConfig.SaveResultAs ??
                    metadata.SaveResultAs ?? descriptorOfResult.ReturnName!, result);

                var conditionalEdges = definition.Graph.Edges
                    .Where(edge => edge.From == completed.NodeId &&
                        edge.Intention != CapabilityGraph.SpawnIntention).ToArray();
                if (conditionalEdges.Length > 0 &&
                    !conditionalEdges.Any(edge => edge.Intention == result.Intention))
                    throw new CapabilityTransitionException(
                        $"Node '{completed.NodeId}' returned unmapped intention '{result.Intention}'.");
                var targets = definition.Graph.Matching(completed.NodeId, result.Intention);
                if (conditionalEdges.Length == 0)
                    leafIntentions.Add(result.Intention);

                // Publish ALL selected successors before re-evaluating pending joins.
                // @spawn is a system intention evaluated by the scheduler, not the Process.
                foreach (var edge in targets)
                {
                    trail.Add("Transition", completed.NodeId, completed.InvocationId,
                        intention: edge.Intention, relatedNodeId: edge.To,
                        detail: edge.Intention == CapabilityGraph.SpawnIntention ? "spawn" : "conditional");
                    if (edge.To.StartsWith('@'))
                        terminals.Add(new TerminalArrival(edge.To, result.Intention, result.Data));
                    else
                        Enqueue(edge.To, completed.NodeId, completed.InvocationId, completed.Depth + 1);
                }
            }

            linked.Token.ThrowIfCancellationRequested();
            if (definition.Templates.Count > 0)
            {
                if (terminals.Count != 0)
                    throw new CapabilityTransitionException(
                        "Explicit terminal responses are not supported in Capability JSON vNext yet.");
                var leafOutcomes = leafIntentions.Distinct(StringComparer.Ordinal).ToArray();
                var implicitIntention = leafOutcomes.Length == 1 ? leafOutcomes[0] : null;
                var implicitResponse = (responseResolver ?? new ResponseResolver(dataResolver))
                    .Resolve(definition, data, "@complete");
                trail.Add("CapabilityCompleted", intention: implicitIntention,
                    relatedNodeId: "@complete", detail: "Implicit response from Data Context");
                return new CapabilityExecutionResult(
                    name, definition.Version, "@complete", implicitIntention, steps, implicitResponse)
                {
                    ExecutionId = executionId,
                    Breadcrumbs = trail.Snapshot()
                };
            }

            if (terminals.Count == 0)
                throw new CapabilityTransitionException("Capability completed without a terminal edge.");

            var first = terminals[0];
            if (terminals.Any(t => t.Terminal != first.Terminal))
                throw new CapabilityTransitionException(
                    "Concurrent branches produced conflicting terminal directives.");

            var terminalIntentions = terminals.Select(t => t.Intention)
                .Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray();
            // A Capability has no single terminal intention when independent
            // branches completed with different intentions. The trail retains both.
            var finalIntention = terminalIntentions.Length == 1 ? terminalIntentions[0] : null;
            var output = (responseResolver ?? new ResponseResolver(dataResolver))
                .Resolve(definition, data, first.Terminal, first.Data);
            trail.Add("CapabilityCompleted", intention: finalIntention, relatedNodeId: first.Terminal,
                detail: terminalIntentions.Length > 1
                    ? $"Independent terminal intentions: {string.Join(", ", terminalIntentions)}"
                    : null);
            return new CapabilityExecutionResult(
                name, definition.Version, first.Terminal, finalIntention, steps, output)
            {
                ExecutionId = executionId,
                Breadcrumbs = trail.Snapshot()
            };
        }
        catch (Exception exception)
        {
            // Immediately stop all further graph propagation and signal every
            // Process. Already-started tasks must settle before we can return.
            linked.Cancel();
            logException(exception, null, null);
            try
            {
                await Task.WhenAll(running.Select(item => item.Work));
            }
            catch (Exception remaining)
            {
                // Workers log each exception themselves (including siblings).
                // If Task.WhenAll raises a distinct error, never discard it.
                logException(remaining, null, null);
            }

            if (deadline.IsCancellationRequested)
                trail.Add("Timeout", detail: $"Global deadline {timeout} expired.");

            // A Process fault may have cancelled the scheduler before its own
            // Task was observed. Preserve the real fault instead of replacing
            // it with the resulting OperationCanceledException.
            var originalFailure = Volatile.Read(ref firstProcessFailure);
            var primary = exception is OperationCanceledException && originalFailure is not null
                ? originalFailure : exception;
            trail.Add(primary is OperationCanceledException ? "CapabilityCancelled" : "CapabilityFailed",
                detail: primary.GetType().Name);
            primary.Data["Arcanum.ExecutionId"] = executionId;
            primary.Data["Arcanum.Breadcrumbs"] = trail.Snapshot();
            if (!ReferenceEquals(primary, exception))
                ExceptionDispatchInfo.Capture(primary).Throw();
            throw;
        }

        void Enqueue(string nodeId, string? sourceNodeId, Guid? sourceInvocationId, int depth)
        {
            if (!pending.TryGetValue(nodeId, out var arrivals))
                pending[nodeId] = arrivals = [];
            arrivals.Add(new Arrival(sourceNodeId, sourceInvocationId, depth));
            trail.Add("Arrival", nodeId, relatedNodeId: sourceNodeId,
                detail: $"depth={depth}", causedByNodeExecutionId: sourceInvocationId);
        }
    }

    private async Task<IProcessResult> InvokeNodeAsync(
        string nodeId,
        string processType,
        Guid invocationId,
        ProcessDescriptor descriptor,
        object?[] args,
        IServiceProvider resourceScope,
        ExecutionTrail trail,
        CancellationTokenSource linked,
        Action<Exception, string?, Guid?> logException,
        Action<Exception> recordFailure)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            // Task.Run deliberately receives no cancellation token: every
            // scheduled worker enters this try/catch even if cancellation won
            // the race before it started, so its exception is observable.
            linked.Token.ThrowIfCancellationRequested();
            var process = processes.Resolve(descriptor, resourceScope);
            var inputs = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < descriptor.Parameters.Count; i++)
                if (!descriptor.Parameters[i].IsCancellationToken)
                    inputs.Add(descriptor.Parameters[i].ParameterName, args[i]);

            trail.Add("Started", nodeId, invocationId, descriptor.MethodName,
                snapshot: inputs, captureSnapshot: true);
            using var activity = Traces.StartActivity("capability.process");
            activity?.SetTag("process.type", processType);
            activity?.SetTag("process.node_id", nodeId);
            activity?.SetTag("process.invocation_id", invocationId);

            var result = await processes.InvokeAsync(descriptor, process, args);
            trail.Add("Completed", nodeId, invocationId, descriptor.MethodName,
                intention: result.Intention, durationMs: clock.Elapsed.TotalMilliseconds,
                snapshot: result.Data, captureSnapshot: true);
            logger.LogInformation(
                "Capability node {NodeId} Process {Process} invocation {InvocationId} returned {Intention}",
                nodeId, processType, invocationId, result.Intention);
            return result;
        }
        catch (Exception exception)
        {
            recordFailure(exception);
            // Cancellation occurs in the worker as soon as the exception
            // is detected, not only when the scheduler later awaits the task.
            linked.Cancel();
            trail.Add(exception is OperationCanceledException ? "Cancelled" : "Failed",
                nodeId, invocationId, descriptor.MethodName,
                durationMs: clock.Elapsed.TotalMilliseconds, detail: exception.GetType().Name);
            logException(exception, nodeId, invocationId);
            throw;
        }
    }
}
