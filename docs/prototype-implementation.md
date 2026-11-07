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
- `displayName`, `description` and `saveResultAs` metadata for readable execution and typed data binding.
- `ProcessResolver`: discovered/whitelisted `IProcess` classes in explicitly registered assemblies; constructor injection from the existing resource scope.
- `DataResolver`: invocation-isolated typed objects bound to `Execute` / `ExecuteAsync` arguments; outputs replace same-typed named values on revisited nodes.
- `CapabilityResolver`: process execution, local intentions, terminal `@complete/@stop/@fail`, trace/log metadata and an optional `maxSteps` override of `CapabilityOptions.DefaultMaxSteps`.
- Startup validation for type references, process method contracts and graph transitions; cancellation and resource-scope sharing.

## Scope and boundaries

The demo is intentionally **not** secure for arbitrary remote use. Do not expose it publicly: there is no authentication on the demo endpoint. The HTTP host only permits built-in Capability IDs beginning with `demo.`; no JSON upload or arbitrary type resolution exists.

The PoC does **not** implement: audit persistence, outbox, real LoA/RBAC, IdP, Kafka, OPA, encrypted records, graphical editor, code generation, or distributed transactions. The framework retains a single Host DI root and a separate execution-data registry.

## CI

Every PR triggers `.github/workflows/pr-verify.yml`: restore, Release build and xUnit tests. The implementation was authored in an environment without an installed .NET SDK, so use this pipeline to confirm compiler/test results before merge.
