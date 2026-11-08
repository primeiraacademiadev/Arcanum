# Arcanum Framework — Executable Prototype

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

## Async graph and execution trail experiment (November 2026)

Two additional demo Capabilities exercise graph-level fan-out and dynamic joins:

- `demo.parallel`: validation activates two concurrent async Processes; a join fires
  once, after **both** reach it.
- `demo.parallel-choice`: validation activates two branches; a decision may send the
  right branch to an independent terminal instead of the join. The join then waits
  only for branches that can still reach it; the Capability still waits for the
  detached terminal branch before returning.

The new `graph.edges` array declares `{from, intention, to}` edges separately
from Process metadata. Multiple edges may have the same source and intention.
Old `graph.transitions` dictionaries remain supported, but must not coexist with
`edges` in the same Capability. Neither `ProcessResult` nor the Process class
defines paths or asks for parallel execution. There are no `PathActivation`
objects or explicit `Fork`/`Join` nodes.

The resolver schedules each selected node asynchronously, tracks in-flight and
queued work, uses potential reachability for synchronization, and publishes all
successors before checking a join again. A join consumes the available arrivals
once no other in-flight or pending branch can still reach it. The global terminal
result is returned only after all scheduled work has completed. Multiple
incompatible terminal arrivals fail explicitly. `MaxSteps` counts scheduled
Process invocations, including concurrent invocations.

Every successful `CapabilityExecutionResult` exposes an internal
`ExecutionId` and `Breadcrumbs` property with event sequence, timestamps,
elapsed time, thread ID, Process method, Process invocation ID, transitions,
waiting decisions, and duration. The breadcrumb array is intentionally
`[JsonIgnore]` so an HTTP endpoint does not leak diagnostics to clients.
When `ILogger<CapabilityResolver>.IsEnabled(LogLevel.Trace)` is true, Started
and Completed events also include **serialized copies** of Process inputs and
outputs. CancellationToken and injected infrastructure are not captured.
Trace snapshots redact configured property names and discard oversize payloads
rather than leaking partial JSON. Trace is still sensitive operational data:
protect storage and access; field-name redaction is not a universal PII scrubber.

```sh
dotnet test Arcanum.slnx --filter FullyQualifiedName~ParallelExecutionTests
curl -sS http://localhost:5000/capabilities/demo.parallel/execute \\
  -H 'Content-Type: application/json' -d '{"value":"hello"}'
```

Caveats for this PoC: reachability is conservative while a node is executing;
arbitrary cyclic join dependencies still need a formal privacy/topology proof.
The shared execution data scope is written only by the resolver, but mutable
objects passed to concurrent Processes must themselves be treated as immutable
or synchronized. Traces expose scheduler-observed order, not a global physical
clock or deterministic replay. No durable breadcrumb sink has yet been added.
