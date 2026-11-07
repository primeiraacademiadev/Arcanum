# Capability Model — JSON definitions and Process execution

Status: **architecture proposal for discussion** · 2026-11-07

## Objective

An Arcanum host (Minimal API, Worker Service or future transport) invokes a `Capability` by logical identifier. A `CapabilityResolver` executes its declared ordered `Process` steps. **Process implementations receive stable resources by constructor injection and invocation data by Execute-method injection**.

The model should be expressive enough to add outcome-directed transitions later, but V1 implements a deterministic sequential runner rather than a general graph engine.

## Concepts and ownership

- **Capability definition**: id/version, input/output contract identifiers and an ordered list of Process references; immutable after activation; local versioned JSON in V1.
- **Process catalog**: whitelisted map `processId -> implementation type + declared input/output/outcome metadata`. Do not load arbitrary CLR types from untrusted JSON.
- **Process**: one business responsibility, depends on services/repositories/loggers through its constructor; `ExecuteAsync` receives only typed data from invocation scope.
- **CapabilityResolver**: obtains a DI scope, instantiates/resolves each Process, binds its Execute parameters, invokes it, records results and determines continuation/termination.
- **Resource scope**: .NET DI scope for services with regular singleton/scoped/transient semantics, including UoW resources.
- **Data scope**: invocation-specific typed data registry, owned by the resolver; inputs and intermediate results are not registered as app services. Mutable execution state never escapes to another invocation.
- **CapabilityResult**: transport-neutral status, optional typed output, diagnostics. HTTP/Kafka adapt it at the host boundary.

## Example V1 capability definition

```json
{
  "id": "arca.audit.publish",
  "version": 1,
  "input": "arca.audit.publish-input.v1",
  "processes": [
    { "id": "validate", "process": "arca.audit.validate" },
    { "id": "deduplicate", "process": "arca.audit.deduplicate" },
    { "id": "classify", "process": "arca.audit.classify" },
    { "id": "persist", "process": "arca.audit.persist" }
  ]
}
```

The array order defines execution; a Process result may short-circuit with a terminal outcome. Reuse the **same registered Process** in different Capabilities only where responsibilities and data contracts match. The future graph model may add explicit `transitions` by named outcome.

## Proposed Process contract

Method-level data injection is an **Arcanum framework feature**, not something the standard Microsoft DI container performs automatically for arbitrary class methods.

A conceptual Process implementation:

```csharp
public sealed class ClassifyAuditProcess(
    IAuditClassifier classifier,
    ILogger<ClassifyAuditProcess> logger) : IProcess
{
    public async ValueTask<ProcessOutcome<ClassifiedAuditEvent>> ExecuteAsync(
        [FromCapabilityData("incoming")] IncomingAuditEvent incoming,
        [FromCapabilityData("producer")] ProducerIdentity producer,
        CancellationToken cancellationToken)
    {
        var value = await classifier.ClassifyAsync(
            incoming, producer, cancellationToken);
        return ProcessOutcome.Success(value);
    }
}
```

`IProcess` may be a **marker interface** with an explicitly validated Execute signature, or a future generic interface when method contracts are fixed; **choose after a small compile/test spike**. The catalog analyzes Execute signatures at startup and caches invocation delegates; no arbitrary parameter resolution or repeated reflection on each event. Inputs are resolved by **typed key + identifier** with ambiguity/missing-data errors, rather than by ambiguous type alone.

Output is assigned to a named data key specified in the catalog/definition or conventional metadata; the next Process may consume it. Outcomes such as `success`, `invalid`, `duplicate`, `denied` and `retryable_failure` remain semantic, not HTTP status codes. Returning a new typed object is preferred over implicit mutation of shared input objects.

## Two DI contexts are not necessarily two independent IServiceProvider roots

The proposed design has **two resolution domains**:

1. The host `IServiceProvider` with a **scoped** instance for the Capability execution. Constructor injection uses this scope. Its lifetime governs scoped repositories, UoW and other resources.
2. A **per-invocation data resolver** (`ICapabilityDataScope`) that binds typed Execute parameters. It is owned and disposed by the `CapabilityResolver`. It is not a second application root service container.

Do **not** normally invoke `BuildServiceProvider()` a second time: this duplicates singleton instances, can break scope and disposal semantics, and obscures ownership. The built-in DI container also does not provide hierarchical scopes for combining arbitrary runtime registrations. If runtime data needs a full DI container, benchmark and document a deliberate adapter; do not assume a child scope can override arbitrary services.

Avoid registering request payloads as `Singleton`/`Scoped` services in the host container merely to perform method injection. Keep the concepts separate even when implemented by a single DI root.

## Execution algorithm

1. Load and **validate** the immutable JSON definition on host startup (schema, unique step IDs, known Process IDs, declared input/output, compatible lifetimes and outcomes).
2. At invocation, create an appropriate resource scope, then a separate data scope populated with authenticated input and transport-neutral metadata.
3. Resolve each Process using the resource scope. Constructor injection supplies infrastructure.
4. Bind annotated Execute parameters from the invocation data scope; inject CancellationToken as a special runtime parameter.
5. Execute and validate Process outcome; register outputs into data scope and record step duration, outcome and trace IDs.
6. Continue to next step or stop for a terminal outcome. Only the hosting adapter maps result to HTTP status, message acknowledgement or retry.
7. Dispose data scope and resource scope, including async-disposable resources.

For HTTP request handling, the resolver may reuse the **existing request DI scope** rather than create a second scope, so user identity, transactional UoW and scoped middleware dependencies are consistent. For BackgroundService, create a new scope per message/invocation.

## Invariants

- All execution data are strictly isolated by invocation; do not leak across simultaneous requests.
- No Process directly decides where to branch; the Resolver owns control flow.
- The Resolver never retries an activity with non-idempotent side effects unless an explicit retry policy guarantees safety.
- Transactions/UoW belong to an explicitly documented boundary, not an implicit per-step commit.
- Tracing/logging are cross-cutting execution behaviors; not required business graph nodes.
- Sensitive execution data are not automatically logged or surfaced in telemetry.
- Definitions and Process registrations are versioned; a graph/editor is a **deferred extension**.
- Process catalog validation must reject unauthorized code types and invalid parameter bindings.

## Next implementation spike (small)

Build a linear four-step demo of `arca.audit.publish` with two inputs and one intermediate output injected into `ExecuteAsync`; one Process depends on an injected scoped repository. Verify concurrency isolation, scope disposal, cancellation, short-circuiting, missing data and startup validation. Only then finalize the Process interface and DI strategy.

Tracking: #13 (incremental framework) and #15 (future graph runtime).


## Design refinement — Intention-directed navigation (2026-11-07)

**Proposal under review.** Every Process returns **data result + intention**. The resolver first registers the returned typed result in the per-invocation data scope and then resolves the intention against the current node's transitions. Process code does not call another Process, know its node ID or select a next class.

Conceptual signature:

```csharp
public sealed record ProcessResult<T>(T? Data, string Intention);

public ValueTask<ProcessResult<ValidationData>> ExecuteAsync(
    [FromCapabilityData("incoming")] IncomingAuditEvent incoming,
    CancellationToken ct);
```

The result is typed output data (including validation/rejection details), **not the control-flow selector**. Intention is the control-flow signal. A Process may return `valid`, `invalid` or `duplicate`; the graph owns their destination. Error details must be sanitized and differentiated from unexpected exceptions. Avoid converting every unhandled exception to an intention.

### Example directed flow (candidate extension)

```json
{
  "id": "arca.audit.publish",
  "version": 1,
  "entry": "validate",
  "nodes": [
    {
      "id": "validate",
      "process": "arca.audit.validate",
      "saveResultAs": "validation",
      "on": { "valid": "deduplicate", "invalid": "@stop" }
    },
    {
      "id": "deduplicate",
      "process": "arca.audit.deduplicate",
      "saveResultAs": "deduplication",
      "on": { "new": "classify", "duplicate": "@stop", "conflict": "@fail" }
    },
    {
      "id": "classify",
      "process": "arca.audit.classify",
      "saveResultAs": "classified",
      "on": { "classified": "persist" }
    },
    {
      "id": "persist",
      "process": "arca.audit.persist",
      "saveResultAs": "receipt",
      "on": { "stored": "@complete" }
    }
  ]
}
```

This JSON is an **illustrative design**, not yet the accepted schema. V1 can begin with a linear node list and `@next` semantics; a one-branch proof is a small extension rather than a full graph engine.

### Intention namespaces

- **Local intentions**, e.g. `valid`, `duplicate`, `rejected`, are defined by reusable Process contracts; transition lookup is **relative to the current node**, preventing collisions across nodes.
- **Reserved framework intentions**, candidate prefix `@`: `@next` (linear fall-through only), `@complete` (success terminal), `@stop` (terminal with returned domain result), `@fail` (terminal failure). `@retry` is *reserved for future use*, and must never blindly repeat side effects.
- **Capability-qualified intentions**, e.g. `arca.audit.publish.duplicate`, are a future **qualified identifier** for external observation/routing. Prefer to **qualify at the resolver boundary**; do not force reusable Processes to hardcode their caller's Capability ID.

The exact reserved names and outcome model are not final. Do not let arbitrary user-defined names impersonate reserved intentions. A transition must never be silently inferred when no mapping is present: reject invalid definitions at startup and fail safely if an unexpected intention appears.

### Data scope and graph execution order

1. Resolve Process instance from the host **resource scope** and bind its Execute inputs from per-invocation **data scope**.
2. Invoke and obtain `ProcessResult<T>`.
3. Validate and write the result to `saveResultAs` (type + name), with explicit overwrite/version rules.
4. Resolve the intention using the **current node's** validated transition map.
5. Record Process ID, Capability version, intention, transition and timing (without logging sensitive values).
6. Move to the target node or conclude with explicit terminal semantics.

For V1, disallow cycles and parallel branches; enforce max-node execution limits and UoW/side-effect invariants. Version the definition atomically so live executions use a stable plan.

## Dependency injection candidates and decision criteria (2026-11-07)

Retain **Microsoft.Extensions.DependencyInjection** as baseline; evaluate third-party containers when they demonstrably simplify the two-context model. Constructor injection remains standard host DI; arbitrary method-parameter binding remains an **Arcanum responsibility** even when a third-party container supports parameterized object creation.

| Candidate | Main reason to test | Caveat |
| --- | --- | --- |
| Microsoft DI | Native IHost, common scopes, minimal dependency footprint | Cannot configure arbitrary child registrations on a per-invocation scope; needs DataScope binder |
| Autofac | Nested lifetime scopes, local registrations, parameterized resolution and mature ASP.NET Core integration | Nested scoped service instances and UoW sharing require explicit lifecycle design; does not auto-bind arbitrary Execute methods |
| DryIoc | Advanced resolution strategies and potential performance | Validate adapter compatibility and binding against current .NET host conventions |
| Stashbox | Scope flexibility and Generic Host integration | Evaluate behavior against reference cases before adopting |
| Lamar | Convention-based registrations and integration | No clear unique need yet |
| LightInject | Feature-rich integration | No proven advantage on current contract |
| Grace | Flexible composition | Not priority without specific need |
| Simple Injector | Container validation / lifestyle diagnostics | Cross-wiring second container adds complexity; its documentation cautions against hidden method injection |

**Required decision spike:** same capability definition, same scoped repository/UoW, concurrent invocations, cancellation, disposal and typed data injection on Microsoft DI vs Autofac; add DryIoc only if needed. Measure implementation complexity and semantics before throughput. **Do not create two independent root ServiceProviders by default**: two *resolution contracts* are enough until proven otherwise.

Tracking: #17 for V1 and #15 for graph navigation; new DI evaluation issue to be linked.
