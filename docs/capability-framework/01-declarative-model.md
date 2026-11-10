# 01 — Declarative Capability model

> **Draft 0.4 update:** Models, NodeTemplates, explicit Node bindings and C# contract validation are now **implemented**. CLR signatures are the technical authority; constructor `resources` are optional and may omit `ILogger<T>`. General template-feature overrides remain RFU. Read the [executable specification](08-capability-json-v0.4.md).

**Status:** historical design detail, updated by Draft 0.4 executable contract. [Back to index](README.md).

## Conceptual sections

| Key | Responsibility |
| --- | --- |
| `models` | Capability-local aliases of concrete .NET data types and optional RFU reference metadata |
| `templates` | Reusable NodeTemplates: Process implementation and resource, input and output contracts |
| `graph.nodes` | Graph-local Node instances of templates, with per-instance bindings |
| `graph.edges` | Functional transitions and system control (`@spawn`) |
| `contract` | External typed input and response contracts |
| `policies` | Behavioral limits and deadlines, overriding framework Host defaults |
| `handlers` | Event strategies/pointcuts, with possible edge-specific overrides |

`templates` replaces current direct `graph.nodes[id].process` references. **Only explicitly trusted assemblies** may provide Process classes; this is not dynamic code loading.

## Models and future identity metadata

```json
{
  "models": {
    "MyInput": {
      "type": "Domain.MyInput",
      "foreignKey": "MyForeignKeyParameter",
      "externalKey": {
        "key": "SomeGUIDParameter",
        "targetSystem": "AnotherSystem"
      }
    },
    "MyOutput": { "type": "Domain.MyOutput" }
  }
}
```

The Model's `type` identifies one CLR type. `foreignKey` and `externalKey` are **RFU metadata** with no approved runtime semantics. A Model is neutral: it is not automatically a DTO or DAO.

## NodeTemplate versus Node

A NodeTemplate describes the Process and its contract; CLR signatures remain the definitive implementation and must be checked during compilation.

```json
{
  "templates": {
    "save": {
      "process": "Domain.SaveProcess",
      "resources": { "repository": "Domain.ISaveRepository" },
      "inputs": { "input": "MyInput" },
      "outputs": { "result": "MyOutput" }
    }
  },
  "graph": {
    "entry": "firstSave",
    "nodes": {
      "firstSave": {
        "template": "save",
        "bindings": {
          "inputs": { "input": "myDataXObject" },
          "outputs": { "result": "firstSaveResult" }
        }
      }
    }
  }
}
```

The output describes the **single `T`** of `ProcessResult<T>`. `firstSave` is a graph-local NodeId; `save` is a reusable TemplateId. A second Node may reference `save` with different bindings, without changing the Process class. The runtime assigns a fresh NodeExecutionId for every actual invocation, including loops.

The `resources` declaration may eventually be inferred from constructor reflection rather than duplicated in JSON. **Whether mandatory or generated is open.** The future Node may override selected template features, but the allowed fields, precedence and merge rules are **not yet fixed**; never interpret the example as authorizing arbitrary schema overrides.

## Identity distinctions

| Identity | Scope |
| --- | --- |
| ModelName | Alias for concrete CLR type |
| TemplateId | Reusable Process description |
| NodeId | Stable logical graph occurrence |
| ExecutionId | Runtime Capability invocation |
| NodeExecutionId | Runtime individual Node execution |
| Process CLR type | Actual implementation, not Node identity |

## Contracts, channels and policies

The external Capability contract maps request/message data to concrete typed objects and named response values. A future declaration of exposure channel (REST/AMQ/Kafka) remains a **design direction with schema to define**, not a current implementation.

Policy naming follows the architecture review: Capability behavior under `policies`, framework/Host defaults in Microsoft's `Options`. Example (illustrative timeout, not an approved default):

```json
{
  "policies": {
    "maxSteps": 100,
    "maxDepth": 20,
    "timeout": "PT30S"
  }
}
```

See [Data Context](02-data-context-and-binding.md) and [execution semantics](03-graph-execution.md).
