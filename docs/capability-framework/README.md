# Arcanum — Capability Framework vNext

**Status:** Capability JSON Draft 0.4 · 2026-11-10. **Core executable; future extensions RFU.**  
**Baseline:** [PR #20](https://github.com/primeiraacademiadev/Arcanum/pull/20), .NET 10 experimental prototype.

This folder documents **Capability JSON 0.4**. The [executable subset](08-capability-json-v0.4.md) and [JSON Schema](../../schemas/capability-json-v0.4.schema.json) are implemented in PR #20. Examples proposing handlers, explicit responses and future Operations remain **RFU and not executable**.

## Contents

| Section | What it covers |
| --- | --- |
| **[08 — Capability JSON 0.4](08-capability-json-v0.4.md)** | **Executable syntax, example, synchronization tool and scope** |
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
- **Implemented (PR #20):** exists in code and tests; core Draft 0.4 is supported alongside legacy JSON.

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

The [prototype documentation](../capabilities.md) and [execution instructions](../prototype-implementation.md) describe both legacy and Draft 0.4 executable behavior. The [gap matrix](07-decisions-and-roadmap.md#implementation-gap-matrix) distinguishes implementation from future work.
