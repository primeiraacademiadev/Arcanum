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
