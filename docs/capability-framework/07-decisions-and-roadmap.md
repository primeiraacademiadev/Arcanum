# 07 — Decisions, implementation status and roadmap

**Status:** Capability JSON **Draft 0.4**, 2026-11-10 · [index](README.md) · [executable specification](08-capability-json-v0.4.md).

## Agreed architecture and current implementation

| Decision | Architectural direction | Prototype status |
| --- | --- | --- |
| CF-01 | Capability JSON is human/AI functional specification; **C# contracts authoritative** for technical signatures | Validated at startup |
| CF-02 | Separate Models, reusable NodeTemplates, and graph-local Node instances | **Implemented**, legacy schema remains accepted |
| CF-03 | Unique data **name** with associated CLR type, typed binding, polymorphic assignability and unconditional compatible replacement | **Implemented** in vNext |
| CF-04 | Explicit input and output names; no vNext type-only fallback | **Implemented** |
| CF-05 | One `ProcessResult<T>(Data, Intention)` per Process, compiled CLR invocation delegates | **Implemented** |
| CF-06 | Unconditional `@spawn` edge(s), independent of returned intention | **Implemented** |
| CF-07 | Join waits for all **possibly reaching** active/scheduled work; divergent work continues | **Implemented** with JoinCoordinator, cyclic behavior remains open |
| CF-08 | Distinct `maxSteps`/`maxDepth` and a single global timeout overridable per Capability | **Implemented**; Host timeout default is configurable |
| CF-09 | All detected exceptions logged; immediate cancellation, drain started tasks and preserve original fault + Execution Trail | **Implemented** |
| CF-10 | Capability `policies` vs Microsoft-style Host `Options` | **Implemented** |
| CF-11 | Response from the declared named typed Data Context variable **after the whole implicit workflow finishes** | **Implemented** in ResponseResolver |
| CF-12 | Contract synchronizer proposes JSON based on CLR signature drift without rewriting behavior | **Implemented**, non-destructive CLI |
| CF-13 | Events/handlers/pointcuts with edge overrides and **no inherited segments** | **Design direction**, **not implemented** |
| CF-14 | Announced responses: hash at announcement, rehash at conclusion, conflict on change or multiple response announcements | **RFU**; no early responses in vNext |

## Implementation gap matrix

| Topic | Legacy PoC | Draft 0.4 executable |
| --- | --- | --- |
| Process metadata | `processes` keyed by CLR class | `models` + `templates` keyed by logical aliases |
| Node declaration | `node.process` and optional `saveResultAs` | `node.template` + `bindings.inputs/outputs` |
| Input resolution | Parameter-name/attribute/compatible-type fallback | Exact name from Node binding; type-checked with CLR polymorphism |
| Results | `[ReturnName]` and Process metadata | Required named typed result binding |
| Capability response | `contract.responses` keyed by `@complete`/`@stop` | One `contract.response` read after global completion |
| Graph termination | Mandatory legacy terminal edge | Ordinary leaf Nodes, no `@complete` edge required |
| Execution rules | Definition-level `maxSteps`/`maxDepth` | `policies.maxSteps`/`maxDepth`/`timeout` |
| Timeout | No deadline | Global cooperative deadline and Timeout breadcrumb |
| Contract sync | Manual | Non-destructive `tools/Arcanum.ContractSync` and reflection validation |
| `@spawn`/joins | Already implemented | Preserved, tested in vNext fan-out and dynamic join |
| Handlers and other Operations | Not implemented | **RFU**, not silently recognized |
| Early response hash | Not implemented | **RFU**, only implicit response executes |

## Still to design

- The exact public contract for a future **Node feature override** (other than bindings), legal merge rules and resource metadata evolution.
- Nullability and optional inputs, and edge cases involving same name with a polymorphic declared type.
- Formal treatment of **cyclic joins/re-entrant concurrent arrivals**; `maxDepth` is not a deadlock proof.
- A per-event handler strategy, pointcut expression language, ordering and failure lifecycle. Local edge override semantics remain deliberately restricted to **no segments**.
- Explicit response announcement syntax, canonical serialization and hash, multiple announced response conflicts and alternative strategies `additive`/`firstResultAndCancelOthers`.
- Channel specification (REST/AMQ/Kafka), richer adapter contracts, redirect safety, serialization formats.
- Complete declaration/semantics of `foreignKey` and `externalKey`.
- Future Process-backed Operations `@call`, `@publish`, `@save`, `@load`, `@query`, each requiring a Model and possibly a `targeting` Data Context variable.

## Validation and tests

The executable core has dedicated tests for:
- Template reuse under separate Node IDs, CLR signature mismatch rejected before execution.
- Explicit name resolution, name uniqueness, polymorphic read and replacement.
- Implicit response from context with no terminal edge.
- Global timeout override and cancellation diagnostics.
- Two `@spawn` edges, dynamic join and draining a detached branch.
- Non-destructive C# → JSON contract synchronization and preservation of functional topology.
- Existing legacy tests: step/depth budgets, trace redaction, concurrent failures, legacy terminal behavior.

Run:

```bash
dotnet restore Arcanum.slnx
dotnet build Arcanum.slnx
dotnet test Arcanum.slnx
dotnet run --project src/Arcanum.Demo.Api
```

CI includes Build/Test/HTTP smoke. See [prototype instructions](../prototype-implementation.md).

## Proposed next increments

1. **Harden Draft 0.4:** nullability contract, typed-context invariants, more polymorphic and cyclic-join tests, schema validation in CI.
2. **Reduce resolver responsibilities:** continue extracting supervision, typed binding/compilation and scheduler behavior behind testable boundaries where appropriate.
3. **Handlers:** implement only a concrete initial global `OnError` strategy and then use a real notification case to decide edge pointcuts and composition.
4. **Explicit responses:** define announcement syntax and canonical response hash, then validate conflict behavior.
5. Add **Process-backed Operations** only once a concrete need determines their contract.

These are suggested priorities, not authorization to implement the RFU features now.

## Related exploration

The future [GUI prototyping with architectural strategies](../ideas/gui-prototyping-architecture.md) is tracked in [issue #21](https://github.com/primeiraacademiadev/Arcanum/issues/21) and does not change Capability JSON 0.4 scope.
