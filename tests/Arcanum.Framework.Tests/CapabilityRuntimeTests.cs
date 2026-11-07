using Arcanum.Demo.Api;
using Arcanum.Framework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace Arcanum.Framework.Tests;

public class CapabilityRuntimeTests
{
    private static ServiceProvider CreateProvider(int defaultMaxSteps = 100)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IExecutionProbe, ExecutionProbe>();
        services.AddArcanumFramework(typeof(ValidateProcess).Assembly);
        services.Configure<CapabilityOptions>(options => options.DefaultMaxSteps = defaultMaxSteps);
        services.AddArcanumCapabilitiesFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "capabilities"));

        var provider = services.BuildServiceProvider(validateScopes: true);
        _ = provider.GetRequiredService<CapabilityCatalog>();
        return provider;
    }

    private static ExecutionDatum[] Data(DemoInput request) =>
    [
        ExecutionDatum.Of("request", request),
        ExecutionDatum.Of("counter", new CounterState(0))
    ];

    [Fact]
    public async Task Valid_pipeline_uses_sync_async_and_one_scoped_resource()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var resolver = provider.GetRequiredService<CapabilityResolver>();

        var result = await resolver.ExecuteAsync(
            "demo.publish", Data(new DemoInput("  abc ")), scope.ServiceProvider);

        Assert.Equal("@complete", result.Terminal);
        Assert.Equal("finished", result.Intention);
        Assert.Equal(3, result.Steps);
        var completed = Assert.IsType<CompletedData>(result.Data);
        Assert.Equal("ABC", completed.Text);
        Assert.True(completed.SameScopedResource);
    }

    [Fact]
    public async Task Invalid_input_stops_after_validation()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var result = await provider.GetRequiredService<CapabilityResolver>()
            .ExecuteAsync("demo.publish", Data(new DemoInput("  ")), scope.ServiceProvider);

        Assert.Equal("@stop", result.Terminal);
        Assert.Equal("invalid", result.Intention);
        Assert.Equal(1, result.Steps);
        Assert.IsType<ValidationData>(result.Data);
    }

    [Fact]
    public async Task Same_stateless_process_can_be_visited_repeatedly()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var result = await provider.GetRequiredService<CapabilityResolver>()
            .ExecuteAsync("demo.loop", Data(new DemoInput("ok", Iterations: 3)), scope.ServiceProvider);

        Assert.Equal("@complete", result.Terminal);
        Assert.Equal(3, result.Steps);
        Assert.Equal(3, Assert.IsType<CounterState>(result.Data).Count);
    }

    [Fact]
    public async Task Completion_at_exact_override_limit_is_allowed()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var result = await provider.GetRequiredService<CapabilityResolver>()
            .ExecuteAsync("demo.loop", Data(new DemoInput("ok", Iterations: 4)), scope.ServiceProvider);

        Assert.Equal("@complete", result.Terminal);
        Assert.Equal(4, result.Steps);
    }

    [Fact]
    public async Task Override_limit_throws_before_fifth_execution()
    {
        using var provider = CreateProvider(defaultMaxSteps: 25);
        using var scope = provider.CreateScope();
        var exception = await Assert.ThrowsAsync<CapabilityExecutionLimitExceededException>(
            () => provider.GetRequiredService<CapabilityResolver>()
                .ExecuteAsync("demo.loop", Data(new DemoInput("ok", Iterations: 100)), scope.ServiceProvider));

        Assert.Equal(4, exception.Limit);
        Assert.Equal("demo.loop", exception.Capability);
    }

    [Fact]
    public async Task Framework_default_applies_when_capability_does_not_override()
    {
        using var provider = CreateProvider(defaultMaxSteps: 2);
        using var scope = provider.CreateScope();
        var exception = await Assert.ThrowsAsync<CapabilityExecutionLimitExceededException>(
            () => provider.GetRequiredService<CapabilityResolver>()
                .ExecuteAsync("demo.default-loop", Data(new DemoInput("ok", Iterations: 100)), scope.ServiceProvider));

        Assert.Equal(2, exception.Limit);
    }

    [Fact]
    public async Task Concurrent_invocations_do_not_share_execution_data_or_scoped_resources()
    {
        using var provider = CreateProvider();
        var resolver = provider.GetRequiredService<CapabilityResolver>();

        async Task<CapabilityExecutionResult> Run(string value)
        {
            using var scope = provider.CreateScope();
            return await resolver.ExecuteAsync(
                "demo.publish", Data(new DemoInput(value)), scope.ServiceProvider);
        }

        var outputs = await Task.WhenAll(Run("first"), Run("second"));
        var one = Assert.IsType<CompletedData>(outputs[0].Data);
        var two = Assert.IsType<CompletedData>(outputs[1].Data);

        Assert.Equal("FIRST", one.Text);
        Assert.Equal("SECOND", two.Text);
        Assert.True(one.SameScopedResource && two.SameScopedResource);
        Assert.NotEqual(one.ScopeId, two.ScopeId);
    }

    [Fact]
    public async Task Cancellation_is_honoured_before_invoking_a_process()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetRequiredService<CapabilityResolver>().ExecuteAsync(
                "demo.publish", Data(new DemoInput("ok")), scope.ServiceProvider, source.Token));
    }

    [Fact]
    public async Task Missing_execution_data_fails_without_constructing_a_second_container()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<CapabilityDataException>(() =>
            provider.GetRequiredService<CapabilityResolver>().ExecuteAsync(
                "demo.publish", Array.Empty<ExecutionDatum>(), scope.ServiceProvider));
    }

    [Fact]
    public void Data_resolution_by_type_rejects_ambiguity_and_allows_explicit_keys()
    {
        var data = new DataResolver().CreateScope(
        [
            ExecutionDatum.Of("a", new CounterState(1)),
            ExecutionDatum.Of("b", new CounterState(2))
        ]);

        Assert.Throws<CapabilityDataException>(() => data.Resolve(typeof(CounterState), null));
        Assert.Equal(1, Assert.IsType<CounterState>(data.Resolve(typeof(CounterState), "a")).Count);
    }

    [Fact]
    public void Unknown_graph_target_is_rejected_during_catalog_validation()
    {
        using var provider = CreateProvider();
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "capabilities", "demo.publish.json"));
        var definition = CapabilityCatalog.ParseJson(source);
        var validate = typeof(ValidateProcess).FullName!;
        definition.Graph.Transitions[validate]["valid"] = "Some.Nonexistent.Process";

        Assert.Throws<CapabilityDefinitionException>(() =>
            new CapabilityCatalog([definition], provider.GetRequiredService<ProcessCatalog>(),
                new CapabilityOptions()));
    }

    [Fact]
    public async Task Unmapped_intention_raises_a_transition_exception()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "capabilities", "demo.publish.json"));
        var definition = CapabilityCatalog.ParseJson(source);
        var validate = typeof(ValidateProcess).FullName!;
        definition.Graph.Transitions[validate].Remove("valid");

        var catalog = new CapabilityCatalog([definition],
            provider.GetRequiredService<ProcessCatalog>(), new CapabilityOptions());
        var resolver = new CapabilityResolver(
            catalog,
            provider.GetRequiredService<ProcessResolver>(),
            provider.GetRequiredService<DataResolver>(),
            Options.Create(new CapabilityOptions()),
            NullLogger<CapabilityResolver>.Instance);

        await Assert.ThrowsAsync<CapabilityTransitionException>(() =>
            resolver.ExecuteAsync(
                "demo.publish", Data(new DemoInput("ok")), scope.ServiceProvider));
    }


    [Fact]
    public void Only_JSON_declared_processes_are_registered()
    {
        using var provider = CreateProvider();
        var descriptor = provider.GetRequiredService<ProcessCatalog>()
            .Get(typeof(ValidateProcess).FullName!);

        Assert.Equal(typeof(ValidateProcess), descriptor.ProcessType);
        Assert.Equal(typeof(EchoActivity), provider.GetRequiredService<ProcessCatalog>()
            .Get(typeof(EchoActivity).FullName!).ProcessType);
        Assert.Throws<CapabilityDefinitionException>(() =>
            provider.GetRequiredService<ProcessCatalog>()
                .Get(typeof(UnlistedActivity).FullName!));
    }

    [Fact]
    public async Task Method_parameters_allow_explicit_names_when_the_type_is_ambiguous()
    {
        var dataResolver = new DataResolver();
        var data = dataResolver.CreateScope(
        [
            ExecutionDatum.Of("other", new CounterState(4)),
            ExecutionDatum.Of("chosen", new CounterState(7))
        ]);
        var descriptor = ProcessDescriptor.Create(typeof(Fixtures.NamedValueProcess));
        var args = dataResolver.Bind(descriptor, data, CancellationToken.None);
        var result = await descriptor.InvokeAsync(new Fixtures.NamedValueProcess(), args);

        Assert.Equal("selected", result.Intention);
        Assert.Equal(7, Assert.IsType<CounterState>(result.Data).Count);
    }


    [Fact]
    public void Process_name_and_marker_are_unnecessary_for_valid_descriptor()
    {
        var catalog = new ProcessCatalog([typeof(Fixtures.UnconventionallyNamedActivity)]);
        Assert.Equal(typeof(Fixtures.UnconventionallyNamedActivity),
            catalog.Get(typeof(Fixtures.UnconventionallyNamedActivity).FullName!).ProcessType);

        // An ordinary class with Execute is valid in isolation; only a JSON graph
        // and a trusted assembly registration can make it executable by the host.
        var descriptor = ProcessDescriptor.Create(typeof(Fixtures.AccidentalProcess));
        Assert.Equal(typeof(Fixtures.AccidentalProcess), descriptor.ProcessType);
    }

    [Fact]
    public async Task Parameter_name_takes_precedence_over_ambiguous_type_matches()
    {
        var dataResolver = new DataResolver();
        var data = dataResolver.CreateScope(
        [
            ExecutionDatum.Of("other", new CounterState(4)),
            ExecutionDatum.Of("chosen", new CounterState(7))
        ]);
        var descriptor = ProcessDescriptor.Create(typeof(Fixtures.ConventionalNameProcess));
        var args = dataResolver.Bind(descriptor, data, CancellationToken.None);
        var result = await descriptor.InvokeAsync(new Fixtures.ConventionalNameProcess(), args);
        Assert.Equal(7, Assert.IsType<CounterState>(result.Data).Count);
    }

    [Fact]
    public void ReturnName_attribute_supplies_the_default_result_key()
    {
        using var provider = CreateProvider();
        var descriptor = provider.GetRequiredService<ProcessCatalog>()
            .Get(typeof(ValidateProcess).FullName!);
        Assert.Equal("validated", descriptor.ReturnName);

        var capability = provider.GetRequiredService<CapabilityCatalog>().Get("demo.publish");
        Assert.Null(capability.Processes[typeof(ValidateProcess).FullName!].SaveResultAs);
    }

    [Fact]
    public async Task JSON_contract_dispatcher_binds_DTO_and_returns_declared_response()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        using var payload = JsonDocument.Parse("""{"value":"  example  "}""");

        var result = await provider.GetRequiredService<CapabilityJsonDispatcher>()
            .ExecuteAsync("demo.publish", payload.RootElement, scope.ServiceProvider);

        Assert.Equal("@complete", result.Terminal);
        Assert.Equal("EXAMPLE", Assert.IsType<CompletedData>(result.Data).Text);
    }

    [Fact]
    public async Task Nested_JSON_contract_maps_payload_without_endpoint_conversions()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        using var payload = JsonDocument.Parse("""{"payload":{"value":"  nested  "}}""");

        var result = await provider.GetRequiredService<CapabilityJsonDispatcher>()
            .ExecuteAsync("demo.nested", payload.RootElement, scope.ServiceProvider);

        Assert.Equal("NESTED", Assert.IsType<CompletedData>(result.Data).Text);
        Assert.Equal(3, result.Steps);
    }

    [Fact]
    public async Task Contract_default_creates_counter_without_endpoint_code()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        using var payload = JsonDocument.Parse("""{"value":"ok","iterations":3}""");

        var result = await provider.GetRequiredService<CapabilityJsonDispatcher>()
            .ExecuteAsync("demo.loop", payload.RootElement, scope.ServiceProvider);

        Assert.Equal("@complete", result.Terminal);
        Assert.Equal(3, Assert.IsType<CounterState>(result.Data).Count);
    }

    [Fact]
    public async Task Missing_nested_contract_property_is_a_contract_error()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        using var payload = JsonDocument.Parse("""{"value":"absent"}""");

        await Assert.ThrowsAsync<CapabilityContractException>(() =>
            provider.GetRequiredService<CapabilityJsonDispatcher>()
                .ExecuteAsync("demo.nested", payload.RootElement, scope.ServiceProvider));
    }

    [Fact]
    public void Invalid_contract_type_and_response_are_rejected_on_startup()
    {
        using var provider = CreateProvider();
        var json = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "capabilities", "demo.publish.json"));
        var definition = CapabilityCatalog.ParseJson(json);
        var processes = provider.GetRequiredService<ProcessCatalog>();

        definition.Contract.Inputs["request"].Type = "System.IO.File";
        Assert.Throws<CapabilityDefinitionException>(() =>
            new CapabilityCatalog([definition], processes, new CapabilityOptions()));

        definition.Contract.Inputs["request"].Type = typeof(DemoInput).FullName!;
        definition.Contract.Responses["@complete"].Type = typeof(ValidationData).FullName!;
        Assert.Throws<CapabilityDefinitionException>(() =>
            new CapabilityCatalog([definition], processes, new CapabilityOptions()));
    }

    [Fact]
    public async Task Class_without_interface_suffix_or_attribute_runs_when_declared_in_JSON()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        using var payload = JsonDocument.Parse("""{"value":"custom"}""");

        var result = await provider.GetRequiredService<CapabilityJsonDispatcher>()
            .ExecuteAsync("demo.activity", payload.RootElement, scope.ServiceProvider);

        Assert.Equal("@complete", result.Terminal);
        Assert.Equal("custom", Assert.IsType<DemoInput>(result.Data).Value);
        Assert.Equal(1, result.Steps);
    }

    [Fact]
    public void Nonpositive_default_and_override_are_rejected()
    {
        using var provider = CreateProvider();
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "capabilities", "demo.loop.json"));
        var definition = CapabilityCatalog.ParseJson(source);
        var processes = provider.GetRequiredService<ProcessCatalog>();

        Assert.Throws<CapabilityDefinitionException>(() =>
            new CapabilityCatalog([definition], processes, new CapabilityOptions { DefaultMaxSteps = 0 }));
        definition.MaxSteps = 0;
        Assert.Throws<CapabilityDefinitionException>(() =>
            new CapabilityCatalog([definition], processes, new CapabilityOptions()));
    }
}
