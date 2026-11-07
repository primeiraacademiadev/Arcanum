# Arcanum — Capabilities, Processes and Graph Runtime

Status: **design proposal** · 2026-11-07. Runtime implementation and JSON schema must be validated by a small executable spike.

## Design principles

Arcanum describes a Capability as a versioned executable **process graph** in the same solution as the .NET code, to make AI-assisted development traceable and understandable.

- **Process identity**: CLR `Type.FullName` (namespace + class), without an extra business alias or mandatory `[Process]` attribute.
- **Presentation**: `displayName` (required) and `description` (recommended) explain the intended behavior of each Process and Capability.
- **Process catalog vs graph**: define Processes/metadata once; define entry and intention-driven connections separately. Avoid verbose explicit edge objects.
- **Repeated execution**: Process instances must hold no cross-execution business state. The same Process may be visited multiple times during graph traversal, including in cycles. A Process class name acts as a **graph node key** with one transition table per key; independent nodes with different transitions but the same implementation would require a separate occurrence identity in the future.
- **Safety**: every invocation has a bounded `maxSteps` counter that prevents endless navigation.
- **Two-way development**: code can generate Process metadata/catalog; a process graph can scaffold missing Process classes/contracts/tests. Graph remains authority for *connections*, implementation for *behavior*.
- **One delivery unit**: graph JSON and corresponding .NET implementation are versioned/tested together in the monorepo. No arbitrary code loading from JSON.

## Example JSON — Processes separated from graph

```json
{
  "name": "arca.audit.publish",
  "displayName": "Publicação de eventos de auditoria",
  "description": "Valida, classifica e regista eventos enviados por produtores autorizados.",
  "version": 1,
  "maxSteps": 100,
  "processes": {
    "Arcanum.Arca.Processes.ValidateAuditProcess": {
      "displayName": "Validar evento",
      "description": "Verifica o contrato e os dados obrigatórios."
    },
    "Arcanum.Arca.Processes.ClassifyAuditProcess": {
      "displayName": "Classificar evento",
      "description": "Resolve o nível de acesso do evento e dos detalhes."
    },
    "Arcanum.Arca.Processes.PersistAuditProcess": {
      "displayName": "Persistir evento",
      "description": "Regista o evento de forma transacional e idempotente."
    }
  },
  "graph": {
    "entry": "Arcanum.Arca.Processes.ValidateAuditProcess",
    "transitions": {
      "Arcanum.Arca.Processes.ValidateAuditProcess": {
        "valid": "Arcanum.Arca.Processes.ClassifyAuditProcess",
        "invalid": "@stop"
      },
      "Arcanum.Arca.Processes.ClassifyAuditProcess": {
        "classified": "Arcanum.Arca.Processes.PersistAuditProcess"
      },
      "Arcanum.Arca.Processes.PersistAuditProcess": {
        "stored": "@complete"
      }
    }
  }
}
```

This is an **illustrative fragment**; actual audit ingestion also needs producer authentication, deduplication, conflict branches and integration tests. A compact form or generated Process metadata may reduce JSON duplication later.

### Identity, edges and presentation

- The **key in `processes`** is the technical Process class name.
- The **key in `graph.transitions`** is the current Process class; each intention maps to the next class or a reserved directive.
- A Process can occur repeatedly **through navigation** (e.g. a loop returning to the same class); execution count is not part of the class identity.
- `displayName` provides the human-oriented label shown in graph diagrams; the class name remains available in technical details.
- If a future Capability truly needs two *different transition maps* for two occurrences of the same class, introduce an optional occurrence ID then; this is not a barrier to repeated execution now.

## Resolvers

| Component | Responsibilities |
| --- | --- |
| `CapabilityResolver` | Load validated immutable graph plan, track current node and per-invocation step budget, execute intentions/transitions, return Capability result and tracing |
| `ProcessResolver` | Map trusted `Type.FullName` to CLR type in explicitly registered assemblies, instantiate via the current Host DI scope, cache method invocation descriptors |
| `DataResolver` | Bind named typed execution data to `Execute`/`ExecuteAsync` arguments and store typed Process results in a per-invocation DataScope |

### Execution and resource scopes

Host infrastructure lives in **one .NET DI root**, with an appropriate resource scope per invocation. For HTTP, normally use `HttpContext.RequestServices`; for Workers, create a new scope per message. All Processes in one Capability execution use the same scope for Unit of Work consistency.

Execution data lives in a separate **logical resolution domain**, not necessarily another `IServiceProvider`. DataScope is unique to the invocation and thread/concurrency-safe by isolation. Avoid capturing invocation state in singleton Process instances. A stateless Process may still have scoped repository dependencies and therefore must be constructed/resolved with compatible lifetime.

### Process method contract (proposed)

```csharp
public sealed record ProcessResult<T>(T? Data, string Intention);

public sealed class ClassifyAuditProcess(
    IAuditClassifier classifier,
    ILogger<ClassifyAuditProcess> logger)
{
    public async ValueTask<ProcessResult<ClassifiedAuditEvent>> ExecuteAsync(
        IncomingAuditEvent incoming,
        ProducerIdentity producer,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Classifying an incoming audit event");
        var classified = await classifier.ClassifyAsync(
            incoming, producer, cancellationToken);
        return new(classified, "classified");
    }
}
```

Accept one unambiguous `Execute` (sync `ProcessResult<T>`) **or** `ExecuteAsync` (`Task/ValueTask<ProcessResult<T>>`). Validate contracts on startup and cache invokers. The DataResolver binds parameters by type and optional key/name (`[FromCapabilityData("incoming")]`), with errors for missing/ambiguous inputs. CancellationToken is supplied by the runtime. Native Microsoft DI does not automatically bind arbitrary method arguments.

The Process returns **data + intention**. DataResolver persists named typed output **before** CapabilityResolver follows the current node's intention edge. The Process does not know its next node.

### Process discovery and implicit data injection

There is **no `IProcess` marker interface**. Trusted assemblies explicitly
registered by a module are scanned for concrete classes with names ending in
`Process` and a public `Execute` or `ExecuteAsync` method. At startup the
framework validates the method contracts and builds an invocation descriptor.

Infrastructure dependencies (such as `ILogger<T>`, repositories and HTTP
clients) are injected into the Process constructor by the Host DI provider.
Execution objects are supplied to `Execute`/`ExecuteAsync` by the DataResolver.
**`[FromCapabilityData]` is optional** when the argument type has precisely
one matching datum; when there are multiple matches, use
`[FromCapabilityData("key")]` to select explicitly, with an error on
missing/ambiguous data rather than an arbitrary choice.

Classes are grouped by responsibility, one top-level class per file:
`Arcanum.Framework/{Abstractions,Models,Definitions,Resolution,Hosting,Configuration,Exceptions}`
and `Arcanum.Demo.Api/{Processes,Models,Services}`. Existing CLR namespaces
remain stable, so folder refactoring does not break versioned graph references.

### Intentions and reserved directives

- Local Process intentions: `valid`, `invalid`, `duplicate`, `classified`; lookup is relative to the current Process's transition map.
- Reserved graph targets prefixed by `@`: proposed `@next`, `@complete`, `@stop`, `@fail`; `@retry` is reserved and unsupported in V1. These are **directives at the transition destination**, not general business intentions emitted by Processes.
- The dot `.` qualifies names, e.g. `arca.audit.publish.duplicate`, when needed by observers or inter-Capability contracts, without coupling a reused Process to its owner.
- Unknown intentions/unmapped edges fail safely and are observed, never silently ignored.

## Bounded graph navigation — mandatory protection

**Count Process executions / visited nodes, not only unique paths.** A loop can revisit the same node without increasing distinct paths. `maxSteps` is an **optional positive integer override on each Capability**. The framework provides a configurable `CapabilityOptions.DefaultMaxSteps` (initial default **100**). The effective limit is `Capability.MaxSteps ?? CapabilityOptions.DefaultMaxSteps`. A host may additionally configure an upper bound as a separate safety policy, but an upper-bound rule is **not yet required for V1**.

Invariant for the **effective** limit `N`: at most **N Process invocations** may start. Before starting Process invocation `N+1`, throw `CapabilityExecutionLimitExceededException`, containing sanitized diagnostics such as Capability ID, version, limit and next Process name. The trace records transitions up to the limit without logging sensitive data. A normal terminal after exactly N calls is allowed.

Applies also if a Process routes to itself. Reject invalid/zero/negative overrides and framework defaults at startup; a missing override is valid. Use cancellation/timeout separately to guard a single long-running or blocked Process; the step counter only protects navigation.

Limit-exceeded is a runtime failure, **not** a business intention and **not** an automatic retry. It does not magically reverse previously committed writes: the Unit of Work/transaction boundaries must be explicit, and side-effecting Processes must be idempotent where re-execution is possible. Nested Capabilities can receive their own budgets; a global execution budget across nested invocations is future design if such composition is introduced.

### Runtime outline

```text
plan = CapabilityCatalog.GetValidated(name, version)
resources = current HTTP request scope OR new worker/message scope
data = DataResolver.CreateExecutionScope(input)
steps = 0
maxSteps = plan.MaxSteps ?? frameworkOptions.DefaultMaxSteps
node = plan.Graph.Entry

while node is Process:
    if steps >= maxSteps:
        throw CapabilityExecutionLimitExceededException(...)
    steps++
    process = ProcessResolver.Resolve(node, resources)
    args = DataResolver.Bind(process.ExecutionDescriptor, data)
    (result, intention) = await ProcessResolver.Invoke(process, args)
    DataResolver.Store(data, process.OutputKey, result)
    node = plan.Graph.ResolveTransition(node, intention)
    emit Process/transition telemetry

return transportNeutralCapabilityResult
```

`saveResultAs` / output-key policy must be explicit to avoid collisions where loops revisit a Process. Define whether values are overwritten or retained as history, and which objects remain visible to subsequent steps, before implementation.

## Bidirectional modeling, documentation and generation

The model should be **bidirectional without claiming that code and graphs are independently complete sources of truth**.

### Code → Process model and graph documentation

1. Discover trusted Process classes and their `Execute` signatures, required data types, output types, and declared possible intentions (when available).
2. Generate or validate the Process catalog, method bindings and human-readable documentation.
3. Combine that catalog with **explicitly declared graph transitions** to render executable diagrams and produce reference documentation. Dependencies and signatures alone **cannot infer** the intended business flow.

### Graph → code

1. A designer/AI authors a Capability diagram/JSON, including named Processes, descriptions, expected inputs/outputs and possible intentions.
2. A CLI/generator can scaffold *missing* CLR Process classes, method signatures, data contracts, tests and comments based on those declarations.
3. AI/humans implement the actual behavior. The framework can verify names, signatures and declared intentions, but documentation consistency alone does not prove business correctness.
4. Never silently overwrite hand-written Process logic. Regeneration must be non-destructive and show diffs/conflicts.

### Consistency and source ownership

- **Graph JSON is authoritative for control flow** (entry, intention edges, terminal outcomes, budgets).
- **.NET Process classes are authoritative for executed behavior and method signatures**.
- **Metadata** (`displayName`, `description`, declared intentions) should have one designated owner at a time (e.g. graph JSON for V1) and be checked for drift if mirrored.
- A CI validator checks class references, signatures, input/output binding, target reachability, declared outcome mapping, safety budget, and generated/scaffolded artifact drift.
- Future visual editor and formal two-way synchronization remain separate increments; start with metadata + basic scaffolding.

## Candidate DI containers

Baseline `Microsoft.Extensions.DependencyInjection` for resources, Arcanum DataResolver for Execute injection. Compare Autofac (and DryIoc if useful) with exactly the same scoped UoW, cancellation and concurrency tests before changing the default (#18). No second independent DI root is required by this model.

## Delivery tracking

- #17: initial executable CapabilityResolver, ProcessResolver, DataResolver; JSON graph/step limits; typed results and branching.
- #15: future graph editor/graph-definition validation and advanced execution.
- #18: DI container evaluation.
- Separate issue: bidirectional catalog/code/doc scaffolding and consistency validation.
