using System.Collections.Concurrent;
using Arcanum.Demo.Api;
using Arcanum.Framework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Arcanum.Framework.Tests;

public sealed class ParallelExecutionTests
{
    private sealed class EnabledTraceLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private sealed class ExceptionRecordingLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<Exception> _exceptions = new();
        public IReadOnlyList<Exception> Exceptions => _exceptions.ToArray();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
                _exceptions.Enqueue(exception);
        }
    }

    private static ServiceProvider Provider(
        bool trace = false, int maxSnapshotBytes = 65536,
        ILogger<CapabilityResolver>? customLogger = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IExecutionProbe, ExecutionProbe>();
        services.AddScoped<ParallelFaultGate>();
        services.AddArcanumFramework(typeof(ValidateProcess).Assembly);
        services.Configure<CapabilityOptions>(o => o.MaxTraceSnapshotBytes = maxSnapshotBytes);
        services.AddArcanumCapabilitiesFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "capabilities"));
        if (trace)
            services.AddSingleton<ILogger<CapabilityResolver>>(new EnabledTraceLogger<CapabilityResolver>());
        if (customLogger is not null)
            services.AddSingleton(customLogger);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<CapabilityExecutionResult> Run(
        ServiceProvider provider, string capability, string value, string? password = null)
    {
        using var scope = provider.CreateScope();
        return await provider.GetRequiredService<CapabilityResolver>().ExecuteAsync(
            capability, [ExecutionDatum.Of("request", new DemoInput(value, Password: password))],
            scope.ServiceProvider);
    }

    private static ExecutionBreadcrumb Event(CapabilityExecutionResult result, string node, string kind) =>
        Assert.Single(result.Breadcrumbs, b => b.NodeId == node && b.Kind == kind);

    [Fact]
    public async Task Two_processes_overlap_and_join_executes_exactly_once_after_both()
    {
        using var provider = Provider();
        var result = await Run(provider, "demo.parallel", " AbC ");
        Assert.Equal("@complete", result.Terminal);
        Assert.Equal("ABC|abc", result.Data);
        Assert.Equal(5, result.Steps);
        var alphaStart = Event(result, "alpha", "Started");
        var betaStart = Event(result, "beta", "Started");
        var alphaEnd = Event(result, "alpha", "Completed");
        var betaEnd = Event(result, "beta", "Completed");
        var joinStart = Event(result, "join", "Started");
        Assert.True(alphaStart.Sequence < betaEnd.Sequence);
        Assert.True(betaStart.Sequence < alphaEnd.Sequence);
        Assert.True(joinStart.Sequence > alphaEnd.Sequence);
        Assert.True(joinStart.Sequence > betaEnd.Sequence);
        Assert.Equal("Execute", joinStart.Method);
        Assert.Equal(1, result.Breadcrumbs.Count(b => b.Kind == "CapabilityCompleted"));
        Assert.Equal(Enumerable.Range(1, result.Breadcrumbs.Count).Select(x => (long)x),
            result.Breadcrumbs.Select(b => b.Sequence));
        Assert.All(result.Breadcrumbs, b => Assert.True(b.Timestamp > DateTimeOffset.MinValue));
    }

    [Theory]
    [InlineData("skip right")]
    [InlineData("include right")]
    public async Task Conditional_branch_changes_join_wait_without_losing_global_completion(string value)
    {
        using var provider = Provider();
        var result = await Run(provider, "demo.parallel-choice", value);
        var join = Event(result, "join", "Started");
        if (value.StartsWith("skip", StringComparison.Ordinal))
        {
            var detachedEnd = Event(result, "detached", "Completed");
            Assert.True(join.Sequence < detachedEnd.Sequence);
            var capabilityEnd = Assert.Single(result.Breadcrumbs, b => b.Kind == "CapabilityCompleted");
            Assert.True(capabilityEnd.Sequence > detachedEnd.Sequence);
            Assert.Null(result.Intention); // independent terminal branches used different intentions
            Assert.DoesNotContain(result.Breadcrumbs,
                b => b.NodeId == "beta" && b.Kind == "Started");
        }
        else
        {
            var betaEnd = Event(result, "beta", "Completed");
            Assert.True(join.Sequence > betaEnd.Sequence);
            Assert.DoesNotContain(result.Breadcrumbs,
                b => b.NodeId == "detached");
        }
        Assert.Equal("@complete", result.Terminal);
        Assert.Equal(value.ToUpperInvariant(), result.Data);
        Assert.Equal(6, result.Steps);
    }

    [Fact]
    public async Task Trace_copies_input_and_output_and_redacts_secret_fields()
    {
        using var provider = Provider(trace: true);
        var result = await Run(provider, "demo.parallel", "ABC", "very-secret-password");
        var started = Event(result, "validate", "Started");
        Assert.NotNull(started.SnapshotJson);
        Assert.Contains("\"password\":\"[redacted]\"", started.SnapshotJson!);
        Assert.DoesNotContain("very-secret-password", started.SnapshotJson!);
        Assert.Contains("ABC", Event(result, "alpha", "Completed").SnapshotJson!);
        Assert.All(result.Breadcrumbs.Where(b => b.SnapshotJson is not null),
            b => Assert.DoesNotContain("very-secret-password", b.SnapshotJson!));
    }

    [Fact]
    public async Task Without_trace_there_are_breadcrumbs_but_no_data_snapshots()
    {
        using var provider = Provider();
        var result = await Run(provider, "demo.parallel", "abc", "secret");
        Assert.NotEmpty(result.Breadcrumbs);
        Assert.All(result.Breadcrumbs, b => Assert.Null(b.SnapshotJson));
    }

    [Fact]
    public async Task Snapshot_size_limit_refuses_to_keep_partial_payloads()
    {
        using var provider = Provider(trace: true, maxSnapshotBytes: 8);
        var result = await Run(provider, "demo.parallel", "abc");
        Assert.True(Event(result, "validate", "Started").SnapshotTruncated);
        Assert.Null(Event(result, "validate", "Started").SnapshotJson);
    }

    [Fact]
    public async Task Concurrent_capabilities_have_distinct_trails()
    {
        using var provider = Provider();
        var runs = await Task.WhenAll(
            Run(provider, "demo.parallel", "one"),
            Run(provider, "demo.parallel", "two"));
        Assert.NotEqual(runs[0].ExecutionId, runs[1].ExecutionId);
        Assert.Equal("ONE|one", runs[0].Data);
        Assert.Equal("TWO|two", runs[1].Data);
        Assert.All(runs, r => Assert.Equal(1, r.Breadcrumbs.Count(b => b.Kind == "CapabilityStarted")));
    }
    [Fact]
    public async Task Distinct_nodes_can_use_the_same_Process_class()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();
        var json = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "capabilities", "demo.parallel.json"));
        var definition = CapabilityCatalog.ParseJson(json);
        definition.Graph.Nodes["beta"].Process = typeof(ParallelAlphaProcess).FullName!;
        definition.Graph.Nodes["beta"].SaveResultAs = "beta";
        var resolver = new CapabilityResolver(
            new CapabilityCatalog([definition],
                provider.GetRequiredService<ProcessCatalog>(), new CapabilityOptions()),
            provider.GetRequiredService<ProcessResolver>(),
            provider.GetRequiredService<DataResolver>(),
            Microsoft.Extensions.Options.Options.Create(new CapabilityOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CapabilityResolver>.Instance);

        var result = await resolver.ExecuteAsync(
            "demo.parallel", [ExecutionDatum.Of("request", new DemoInput(" AbC "))],
            scope.ServiceProvider);

        Assert.Equal("ABC|ABC", result.Data);
        Assert.NotEqual(Event(result, "alpha", "Started").NodeExecutionId,
            Event(result, "beta", "Started").NodeExecutionId);
        Assert.Equal(1, result.Breadcrumbs.Count(b => b.Kind == "CapabilityCompleted"));
    }

    [Fact]
    public async Task Cancellation_during_parallel_work_settles_all_started_tasks_before_throwing()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();
        using var source = new CancellationTokenSource();
        source.CancelAfter(45);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetRequiredService<CapabilityResolver>().ExecuteAsync(
                "demo.parallel",
                [ExecutionDatum.Of("request", new DemoInput("abc"))],
                scope.ServiceProvider, source.Token));

        var trail = Assert.IsAssignableFrom<IReadOnlyList<ExecutionBreadcrumb>>(
            error.Data["Arcanum.Breadcrumbs"]);
        Assert.Contains(trail, b => b.Kind == "CapabilityCancelled");
        Assert.DoesNotContain(trail, b => b.NodeId == "join" && b.Kind == "Started");
        var completed = Assert.Single(trail, b => b.Kind == "CapabilityCancelled");
        Assert.DoesNotContain(trail, b =>
            b.Sequence > completed.Sequence && b.Kind is "Started" or "Completed" or "Cancelled");
    }

    [Fact]
    public void Graph_spawn_edges_are_system_intentions_not_process_intentions()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "capabilities", "demo.parallel.json");
        var graph = CapabilityCatalog.ParseJson(File.ReadAllText(path)).Graph;
        var targets = graph.Matching("fork", "ready");
        Assert.Equal(2, targets.Length);
        Assert.Single(targets, edge => edge.Intention == "@spawn" && edge.To == "beta");
        Assert.Single(targets, edge => edge.Intention == "ready" && edge.To == "alpha");
    }

    [Fact]
    public async Task Process_fault_immediately_cancels_and_logs_every_parallel_exception()
    {
        var recorder = new ExceptionRecordingLogger<CapabilityResolver>();
        using var provider = Provider(customLogger: recorder);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Run(provider, "demo.parallel-failure", "valid"));

        Assert.Equal("alpha failure", error.Message);
        Assert.Contains(recorder.Exceptions, e => e is InvalidOperationException
            && e.Message == "alpha failure");
        Assert.Contains(recorder.Exceptions, e => e is InvalidOperationException
            && e.Message == "beta failure after cancellation");

        var trail = Assert.IsAssignableFrom<IReadOnlyList<ExecutionBreadcrumb>>(
            error.Data["Arcanum.Breadcrumbs"]);
        var failedAlpha = Assert.Single(trail, e => e.NodeId == "alpha" && e.Kind == "Failed");
        var failedBeta = Assert.Single(trail, e => e.NodeId == "beta" && e.Kind == "Failed");
        var capabilityFailed = Assert.Single(trail, e => e.Kind == "CapabilityFailed");
        Assert.True(capabilityFailed.Sequence > failedAlpha.Sequence);
        Assert.True(capabilityFailed.Sequence > failedBeta.Sequence);
        Assert.DoesNotContain(trail, e => e.NodeId == "join" && e.Kind == "Started");
    }

    [Fact]
    public async Task Engine_transition_exception_is_logged_and_cancels_the_run()
    {
        var recorder = new ExceptionRecordingLogger<CapabilityResolver>();
        using var provider = Provider(customLogger: recorder);
        using var scope = provider.CreateScope();
        var definition = CapabilityCatalog.ParseJson(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "capabilities", "demo.parallel.json")));
        definition.Graph.Edges.RemoveAll(edge => edge.From == "fork" && edge.Intention == "ready");
        // The catalog rejects a node with no business continuation, before execution.
        Assert.Throws<CapabilityDefinitionException>(() =>
            new CapabilityCatalog([definition], provider.GetRequiredService<ProcessCatalog>(),
                new CapabilityOptions()));
    }

}
