# Capability JSON Specification — Draft 0.4

**Status:** adopted working specification · 2026-11-10.  
**Implementation:** core vNext parser, template validation, named binding, global timeout, implicit response and contract synchronization are implemented in PR #20; extensions labeled **RFU** are not.  
[Documentation index](README.md) · [JSON Schema](../../schemas/capability-json-v0.4.schema.json) · [Executable sample](../../src/Arcanum.Demo.Api/capabilities/demo.capability-json.json).

## 1. Ownership: humans, AI, code and JSON

The **Capability JSON** is the collaborative functional and architectural specification: intent, descriptions, data models, Process roles, bindings, graph and execution policies. Humans and AI should align on this contract before implementing behavior.

The actual **C# constructor and Execute/ExecuteAsync signatures are authoritative for technical contracts**. A signature change can therefore expose drift against the JSON, even when code was changed before the specification.

- At application startup, template contracts are checked against trusted CLR Process signatures; incompatible declarations fail.
- The [ContractSync tool](#8-contract-synchronization-tool) proposes a refreshed JSON description of technical inputs/outputs/resources based on the code.
- The tool **does not silently rewrite** descriptions, functional intentions, graph topology, Node bindings or policies. Such changes require architectural review.
- Infrastructure resources such as `ILogger<T>` need **not** be duplicated in templates. Constructor DI remains supplied by the Host.

## 2. JSON document layout

| Property | Purpose | Runtime status |
| --- | --- | --- |
| `name`, `version`, `displayName`, `description` | Capability identity, version and discussion context | Implemented |
| `models` | Names for concrete CLR models and optional reference metadata | Implemented, reference metadata RFU |
| `contract.inputs` | Request variable name and its Model, payload source or default | Implemented |
| `contract.response` | **One implicit response variable** (name + Model) | Implemented |
| `templates` | Reusable Process description and typed CLR input/output contracts | Implemented |
| `graph.entry`, `graph.nodes`, `graph.edges` | Execution topology, per-Node bindings and `@spawn` | Implemented |
| `policies` | `maxSteps`, `maxDepth`, global `timeout` | Implemented |
| `handlers` | Pointcut-bound cross-cutting strategies | **RFU** — not accepted by executable vNext schema |

Example (the repository contains an [equivalent executable JSON](../../src/Arcanum.Demo.Api/capabilities/demo.capability-json.json)):

```json
{
  "name": "demo.capability-json",
  "version": 1,
  "displayName": "Capability JSON vNext sample",
  "description": "One reusable Process template with two graph Nodes.",
  "models": {
    "DemoInput": {
      "type": "Arcanum.Demo.Api.DemoInput",
      "description": "Typed input and response data."
    }
  },
  "contract": {
    "inputs": {
      "request": { "model": "DemoInput", "source": "$" }
    },
    "response": { "name": "finalEcho", "model": "DemoInput" }
  },
  "templates": {
    "echo": {
      "process": "Arcanum.Demo.Api.EchoActivity",
      "description": "Stateless Execute Process.",
      "inputs": { "request": "DemoInput" },
      "outputs": { "result": "DemoInput" }
    }
  },
  "graph": {
    "entry": "first",
    "nodes": {
      "first": {
        "template": "echo",
        "bindings": {
          "inputs": { "request": "request" },
          "outputs": { "result": "firstEcho" }
        }
      },
      "second": {
        "template": "echo",
        "bindings": {
          "inputs": { "request": "firstEcho" },
          "outputs": { "result": "finalEcho" }
        }
      }
    },
    "edges": [
      { "from": "first", "intention": "echoed", "to": "second" }
    ]
  },
  "policies": {
    "maxSteps": 20,
    "maxDepth": 20,
    "timeout": "PT30S"
  }
}
```

No `processes` section or mandatory `@complete` edge is needed. The same Process implementation is reused through its template; the two Nodes have different identifiers, bindings and runtime invocation identities.

## 3. Models, templates and Nodes

- Each Model alias resolves to one CLR type in a trusted Process assembly's contract.
- `foreignKey` and `externalKey` may appear as descriptive **RFU** metadata, with no current resolution behavior.
- Each template references a registered, trusted CLR Process and describes method inputs by argument name and Model alias. Its single output describes the `T` of `ProcessResult<T>`.
- Each Node references a template and maps all functional method parameters and the single result to **named Data Context variables**.
- The compiler validates every declared Model, method signature, constructor `resources` entry if present, binding and output compatibility at startup. CLR types, not JSON strings, decide technical truth.
- Constructor resources are inferred through Host DI even when their `resources` declaration is omitted (including `ILogger<T>`). Declarative resources are optional documentation/validation; automatically generated full resource metadata is not mandatory.
- General feature/configuration overrides at Node level are **RFU**. The only necessary per-Node overrides now are explicit bindings.

## 4. Data Context

The **variable name is unique** within a Capability invocation. Its associated type is its contract; `(name, type)` is a useful diagnostic description, **not** permission for the same name to identify two independent variables of different types.

- Input resolution uses an **explicit binding name**, never a same-type fallback in vNext.
- Polymorphism is permitted using .NET assignability: a Process argument declared as an interface/base type may receive an appropriately typed concrete variable.
- A missing name or incompatible type throws; a present nullable value, if permitted by its contract, differs from absence.
- A completed Process result is written under its declared output variable; if that name exists, **the value is replaced unconditionally**, subject to the existing variable type contract.
- Passing references/values obeys .NET semantics. Mutations of shared objects become visible immediately; values copied into methods do not auto-update the context, but returned outputs can replace them.
- Parallel Processes share Data Context and DI scope. No automatic copy/lock/isolation is provided; authors are responsible for concurrency safety of objects and resources.
- Direct unrestricted application Process access to Data Context is **not part of the current public Process contract**; the broader policy remains an architectural discussion.

## 5. Edges, convergence and exit

- A Process returns exactly one functional intention. A matching conditional edge routes to at most one successor.
- `@spawn` is an **edge system intention**, active unconditionally regardless of the returned functional intention (including `invalid`). It may occur more than once from a Node and is never returned by Process code.
- In vNext, a Node without a matching business successor can be a normal **leaf**, while its `@spawn` edges still execute. Legacy JSON retains the previous exhaustive mapping rule.
- Convergence waits as long as other running/scheduled work can still reach the Node. When a branch diverges away, it stops delaying that Node, **but continues independently**.
- A Capability completes only after **all already started work settles**, including detached branches. New Nodes are not scheduled after observed cancellation.

See the [parallel executable example](../../src/Arcanum.Demo.Api/capabilities/demo.capability-json-parallel.json).

## 6. Policies, exceptions and timeout

`policies.maxSteps` caps total invocations; `policies.maxDepth` caps causal depth. `policies.timeout` is an ISO 8601 duration (e.g. `PT30S`) applying to the **entire Capability invocation**, overriding the Host's `CapabilityOptions.DefaultTimeout`. The implementation currently defaults to **5 minutes** at Host level (configurable; not a permanent architectural constant).

On exception: log immediately, cancel the shared execution, prevent further scheduling, await all started workers and preserve original fault plus breadcrumbs. Concurrent secondary errors must also be logged. Timeouts emit a `Timeout` breadcrumb and use cooperative cancellation: Processes ignoring the CancellationToken cannot be forcibly terminated.

## 7. Implicit responses; explicit responses RFU

**Implemented now:** if there is **no explicit response announcement**, execute the whole workflow, await its work, then `ResponseResolver` reads the single `contract.response` variable from Data Context by name and declared Model type. It is **not** necessarily the last Process result. The Host adapter translates the structured result to HTTP (current demo uses `@complete` as the internal success terminal).

**RFU:** Nodes may later announce an explicit response destination like `@200`, `@204`, `@302`, `@401`, `@403`, or `@404`. When a response is announced before global completion, record a **deterministic hash of the serialized response contract** at that instant; recompute at finalization, and **abort with a conflict if it changed**. Multiple independently announced responses also conflict by default. Future strategies might choose `exception`, `additive` or `firstResultAndCancelOthers`, but only the no-announcement implicit path is executable now.

The compact notation `{intention}:@302?{url}` is **RFU**; no interpreter or redirect implementation is implied.

## 8. Contract synchronization tool

After building the application/Process assembly:

```bash
dotnet build Arcanum.slnx
dotnet run --project tools/Arcanum.ContractSync -- \
  --assembly src/Arcanum.Demo.Api/bin/Debug/net10.0/Arcanum.Demo.Api.dll \
  --input src/Arcanum.Demo.Api/capabilities/demo.capability-json.json
```

The command prints the **original JSON if aligned** or a **proposed updated JSON** if technical signatures diverge; diagnostic differences are written to stderr. Exit codes: **0** (aligned), **2** (proposal contains changes), **1** (invalid input or tool failure). It does not write to the source file; review the proposal and apply it deliberately.

Existing Model aliases, descriptions and Node bindings are retained wherever possible. Changed CLR argument/result types may require a new Model alias. The tool does **not** infer business intentions or rewrite graph edges. Startup validation is stricter: a mismatched contract fails rather than silently adapting.

## 9. Runtime compatibility and extensions

The current PR also retains legacy PoC JSON with `processes` and nodes referring directly to `process`, legacy `[FromCapabilityData]`/`[ReturnName]`/`saveResultAs`, `maxSteps`/`maxDepth` and terminal `@complete`/`@stop`/`@fail`. Legacy support preserves existing samples/tests while new code adopts vNext.

Generic handlers/pointcuts/edge overrides; Operations `@call`, `@publish`, `@save`, `@load` and `@query`; explicit responses and hash guarding; richer channel declarations; automatic metadata semantics and advanced Node overrides remain **future work**. See [events](04-events-and-handlers.md), [Operations](06-operations-rfu.md), and [roadmap](07-decisions-and-roadmap.md).
