# Arcanum — Capabilities, Processes and graph execution

Status: **design proposal**, refined 2026-11-07. Used for Audit Concentrator V1; the framework API and JSON schema remain subject to a small executable spike.

## Objective and rationale

A `Capability` is a versioned, executable description of a business flow. It belongs in the **same source solution** as its Process classes, so CI validates each graph with the code it invokes. It must also serve as **living functional documentation**: a human or an AI development agent should understand what a capability does and how execution proceeds without needing to open every Process implementation.

- **Execution identity**: the full CLR type name (namespace + class name), not a separately maintained alias.
- **Presentation**: `displayName` and optional `description` communicate business meaning; they are not identity keys.
- **Node identity in V1**: the Process full type name, provided the type occurs **once per Capability graph**. A repeated invocation of the same Process will later require an explicit node-instance `id`, or another deliberate extension. Do not allow implicit duplicate nodes.
- **Edges**: `on` maps a Process-returned **intention** to the full type name of the next Process, or to a reserved `@` directive.
- **Source consistency**: graph JSON and implementation version together; rename/namespace changes to a Process must update all graph references in the same PR.

## Proposed JSON — first executable graph

```json
{
  "name": "arca.audit.publish",
  "displayName": "Publicação de eventos de auditoria",
  "description": "Valida, classifica e persiste eventos recebidos de produtores autorizados.",
  "version": 1,
  "entry": "Arcanum.Arca.Processes.ValidateAuditProcess",
  "nodes": [
    {
      "process": "Arcanum.Arca.Processes.ValidateAuditProcess",
      "displayName": "Validar evento recebido",
      "description": "Verifica o contrato e os campos obrigatórios.",
      "saveResultAs": "validation",
      "on": {
        "valid": "Arcanum.Arca.Processes.ClassifyAuditProcess",
        "invalid": "@stop"
      }
    },
    {
      "process": "Arcanum.Arca.Processes.ClassifyAuditProcess",
      "displayName": "Determinar classificação",
      "description": "Resolve o LoA do evento e dos detalhes segundo as regras do Concentrator.",
      "saveResultAs": "classification",
      "on": {
        "classified": "Arcanum.Arca.Processes.PersistAuditProcess"
      }
    },
    {
      "process": "Arcanum.Arca.Processes.PersistAuditProcess",
      "displayName": "Persistir evento",
      "description": "Guarda o evento e os detalhes de forma transacional e idempotente.",
      "saveResultAs": "receipt",
      "on": {
        "stored": "@complete"
      }
    }
  ]
}
```

This **illustrative fragment** intentionally omits producer authorization, idempotency branching and detailed error outcomes for readability. The actual ingest capability must cover those requirements before implementation is complete.

The `on` dictionary represents outgoing **edges** of the node: intention → target Process identity. A future graphical editor may normalize it to explicit edges for drawing, without changing execution semantics. There is no extra mapping from a symbolic logical name to a Process CLR class.

### Graph display and AI legibility

`displayName` is required for the Capability and every node. `description` is recommended as a short, **behavioral statement**, not a mechanical restatement of the class name. The graph can therefore explain:
- its overall purpose;
- what each activity contributes;
- which intentions produce which transitions and terminal results;
- which source class implements each activity.

A graphical renderer should show **displayName**, intention labels and links by default, and expose the full type name and description in details. Changes to labels/descriptions are reviewed with functional changes, but do not change technical node identity. Consider i18n later; do not overload the display name with stable codes.

## Process identification and registration

The Process identifier is `Type.FullName` from the assemblies explicitly registered by a Tessera module. V1 does **not require** `[Process("business.alias")]` or a manual logical-id-to-type correlation. A `ProcessCatalog` is still useful as a trusted **technical index** from full name to `Type` and compiled execution descriptor.

- Scan **only explicit trusted assemblies**; no arbitrary JSON-directed assembly loading or type activation.
- Reject duplicate type names from different assemblies, unknown Processes, unsupported methods and invalid data contracts on startup/CI.
- The same Process may appear in multiple Capabilities, with different `displayName` values appropriate to their contexts.
- If future independently deployed/persisted graph versions require stable external identities, add that abstraction only with a demonstrated need.
- Distinguish graph node identity from **Process implementation type** if repeated instances become necessary; don't silently identify two occurrences as the same node.

## Resolver responsibilities

| Component | Owns | Does not own |
| --- | --- | --- |
| CapabilityResolver | Validated graph plan; current node; intention → edge; terminal result; execution limits and trace | Building infrastructure services or reading HTTP-specific codes |
| ProcessResolver | Trusted type catalog; host-scoped object creation via DI; validated `Execute` method descriptor/invoker | Choosing next node, binding business inputs |
| DataResolver | Per-invocation typed data registry; binding `Execute` parameters; storing typed outputs by name and type | Constructor dependencies or graph navigation |

The architecture uses **two resolution contexts** rather than automatically creating two root service containers:

1. **Resource scope** from the Host DI (`HttpContext.RequestServices` for HTTP or a per-message scope for Workers). All Processes within a Capability execution share that scope for UoW/repository coherence.
2. **Data scope** created for each Capability execution. It holds authenticated inputs, intermediate outputs, metadata and typed objects; no cross-request state is shared.

## Process execution contract (proposed)

```csharp
public sealed record ProcessResult<T>(T? Data, string Intention);

public sealed class ClassifyAuditProcess(IAuditClassifier classifier)
{
    public async ValueTask<ProcessResult<ClassifiedAuditEvent>> ExecuteAsync(
        [FromCapabilityData("incoming")] IncomingAuditEvent incoming,
        [FromCapabilityData("producer")] ProducerIdentity producer,
        CancellationToken ct)
    {
        var classified = await classifier.ClassifyAsync(incoming, producer, ct);
        return new(classified, "classified");
    }
}
```

- Resources are injected into Process constructors by the existing Host DI provider.
- Execution data are injected into **method parameters** by Arcanum's DataResolver. Native Microsoft DI does not implement arbitrary method-level data binding automatically.
- Accept one unambiguous `Execute` returning `ProcessResult<T>`, or `ExecuteAsync` returning `Task/ValueTask<ProcessResult<T>>`; validation rejects ambiguous overloads. The runtime normalizes both to a cached async invoker.
- Bind data by **typed key + name**. Type-only binding is acceptable only when unambiguous; use `[FromCapabilityData("name")]` otherwise. CancellationToken comes from execution runtime.
- The Process returns `Data` and an **intention**. After execution, DataResolver stores the result under `saveResultAs` **before** CapabilityResolver resolves the outgoing edge.

### Intention and terminal directives

- Ordinary intentions are names local to the current Process node, e.g. `valid`, `invalid`, `duplicate`, `classified`.
- A target beginning with `@` is a reserved **graph directive**, not an ordinary business intention. Candidate targets: `@next` (explicit sequential continuation), `@complete` (successful terminal), `@stop` (terminal with current functional result) and `@fail` (failure). Reserve `@retry` only for later deliberate retry design.
- `.` is a namespace separator, e.g. `arca.audit.publish.duplicate`, used if capabilities or external observers need qualified intentions; reusable Process classes need not know their caller's Capability name.
- Every returned intention must match a declared outgoing edge of its node; unknown intentions fail safely. Do not fall through silently.
- Unexpected exceptions are not casually converted to business intentions; preserve diagnostics and avoid exposing sensitive data.

## Runtime sequence

1. On host startup, parse JSON, resolve all Process types and method contracts, validate all node targets, unique names, output bindings, reachability and unsupported graph structures.
2. Obtain the correct Host resource scope and an isolated DataScope for one invocation.
3. Resolve the current Process by its `Type.FullName` through ProcessResolver; bind the Execute parameters from DataResolver.
4. Invoke the Process and obtain `ProcessResult<T>`.
5. Validate/store typed output as `saveResultAs`.
6. Resolve the current node's `on[intention]` into another Process node or an `@` terminal directive.
7. Record transition metadata and duration, then continue or return a transport-neutral Capability result.

Never log confidential data automatically. Do not retry operations with unguarded side effects. UoW/commit boundaries are explicit. V1 starts with deterministic, bounded, acyclic execution; parallelism, cycles, durable resumptions and a graphical editor remain future features. The complete graph definition and its version are immutable during a running invocation.

## Container choice

Native `Microsoft.Extensions.DependencyInjection` is the baseline for resource injection; evaluate Autofac and possibly DryIoc if advanced scopes materially simplify execution (#18). Neither constructor parameterized resolution nor scopes automatically bind arbitrary `Execute` data arguments. The DataResolver remains framework-owned regardless of container choice. No second independent root `IServiceProvider` by default.

## Delivery and decision points

- #17: proof of ProcessResolver + DataResolver + CapabilityResolver over a small JSON graph, with a single branching intention and typed result handoff.
- #15: future versioned graph authoring, validation and editor.
- #18: DI container comparison.
- Before freezing V1 contract: decide whether to permit repeated Process classes via optional node-instance IDs. For now **one occurrence per Capability** is an explicit constraint, not an accidental assumption.
