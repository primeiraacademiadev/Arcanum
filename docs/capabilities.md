# Arcanum — Capabilities, Processes and Graph Runtime


> **Current implementation (PR #20), not vNext.** The proposed NodeTemplate/Model
> architecture, explicit typed Data Context, policies, transversal handlers and
> ResponseResolver are documented separately in the
> [Capability Framework vNext specification](capability-framework/README.md).
> Do not apply those illustrative JSON examples to the current runtime.


Status: **experimental runtime prototype** · updated 2026-11-08. The graph and Process contracts below describe the current PoC, not a production workflow service.

## Design principles

Arcanum describes a Capability as a versioned executable **process graph** in the same solution as the .NET code, to make AI-assisted development traceable and understandable.

- **Process identity**: CLR `Type.FullName`, resolved only in trusted assemblies and declared in the Capability catalog.
- **Node identity**: a unique graph-local `NodeId`; several nodes may reference the same Process class.
- **Execution identity**: `NodeExecutionId` is generated per invocation **by the runtime** and never declared in graph JSON.
- **Presentation**: Process `displayName` and `description` describe code; nodes select the implementation.
- **Topology**: `graph.nodes` and `graph.edges` are independent declarations, not edges embedded in Processes or nodes.
- **Intentions**: `ProcessResult<T>` has a single intention; each node permits at most one conditional edge for that intention.
- **Parallelism**: edges whose `intention` is the reserved system value `@spawn` are activated independently of the Process's business intention. This is graph-directed: Processes never return `@spawn`.
- **Limits**: `maxSteps` caps total invocations; `maxDepth` caps the longest causal chain. They are different budgets.
- **Scope**: execution data and DI scope are shared across parallel Processes. Mutable-object interference is an **accepted, documented effect**, not prevented by the framework.
- **Source of truth**: JSON owns graph connectivity, CLR Process methods own behavior; both are versioned together.

## Example JSON — Processes separated from graph

```json
{
  "name": "demo.parallel",
  "displayName": "Execução paralela com convergência",
  "version": 1,
  "contract": {
    "inputs": {
      "request": {
        "type": "Arcanum.Demo.Api.DemoInput",
        "source": "$"
      }
    },
    "responses": {
      "@complete": {
        "name": "combined",
        "type": "System.String"
      },
      "@stop": {
        "name": "validated",
        "type": "Arcanum.Demo.Api.ValidationData"
      }
    }
  },
  "processes": {
    "Arcanum.Demo.Api.ValidateProcess": {
      "displayName": "Validar"
    },
    "Arcanum.Demo.Api.ParallelStartProcess": {
      "displayName": "Iniciar passo"
    },
    "Arcanum.Demo.Api.ParallelAlphaProcess": {
      "displayName": "Alpha"
    },
    "Arcanum.Demo.Api.ParallelBetaProcess": {
      "displayName": "Beta"
    },
    "Arcanum.Demo.Api.ParallelJoinProcess": {
      "displayName": "Convergir"
    }
  },
  "graph": {
    "entry": "validate",
    "nodes": {
      "validate": {
        "process": "Arcanum.Demo.Api.ValidateProcess"
      },
      "fork": {
        "process": "Arcanum.Demo.Api.ParallelStartProcess"
      },
      "alpha": {
        "process": "Arcanum.Demo.Api.ParallelAlphaProcess"
      },
      "beta": {
        "process": "Arcanum.Demo.Api.ParallelBetaProcess"
      },
      "join": {
        "process": "Arcanum.Demo.Api.ParallelJoinProcess"
      }
    },
    "edges": [
      {
        "from": "validate",
        "intention": "valid",
        "to": "fork"
      },
      {
        "from": "validate",
        "intention": "invalid",
        "to": "@stop"
      },
      {
        "from": "fork",
        "intention": "ready",
        "to": "alpha"
      },
      {
        "from": "fork",
        "intention": "@spawn",
        "to": "beta"
      },
      {
        "from": "alpha",
        "intention": "done",
        "to": "join"
      },
      {
        "from": "beta",
        "intention": "done",
        "to": "join"
      },
      {
        "from": "join",
        "intention": "finished",
        "to": "@complete"
      }
    ]
  }
}
```

This is an **illustrative fragment**; actual audit ingestion also needs producer authentication, deduplication, conflict branches and integration tests. A compact form or generated Process metadata may reduce JSON duplication later.

### Node and execution identity

- `processes` keys are full CLR class names and are the only types the loader may register.
- `graph.nodes` keys are **stable, unique NodeIds** in the Capability, associated with `process` and optional per-node `saveResultAs`.
- `graph.entry` and the `from`/`to` endpoints in `graph.edges` reference those IDs. Reserved `@complete`, `@stop`, `@fail` are terminal destinations.
- Business `intention` edges are unique for each `(from, intention)` pair. The reserved system intention `@spawn` may appear on multiple edges from the same node and launches each corresponding destination in parallel with the selected business continuation.
- Neither `NodeExecutionId` nor `PathActivation` belongs to the graph specification. The resolver assigns a new invocation ID on each visit and records causal links in breadcrumbs.
- Multiple visits to a node, including loops, are allowed and count separately against both depth and total-step budgets.

## Resolvers

| Component | Responsibilities |
| --- | --- |
| `CapabilityResolver` | Schedule graph-local nodes asynchronously, coordinate outstanding work and potential joins, enforce depth/step limits, propagate cancellation and preserve execution breadcrumbs |
| `ProcessResolver` | Map trusted `Type.FullName` to CLR type in explicitly registered assemblies, instantiate via the current Host DI scope, cache method invocation descriptors |
| `DataResolver` | Bind named typed execution data to `Execute`/`ExecuteAsync` arguments and store typed Process results in a per-invocation DataScope |

### Execution and resource scopes

Host infrastructure lives in **one .NET DI root**, with an appropriate resource scope per invocation. For HTTP, normally use `HttpContext.RequestServices`; for Workers, create a new scope per message. All Processes in one Capability execution use the same scope for Unit of Work consistency.

Execution data lives in a shared **logical runtime context** per Capability invocation. It contains named data and the runtime `CancellationToken` (not as a named business value and never in JSON). The resolver is the sole writer of named results, but parallel Processes can receive references to the **same mutable objects** and the same scoped resources. This interference is accepted: the framework makes no implicit copies, locks, isolation or thread-safety guarantees for business data and injected services. Process authors must evaluate the consequences. Trace breadcrumbs help diagnose overlap.

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

### Process registration, data bindings and named results

The **Capability JSON catalog is the only list of executable Process classes**.
On boot, the framework loads that JSON and resolves the referenced full CLR
type names **only inside explicitly trusted assemblies**. Each referenced class
must be public, concrete and have exactly one valid `Execute` or
`ExecuteAsync` method. Its invocation descriptor is compiled at startup.
**No `IProcess`, suffix convention or attribute is mandatory.** An optional
`[Process]` class attribute exists for visual documentation only. Classes
not referenced by JSON are not registered even if they have an Execute method.

Resources including `ILogger<T>` are constructor-injected from the Host
resource scope. Typed execution data bind to method parameters by **parameter
name first**, falling back to the type when exactly one compatible value
exists. `[FromCapabilityData("key")]` overrides this selection explicitly.

A method may declare `[ReturnName("validated")]` to name the result saved in
the execution DataScope; the Capability may override that name using
`saveResultAs`. These names also resolve the declared terminal response.

### Capability contracts — data conversion is not endpoint responsibility

Each Capability JSON has a `contract` independent of the transport:

```json
{
  "contract": {
    "inputs": {
      "request": {
        "type": "Arcanum.Demo.Api.DemoInput",
        "source": "$"
      }
    },
    "responses": {
      "@complete": {
        "name": "final",
        "type": "Arcanum.Demo.Api.CompletedData"
      }
    }
  }
}
```

- `inputs` declares typed, named execution objects; sources support `$`,
  simple dotted JSON paths such as `$.payload`, and constant `default`
  values used for initialization.
- `responses` declares the name and type of the result delivered for each
  terminal direction, preventing the endpoint from choosing arbitrary
  intermediate data.
- `CapabilityContractResolver` binds JSON into trusted typed inputs;
  `CapabilityJsonDispatcher` starts execution; the generic HTTP adapter
  translates terminal semantics into HTTP responses.
- Declared input types must already occur in referenced Process signatures;
  unknown types or invalid bindings are rejected. The HTTP adapter does not
  instantiate arbitrary CLR types from untrusted strings.
- Contract validation is currently structural/type oriented; it does not yet
  provide all static dataflow analysis, arbitrary field transformations,
  custom converters or endpoint-specific security policy enforcement.

Classes are grouped by responsibility, one top-level class per file:
`Arcanum.Framework/{Abstractions,Models,Definitions,Resolution,Hosting,Configuration,Exceptions}`
and `Arcanum.Demo.Api/{Processes,Models,Services}`. Existing CLR namespaces
remain stable so folder refactoring does not break graph references.

### Intentions and reserved directives

- Local Process intentions: `valid`, `invalid`, `duplicate`, `classified`; each outcome matches at most one conditional edge from the current **NodeId**. Graph-level `@spawn` edges are unconditional and independent of this result.
- Reserved **edge intention**: `@spawn` (system-controlled parallel activation; never a `ProcessResult.Intention`). Reserved **destinations**: `@complete`, `@stop`, `@fail`. Other `@` forms are invalid. These are **directives at the transition destination**, not general business intentions emitted by Processes.
- The dot `.` qualifies names, e.g. `arca.audit.publish.duplicate`, when needed by observers or inter-Capability contracts, without coupling a reused Process to its owner.
- Unknown intentions/unmapped edges fail safely and are observed, never silently ignored.

## Execution, completion and budgets

`maxSteps` (or `CapabilityOptions.DefaultMaxSteps`, default 100) caps **total scheduled Process invocations** across all branches. `maxDepth` (or `CapabilityOptions.DefaultMaxDepth`, default 100) independently caps **causal depth**, starting at 1 for the entry; for a join use the maximum depth of its arrivals plus one. Neither limit implies isolation or eliminates every possible waiting deadlock.

The `CapabilityResolver` schedules all eligible outgoing edges asynchronously after storing the completed Process output. A node with one incoming edge can run immediately; a node with several incoming edges waits while any other **pending or running** execution can still reach it. When an alternative path becomes impossible, the wait is recalculated. The resolver follows all started work and **never returns a Capability result before its running and queued work settles**. Concurrent terminal arrivals must agree on the terminal directive. If their intentions differ, the successful Capability result carries a null Intention (there is no single answer) while the trail records all terminal transitions.

The linked `CancellationToken` is held in the runtime data scope, supplied to Process methods, checked before **each** new invocation and again **after** each completed invocation before scheduling successors. When any Process throws, its worker immediately cancels the linked token and logs the exception. The resolver stops scheduling successors, logs its own failures, and awaits every already-started worker before rethrowing; independent sibling exceptions are logged individually. Unknown/invalid graph intentions also cause cancellation. A non-cooperative Process may therefore delay cancellation indefinitely; the framework cannot forcibly stop user code. No external side effect is rolled back automatically.

`ExecutionTrail` records the scheduler-observed order, node and invocation IDs, start/end time and elapsed duration, thread ID, causal transitions, join waits, failures and cancellation. At effective `Trace` level it snapshots serializable business method inputs/outputs with configured name-based redaction and size cap. It does **not** serialize injected infrastructure or the token. On success the breadcrumbs are attached to the result with `[JsonIgnore]`; on exception they are available as `exception.Data["Arcanum.Breadcrumbs"]`, with `Arcanum.ExecutionId` for correlation. Every runtime exception handled by the resolver is logged with its exception object; errors during Trace snapshot serialization are also logged. These diagnostics are not a globally synchronized physical clock.

No special Fork/Join/Path objects or `PathActivation` are part of the graph model; outbox-based publish, operation-specific integration executors, durable execution trails and distributed replay are not implemented in this PoC.

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
