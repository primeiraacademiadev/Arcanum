# Arcanum — Capability Framework vNext

**Status:** working specification draft 0.3 · 2026-11-09. **Not implemented.**  
**Baseline:** [PR #20](https://github.com/primeiraacademiadev/Arcanum/pull/20), .NET 10 experimental prototype.

This folder is the structured reference for designing the **next** Capability Framework prototype. It records agreed architectural direction, outstanding decisions and Requirements for Future Use (RFU). **JSON examples here are illustrative and must not be copied into the current executable capability directory.**

## Contents

| Section | What it covers |
| --- | --- |
| [01 — Declarative model](01-declarative-model.md) | Models, NodeTemplates, Nodes, contracts, identities, policies |
| [02 — Data Context](02-data-context-and-binding.md) | Named typed variables, bindings, delegates, CLR semantics, DAO/DTO |
| [03 — Graph execution](03-graph-execution.md) | `@spawn`, convergence, supervision, timeouts, trace |
| [04 — Events & handlers](04-events-and-handlers.md) | OnError/OnCompleted/OnFinally, pointcuts, edge override |
| [05 — ResponseResolver](05-response-resolution.md) | Typed outputs, terminal candidates, competing responses |
| [06 — Operations RFU](06-operations-rfu.md) | Process-backed `@call`/`@publish`/`@save`/`@load`/`@query` |
| [07 — Decisions & roadmap](07-decisions-and-roadmap.md) | Agreed/proposed/deferred, migration matrix, acceptance criteria |
| [Glossary](glossary.md) | Shared vocabulary |
| [ADR-0003](../adr/0003-capability-templates-and-boundaries.md) | Architectural rationale |

## Status language

- **Agreed direction:** discussed and retained as a design constraint; does not imply implemented.
- **Proposed:** illustrative schema, recommendation or mechanism requiring a further decision.
- **RFU/deferred:** preserve for a future concrete use case; **do not implement now**.
- **Implemented (PR #20):** exists in the experimental code and tests.

## Invariants

1. One versioned Capability contains a graph. Nodes have stable `NodeId` and reference reusable NodeTemplates, not the CLR class directly.
2. A Process returns one `ProcessResult<T>(Data, Intention)`; it never chooses its next Node.
3. `NodeExecutionId` identifies a single runtime invocation, never appears in JSON.
4. `@spawn` is a reserved **edge system intention**, never a Process intention; successors are scheduled asynchronously.
5. Convergence waits only while another running/scheduled execution **can still reach** the waiting Node. Divergent work continues.
6. Data and DI scope are shared; **interference on mutable objects is accepted**.
7. Every detected exception is logged and immediately cancels execution; all started work is awaited before returning/throwing.
8. Output is resolved from a declared named, typed variable in Data Context; **multiple terminal response decisions are an error** in vNext.
9. `policies` govern Capability behavior; .NET `Options` configure Host defaults.
10. Cross-cutting handlers can have global pointcuts and edge overrides, but **no graph segments** in the next iteration.

The [current prototype documentation](../capabilities.md) and [execution instructions](../prototype-implementation.md) describe what **already works**. The [gap matrix](07-decisions-and-roadmap.md#implementation-gap-matrix) distinguishes vNext design from that code.
