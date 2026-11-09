# Glossary — Arcanum Capability Framework

[Index](README.md).

| Term | Definition |
| --- | --- |
| Capability | Versioned declarative directed execution graph |
| Model / ModelName | Local alias of concrete .NET type, with possible future metadata |
| NodeTemplate / TemplateId | Reusable description of Process implementation, constructor and Execute contract |
| Process | CLR implementation exposing one valid `Execute` or `ExecuteAsync` |
| Node / NodeId | Stable graph-local instance referring to a NodeTemplate |
| ExecutionId | One complete Capability invocation |
| NodeExecutionId | One concrete runtime Node invocation, including loop revisits |
| Intention | One functional routing outcome returned with Process data |
| `@spawn` | Reserved edge system intention activating unconditional parallel work |
| Data Context | Shared named typed variables for an invocation |
| `(name, type)` | Conceptual vNext variable identity |
| Resources | Host DI constructor dependencies |
| Output binding | Destination name/type for Process result |
| Convergence | Wait while any other active/scheduled invocation could still reach a Node |
| Policies | Per-Capability behavioral rules such as limits and global timeout |
| Options | Host/framework settings and defaults following Microsoft .NET convention |
| Handler / pointcut | Transversal strategy and the event/context selection governing its application |
| Response candidate | One terminal decision, not yet a completed response |
| ResponseResolver | Resolve the final typed object from Data Context and build a structured reply |
| RFU | Requirement for Future Use; documented but not currently in scope |
| Execution Trail | Event sequence, causal invocations, cancellation, errors and optional Trace snapshots |
