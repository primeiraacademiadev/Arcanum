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

    private static ServiceProvider Provider(bool trace = false, int maxSnapshotBytes = 65536)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IExecutionProbe, ExecutionProbe>();
        services.AddArcanumFramework(typeof(ValidateProcess).Assembly);
        services.Configure<CapabilityOptions>(o => o.MaxTraceSnapshotBytes = maxSnapshotBytes);
        services.AddArcanumCapabilitiesFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "capabilities"));
        if (trace)
            services.AddSingleton<ILogger<CapabilityResolver>>(new EnabledTraceLogger<CapabilityResolver>());
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
        Assert.Single(result.Breadcrumbs.Where(b => b.NodeId == "Arcanum.Demo.Api." + node &&
            b.Kind == kind));

    [Fact]
    public async Task Two_processes_overlap_and_join_executes_exactly_once_after_both()
    {
        using var provider = Provider();
        var result = await Run(provider, "demo.parallel", " AbC ");
        Assert.Equal("@complete", result.Terminal);
        Assert.Equal("ABC|abc", result.Data);
        Assert.Equal(4, result.Steps);
        var alphaStart = Event(result, "ParallelAlphaProcess", "Started");
        var betaStart = Event(result, "ParallelBetaProcess", "Started");
        var alphaEnd = Event(result, "ParallelAlphaProcess", "Completed");
        var betaEnd = Event(result, "ParallelBetaProcess", "Completed");
        var joinStart = Event(result, "ParallelJoinProcess", "Started");
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
        var join = Event(result, "ParallelOptionalJoinProcess", "Started");
        if (value.StartsWith("skip", StringComparison.Ordinal))
        {
            var detachedEnd = Event(result, "ParallelDetachedProcess", "Completed");
            Assert.True(join.Sequence < detachedEnd.Sequence);
            Assert.DoesNotContain(result.Breadcrumbs,
                b => b.NodeId == "Arcanum.Demo.Api.ParallelBetaProcess" && b.Kind == "Started");
        }
        else
        {
            var betaEnd = Event(result, "ParallelBetaProcess", "Completed");
            Assert.True(join.Sequence > betaEnd.Sequence);
            Assert.DoesNotContain(result.Breadcrumbs,
                b => b.NodeId == "Arcanum.Demo.Api.ParallelDetachedProcess");
        }
        Assert.Equal("@complete", result.Terminal);
        Assert.Equal(value.ToUpperInvariant(), result.Data);
        Assert.Equal(5, result.Steps);
    }

    [Fact]
    public async Task Trace_copies_input_and_output_and_redacts_secret_fields()
    {
        using var provider = Provider(trace: true);
        var result = await Run(provider, "demo.parallel", "ABC", "very-secret-password");
        var started = Event(result, "ValidateProcess", "Started");
        Assert.NotNull(started.SnapshotJson);
        Assert.Contains("\"password\":\"[redacted]\"", started.SnapshotJson!);
        Assert.DoesNotContain("very-secret-password", started.SnapshotJson!);
        Assert.Contains("ABC", Event(result, "ParallelAlphaProcess", "Completed").SnapshotJson!);
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
        Assert.True(Event(result, "ValidateProcess", "Started").SnapshotTruncated);
        Assert.Null(Event(result, "ValidateProcess", "Started").SnapshotJson);
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
}
