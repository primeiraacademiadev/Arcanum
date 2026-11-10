# 03 — Graph execution, convergence and supervision

> **Draft 0.4 update:** `@spawn` remains unconditional even for a non-matching business intention. Draft 0.4 allows normal **leaf Nodes without terminal edges**, including nodes with no matching conditional continuation; legacy mapping rules are preserved. Global timeout and Capability policy override are implemented, with provisional Host default of five minutes. See [executable specification](08-capability-json-v0.4.md).

**Status:** historical design detail, updated by Draft 0.4 executable contract. [Back to index](README.md).

## Functional and system routing

A Process returns one functional `Intention`. At most one conditional outgoing edge may match it. `@spawn` is a reserved **edge intention** activated by the engine irrespective of the Process outcome, and multiple `@spawn` edges may share a source:

```json
{
  "edges": [
    { "from": "prepare", "intention": "ready", "to": "alpha" },
    { "from": "prepare", "intention": "@spawn", "to": "beta" },
    { "from": "prepare", "intention": "@spawn", "to": "gamma" }
  ]
}
```

`@spawn` is **not** returned in `ProcessResult<T>`. Outgoing scheduling is always asynchronous, even after a synchronous `Execute`. No explicit Fork/Join Process or `PathActivation` exists. Other system Operations are [RFU](06-operations-rfu.md).

## Dynamic convergence

When a Node has pending arrivals, use the agreed possible-path rule:

1. Examine each **running or already scheduled** other invocation.
2. Determine whether, after already selected intentions, a possible route from it **can still reach** the waiting Node.
3. Wait for all relevant invocations that can still reach it.
4. If an invocation diverges onto a route that can no longer reach it, stop waiting **for that arrival**, but the divergent invocation **continues** independently.
5. If no other active/scheduled invocation can reach the Node, schedule it.

Convergence is not a static count of all incoming edges, and a divergent branch does not invalidate the join. Re-entrant loops/multiple simultaneous arrivals need stress tests; causal-depth limits alone do not prove freedom from cyclic deadlocks.

## Terminal candidate versus global completion

Reaching a response destination creates a **candidate**, not an immediate return. The Capability does **not** return or rethrow before **all already started work settles**, including detached branches. Multiple terminal response decisions are an **exception** in vNext, even if their statuses agree. Several Processes may still contribute *data* to one final response; see [ResponseResolver](05-response-resolution.md).

## Limits and timeout

The prototype already enforces `maxSteps` (total invoked nodes) and `maxDepth` (causal path depth). vNext exposes these under Capability `policies`, with Host defaults from `Options`. Add **one global elapsed-time timeout**, overrideable by Capability; it does not restart per Process. No concrete default is yet approved.

```json
{
  "policies": {
    "maxSteps": 100,
    "maxDepth": 20,
    "timeout": "PT30S"
  }
}
```

The example is illustrative. On deadline expiry record cause `Timeout`, signal the linked CancellationToken, stop new scheduling and await started tasks. A Process ignoring cancellation may prevent a bounded real-time shutdown: the timeout is **cooperative**.

## Exceptions and cancellation

1. Every detected exception is **logged with the exception object**.
2. The first Process/engine failure **immediately signals cancellation** (not only when the scheduler later awaits it).
3. Do not start new Nodes after cancellation is observed.
4. Await all already started tasks and log other errors thrown while draining.
5. Preserve the original fault instead of a secondary `OperationCanceledException`.
6. Preserve the execution trail and correlations on failure.
7. Do not resume the cancelled business graph from an error handler.

External cancellation, timeout and fault are distinct causes. The precise event handler scheduling order is still an [open issue](04-events-and-handlers.md#handler-order-and-failures).

## Execution Trail

Record `ExecutionId`, `NodeId`, `NodeExecutionId`, sequence, timestamp, method, duration, thread, causal transitions, scheduling, join waits, cancellation and exceptions. At effective `Trace` capture immutable serialized input/output snapshots at the observation instant, with redaction and byte limits, excluding infrastructure and CancellationToken. Snapshot serialization errors must be logged. Trace helps investigate overlap; it does not isolate mutable objects or guarantee deterministic replay.

## Responsibility decomposition (proposed)

The `CapabilityResolver` remains the lifecycle coordinator. Delegate implementation details to a compiler, execution scheduler, join coordinator, DataResolver, Process runtime, Event Dispatcher, Execution Supervisor, ResponseResolver and Execution Trail **only as justified**. These are responsibility boundaries, **not a required interface per box**. See [roadmap](07-decisions-and-roadmap.md).
