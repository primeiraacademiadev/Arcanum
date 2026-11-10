# 06 — Operations: Requirements for Future Use (RFU)

**Scope:** only `@spawn` is in the immediate graph-control implementation scope. Other operations are **documented for future cases, not to be implemented now**. [Index](README.md).

## One runtime — Process-backed Operations

The adopted direction is **option B**: framework-supplied or generated Processes implement Operations, using the same Process Runtime, typed bindings, CancellationToken, Execution Trail and scheduler as application Processes. No second general-purpose Operation engine inside the CapabilityResolver.

A future compiler/accelerator may produce stronger generated delegates only if justified by real use cases.

## Operation catalogue (RFU)

| Operation | Concept | Required declaration |
| --- | --- | --- |
| `@call` | Call registered internal/external service or Capability | Request Model and provider/target; typed outcome |
| `@publish` | Publish message/event | Payload Model, targeting variable and provider/topic |
| `@save` | Persist object/mutation | Object Model and targeting/provider |
| `@load` | Load one resource by identity | Key/request Model and typed output |
| `@query` | Execute bounded parameterized query | Criteria Model and typed output |

**Every Operation must declare a Model as an argument.** Some Operations must also specify the **name of the Data Context variable** that is the subject of the action.

The following **edge-oriented syntax is an RFU sketch, not executable JSON**:

```json
{
  "edges": [
    {
      "from": "prepare",
      "intention": "@publish",
      "model": "MyInput",
      "targeting": "myDataXObject"
    }
  ]
}
```

At runtime, `targeting` must resolve a named variable of the Model's concrete CLR type; missing/mismatched input throws. The exact destination/continuation, provider resolution, result variable and business intentions are **undecided**. A future compiler can materialize this into a Process-backed Node with normal NodeExecutionId and diagnostics.

## Semantics to settle on first use

For each Operation, define Model, targeting, authorized provider, exact input/output type, output name, functional intentions, error semantics, side effects, idempotency, transactional boundary and tests. A business outcome such as `not_found` is not an exception; a technical outage is.

Do not embed credentials or arbitrary untrusted URLs directly in the graph. Resolve providers through Host configuration and authorization.

For `@publish`, a future durable Outbox may confirm **acceptance/persistence**; it must not be misrepresented as final consumer delivery. Transport specifics, provider integration and durability are out of scope.

DAO/DTO distinctions and `foreignKey`/`externalKey` are covered by [Data Context](02-data-context-and-binding.md) and [Models](01-declarative-model.md), without imposing automatic entity/DTO mapping now.
