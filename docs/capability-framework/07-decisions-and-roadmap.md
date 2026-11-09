# 07 — Decisions, implementation gaps and roadmap

**Status:** design decision log + proposed delivery sequence · 2026-11-09. [Index](README.md).

## Agreed architectural direction

| ID | Decision |
| --- | --- |
| CF-01 | Capability JSON owns graph topology; each Process returns one result `(Data, Intention)` and does not pick successors |
| CF-02 | Distinct Models, NodeTemplates and Node instances; NodeId is stable; NodeExecutionId is runtime-only |
| CF-03 | NodeTemplate describes CLR Process, constructor resources, Execute inputs and typed output, validated on startup |
| CF-04 | Execution variables identified as `(name, type)`; missing data throws; every result mapped to a named output |
| CF-05 | Native .NET reference/value semantics; no implicit isolation of mutable data or DI scoped services |
| CF-06 | `@spawn` on edges supports multiple unconditional parallel continuations; not returned by Processes |
| CF-07 | Dynamic convergence depends on whether running/scheduled Nodes can still reach the waiting Node; divergent work continues |
| CF-08 | Separate `maxSteps`/`maxDepth` and one global timeout with Capability override |
| CF-09 | All detected exceptions logged; first failure cancels immediately; all started work drained; original exception and trail retained |
| CF-10 | Capability `policies` and Microsoft-style Host `Options` have distinct meanings |
| CF-11 | Transversal events/pointcut handlers with global and edge overrides, but no inherited graph segments now |
| CF-12 | `ResponseResolver` reads typed response from Data Context according to Capability output contract |
| CF-13 | More than one terminal response candidate raises exception, including identical HTTP codes |

These are **conceptual directions**, not confirmation that every JSON property, precedence rule or public CLR contract is approved.

## Unsettled implementation details

| Topic | Remains open |
| --- | --- |
| Template contract | Whether constructor `resources` must be written or inferred; allowed Node overrides |
| Data Context | Same name with multiple types, exact vs assignable type matching, explicit nullability |
| Process access | Restriction against direct Data Context access is **proposed**, not separately ratified |
| Convergence | Multiple arrivals and cyclic/re-entrant deadlock analysis |
| Timeout | Default duration and handler-specific deadlines |
| Events/handlers | Concrete pointcut schema, edge transition vs directly activated invocation, add/replace/disable, ordering, failure recursion |
| Error lifecycle | OnError dispatch relative to draining, token/scope, multiple exceptions, response selection |
| Response contract | Structured return type, headers/body, adapter/channels, redirect validation |
| Channels | REST/AMQ/Kafka exposure declaration is a direction without a complete schema |

Resolve these from concrete use cases; do not fill blanks with arbitrary rules.

## Deferred / RFU

- Process-backed `@call`, `@publish`, `@save`, `@load`, `@query`, each with a Model and optional `targeting`.
- `foreignKey` and `externalKey` behavior, automated external-reference mapping.
- More template features, codegen accelerators, advanced pointcuts/segments, custom error handlers.
- Response strategies `additive` and `firstResultAndCancelOthers`.
- Durable Outbox, trail persistence/replay, distributed compensation, editor/graph generation.

## Implementation gap matrix

| Topic | PR #20 executable baseline | Next version |
| --- | --- | --- |
| Nodes | `graph.nodes[id].process` refers to CLR class | `nodes[id].template` + reusable `templates` and `models` |
| Method binding | `[FromCapabilityData]`/parameter name/type fallback | Explicit Node input binding by `(name, type)` |
| Data storage | Dictionary by name; one associated type | Explicit `(name, type)` identity |
| Returned data | `[ReturnName]`/`saveResultAs` | Mandatory named output binding |
| Rules | `MaxSteps` and `MaxDepth` directly on definition | `policies` and global `timeout`; Host `Options` |
| Parallelism | **`@spawn` already implemented** | Preserve existing semantics |
| Convergence | Conservative runtime reachability | Preserve/test current active-path algorithm, including divergence and cycles |
| Responses | `@complete`/`@stop`/`@fail`, same terminal may have multiple arrivals | Explicit response directives and **one candidate only** |
| Response implementation | Capability result + demo HTTP mapping | `ResponseResolver` with Data Context contract |
| Handlers | Error logging/cancellation, no generic pointcuts | Generic event/handler model, introduced by use case |
| Operations | Application CLR Processes | Process-backed Operations, **RFU** |

Current [runtime docs](../capabilities.md) and [run guide](../prototype-implementation.md) are the sources for **implemented** behavior, not this proposal.

## Proposed refactoring responsibilities

| Responsibility | Candidate component |
| --- | --- |
| Validate/prepare Model, Template, Node and policy | Capability Compiler |
| Inspect signatures and cache invoke delegates | Process Compiler |
| Coordinate Capability lifecycle | `CapabilityResolver` |
| Schedule invocations | Execution Scheduler |
| Calculate potential arrivals / convergence | Join Coordinator |
| Read/store typed variables | `DataResolver` |
| Invoke application/generated Processes | Process Runtime |
| Dispatch events and pointcut strategies | Event Dispatcher |
| Cancellation, timeout, drain | Execution Supervisor |
| Resolve response | `ResponseResolver` |
| Capture causal breadcrumbs and Trace | Execution Trail |

These are *responsibility boundaries*, not a required collection of public interfaces/classes. The goal is a smaller, testable resolver.

## Delivery sequence

1. Model/template/node catalogue and startup validation, allowing one Process template to be used in several Nodes.
2. Explicit typed Data Context and compiled delegates; verify CLR mutation/value/return semantics.
3. Extract scheduler and convergence logic, preserve `@spawn`, global completion and causal trail.
4. Global timeout + Capability override, fault supervision and cooperative cancellation.
5. Single-candidate ResponseResolver with typed output from context, conflict exception and adapter mapping.
6. Minimal global `OnError` policy (e.g. `@500`), then pointcuts/edge handlers only when a use case justifies them.
7. Introduce other Operations individually, outside this initial refactor.

## Minimum acceptance suite

- Same template in several Nodes, each NodeExecutionId unique even on revisits.
- Constructor/Execute contract checked before execution; invalid trusted-type mappings rejected.
- Missing or wrong `(name, type)` fails; named outputs replace old typed values.
- Reference mutations and copied value types behave according to .NET semantics.
- Several `@spawn` successors overlap; join waits for possible arrivals; divergent branch runs independently.
- Capability does not return before all started workers settle.
- Step, depth and global timeout independently verified.
- Concurrent exceptions all logged, first immediately cancels, original fault and trail survive.
- Non-cooperative finite worker is awaited; no new work after observed cancellation.
- Response read from declared typed context; multiple terminal candidates throw even with identical status.
- Trace input/output snapshots, sequence/timestamps, method durations and causal invocation IDs verified.
- Handler tests added when implemented: global error response, edge-local pointcut, failure without infinite recursion.

## Documentation ownership

Update this **design folder** as decisions evolve. Update [prototype run instructions](../prototype-implementation.md) **only when runtime code changes**. Promote substantial trade-offs into [ADRs](../adr/0003-capability-templates-and-boundaries.md), recording accepted versus open elements.
