# ADR-0003: NodeTemplates and separation of Capability runtime responsibilities

**Status:** accepted in concept; detailed schema and event/response contracts remain **proposed** · 2026-11-09.

## Context

PR #20 proves graph-local NodeId, `@spawn`, dynamic convergence, shared Data Context, cancellation and breadcrumbs. The current resolver has many responsibilities and graph nodes still reference CLR Process classes directly. Reusability and early validation require an explicit Process contract that can be referenced by several Nodes.

## Agreed architectural direction

1. Separate `models`, `templates`/NodeTemplates and `graph.nodes`. Keep Node identity distinct from implementation identity, validate CLR constructor/Execute contracts during preparation, and map outputs by name.
2. Use named typed `(name, type)` execution data and native .NET reference/value semantics; no automatic concurrency isolation.
3. Retain graph-controlled `@spawn`. Future `@call`/`@publish`/`@save`/`@load`/`@query` are Process-backed and **RFU**, not a second execution engine.
4. Distinguish per-Capability `policies` from .NET Host `Options`; add global deadline with Capability override.
5. Introduce transversal event/handler pointcuts with edge override support but **no implicit graph segments**.
6. Delegate response construction to `ResponseResolver` based on the typed Data Context; multiple terminal candidates or independent direct producers of the response result in an exception. An explicit Process may aggregate intermediate data.

## Rationale

NodeTemplates reduce repetition, allow declarative and reflective contract checks, and provide a future place for narrowly controlled per-Node overrides. Separating the scheduler, supervision, event handling and response calculation should make concurrency and lifecycle behavior independently testable.

## Consequences / unresolved

- This is a **new schema**, not accepted by existing PR #20 code: do not rewrite executable example JSON until a migration exists.
- No untrusted arbitrary CLR loading, no direct runtime NodeExecutionId in JSON and no silent response merging.
- Details still open: mandatory `resources` metadata, exact `(name, type)` lookup, direct context access, handler pointcut semantics/order, timeout defaults, response wire contract, cyclic join edge cases.
- No implementation of future Operations or custom handlers is authorized by this ADR alone.

## Alternatives

Direct Node → Process association is simpler now but less reusable; retained as **current PoC only**. A separate Operation engine is rejected for now. Implicit inherited handler segments and first/last result selection are not part of vNext.

See [vNext specification](../capability-framework/README.md) and [open questions](../capability-framework/07-decisions-and-roadmap.md).
