# Arcanum Framework — Executable Prototype


> This page documents the executable **PR #20** code. The next architecture
> iteration is a distinct [Capability Framework vNext working specification](capability-framework/README.md).
> Its NodeTemplate, policy, handler and ResponseResolver syntax is **not yet implemented**.


Status: implementation on `feat/capability-framework-poc`. Small technical PoC, **not** the Audit Concentrator business implementation.

## Run locally

Install the .NET 10 SDK.

```sh
dotnet restore Arcanum.slnx
dotnet build Arcanum.slnx
dotnet test Arcanum.slnx
dotnet run --project src/Arcanum.Demo.Api
```

Available endpoints after the API starts:

```http
GET /health

POST /capabilities/demo.publish/execute
Content-Type: application/json

{ "value": "  hello  " }
```

Expected HTTP 200 with `terminal: "@complete"`, `steps: 3`, and a typed final result whose text is `HELLO`. Sending `{"value":""}` returns HTTP 422 with `terminal: "@stop"`.

```http
POST /capabilities/demo.loop/execute
Content-Type: application/json

{ "value": "ok", "iterations": 100 }
```

This graph calls the same stateless `CounterProcess` repeatedly and throws `CapabilityExecutionLimitExceededException` before Process invocation #5 (override `maxSteps = 4`). The exception is an engine failure, not a business intention. For successful termination, use `iterations: 4`. `demo.default-loop` omits `maxSteps` and inherits the framework default, configurable through `Arcanum__DefaultMaxSteps`.

## Implemented contracts

- JSON Capability catalog with distinct `processes` metadata and compact graph `entry` / `transitions`.
- Full CLR Process names as identities, no manual aliases or attribute registration.
- `displayName`/`description` metadata, `[ReturnName]` for output naming and optional `saveResultAs` overrides.
- `ProcessResolver`: classes explicitly referenced by Capability JSON, resolved only inside trusted assemblies; constructor injection from the existing resource scope.
- `DataResolver`: invocation-isolated typed objects bound to `Execute` / `ExecuteAsync` arguments; outputs replace same-typed named values on revisited nodes.
- `CapabilityResolver`: process execution, local intentions, terminal `@complete/@stop/@fail`, trace/log metadata and an optional `maxSteps` override of `CapabilityOptions.DefaultMaxSteps`.
- Startup validation for type references, process method contracts and graph transitions; cancellation and resource-scope sharing.

## Scope and boundaries

The demo is intentionally **not** secure for arbitrary remote use. Do not expose it publicly: there is no authentication on the demo endpoint. The HTTP host only permits built-in Capability IDs beginning with `demo.`; no JSON upload or arbitrary type resolution exists.

The PoC does **not** implement: audit persistence, outbox, real LoA/RBAC, IdP, Kafka, OPA, encrypted records, graphical editor, code generation, or distributed transactions. The framework retains a single Host DI root and a separate execution-data registry.

## CI

Every PR triggers `.github/workflows/pr-verify.yml`: restore, Release build and xUnit tests. The implementation was authored in an environment without an installed .NET SDK, so use this pipeline to confirm compiler/test results before merge.

## Process registration — JSON is the source of truth

`AddArcanumFramework(assembly)` declares the trusted assemblies. The framework
does **not** scan classes by suffix or interface: `AddArcanumCapabilitiesFromDirectory`
reads each JSON, resolves the fully qualified CLR classes named in its
`processes` catalog, registers only those types, validates their unique
`Execute`/`ExecuteAsync` signatures and prepares cached invocation delegates.

There is no `IProcess`. A class may have any name; `[Process]` is an optional
documentation marker. `EchoActivity` demonstrates execution without marker,
interface or suffix. `UnlistedActivity`, despite having `Execute`, is not
registered because no Capability references it.

Process classes live in `Processes/`, data types in `Models/`, infrastructure
dependencies in `Services/`, one class per file.

```csharp
[Process] // Optional: visual documentation, not used for discovery
public sealed class ValidateProcess(
    IExecutionProbe probe,
    ILogger<ValidateProcess> logger)
{
    [ReturnName("validated")]
    public ProcessResult<ValidationData> Execute(DemoInput request)
    {
        logger.LogInformation("Validating input in scope {ScopeId}", probe.ScopeId);
        return new(
            new ValidationData(request.Value, probe.ScopeId),
            string.IsNullOrWhiteSpace(request.Value) ? "invalid" : "valid");
    }
}
```

Constructor arguments come from Host DI, `Execute` arguments from the
per-invocation Data Scope. The resolver looks up **the parameter name first**,
then falls back to a **unique compatible type**, and finally uses the
optional `[FromCapabilityData("key")]` override if supplied.

## Declarative Capability contract

The host declares transport-neutral input and output bindings in the Capability
JSON instead of mapping a `DemoInput` or creating `ExecutionDatum` inside
the endpoint:

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
      },
      "@stop": {
        "name": "validated",
        "type": "Arcanum.Demo.Api.ValidationData"
      }
    }
  }
}
```

`demo.loop` declares `counter` with a constant default `{"count":0}`;
`demo.nested` uses `"source":"$.payload"` to bind an embedded object.

The `CapabilityContractResolver` converts the input JSON into typed named
execution values, using only data types declared by trusted Process signatures.
`CapabilityJsonDispatcher` executes the Capability and selects declared
outputs, and the HTTP adapter maps terminal semantics to HTTP responses.

```csharp
app.MapArcanumCapabilityEndpoint(namePrefix: "demo.");
```

No endpoint-specific DTO translation or extra per-Capability mapping code is
required. The PoC supports body root `$`, simple property paths `$.name`,
constant defaults and typed terminal results; complex mappings, source
converters, authentication and automatic OpenAPI generation are future work.

## Async graph execution and diagnostics (November 2026)

The experimental JSON schema distinguishes **Process classes** in `processes`
from **graph-local node IDs** in `graph.nodes`. The `graph.entry` and each
`graph.edges` endpoint use those IDs; a CLR Process class may be referenced by
several distinct nodes and a node may be visited repeatedly. `NodeExecutionId`
is generated per invocation at runtime, never declared in the graph.

The `graph.edges` collection permits one conditional edge per `(from, intention)`
pair. **Duplicate business intention edges are invalid.** A separate edge such as
`{"from":"fork","intention":"@spawn","to":"beta"}` uses the reserved **system
intention** `@spawn`. It is unconditional and activates independently of the
single business intention in `ProcessResult`. Multiple `@spawn` edges may
originate from a node, and none may target a terminal directive. There are no special Fork/Join/Path objects
or PathActivation records. All outgoing scheduling occurs asynchronously.

- `demo.parallel`: validation selects a single continuation into a fan-out
  step. One conditional and one unconditional parallel edge run two async
  Processes; a convergent node executes once after both reach it.
- `demo.parallel-choice`: one parallel branch conditionally stops reaching
  the join and follows a detached terminal path. The join can proceed while
  that path is active, **but the Capability still waits for the detached Process**
  before returning.

The resolver keeps all scheduled/running work in its own execution context.
For a convergent node, it analyses whether other pending/running node invocations
can still reach it. New successors are published before recalculating joins.
Both successful completion and failure/cancellation handling await all started
Process invocations. When any Process throws, its worker **immediately signals
cancellation**, preventing further graph propagation. All Process exceptions
(including those thrown by siblings while cancellation is in progress) are
logged individually, as are scheduler failures. Trace snapshot serialization
exceptions are logged too, even when the snapshot is omitted. On cancellation, a Process that ignores its token can hold
up completion indefinitely. Multiple terminal arrivals with conflicting directives fail. If they reach the
same directive with different intentions, the Capability's singular Intention
is null and all individual intentions remain visible in its Execution Trail.

The runtime holds a linked `CancellationToken` in its `ExecutionDataScope`,
but **not** in the business data dictionary or the declarative input contract.
It is injected into `ExecuteAsync` parameters and checked before every
scheduled invocation and again after each completion. `maxSteps` counts all
Process invocations; `maxDepth` independently caps causal graph depth,
including loops. Neither limit proves that arbitrary cyclic joins cannot
deadlock.

Business inputs and DI scoped resources are shared by parallel Processes,
**without copies, locking or implicit isolation**. Interference and
nondeterministic mutations are **accepted properties** of this prototype,
to be understood by Process implementers; snapshots in Trace can reveal them.

`ExecutionTrail` is append-only and thread-safe. Its breadcrumbs contain
NodeId, NodeExecutionId, causal source invocation ID, sequence, timestamps,
elapsed duration, thread ID, method, transitions, wait/ready status and outcomes.
At effective `ILogger<CapabilityResolver>.IsEnabled(LogLevel.Trace)`, it also
captures redacted, size-bounded serialized input/output snapshots (not injected
resources). Success results hold the trail internally with `[JsonIgnore]`;
failures expose the trail through `exception.Data["Arcanum.Breadcrumbs"]`
and the run ID through `exception.Data["Arcanum.ExecutionId"]`.
No persistent trail sink is implemented.

```sh
dotnet test Arcanum.slnx --filter FullyQualifiedName~ParallelExecutionTests
curl -sS http://localhost:5000/capabilities/demo.parallel/execute \\
  -H 'Content-Type: application/json' -d '{"value":"hello"}'
```

Outbox-backed publishing and framework-generated integration executors were
discussed separately, but are **not** implemented by this graph runtime change.
