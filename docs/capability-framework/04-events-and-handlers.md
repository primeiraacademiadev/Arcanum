# 04 — Transversal events, handlers and pointcuts

**Status:** event/handler and edge-override model **agreed direction**; schemas, composition and timing **open**. [Index](README.md).

## Events rather than three kinds of exit

The earlier distinction between orderly, spurious and abrupt exits is dropped in favor of lifecycle **events**:

| Event | Meaning |
| --- | --- |
| `OnCompleted` | Functional work finished normally |
| `OnError` | A Process or engine fault was detected |
| `OnCancelled` | Execution cancelled (without a superseding fault) |
| `OnFinally` | Final closure after owned work settles |

`OnTimeout`, per-Node and per-transition events are candidates for later evaluation, not commitments now. A timeout cause must be recorded even without a dedicated OnTimeout event. Events are **not automatically business graph transitions**.

## Strategies and pointcuts

A **handler** executes a strategy selected for a particular event and pointcut. It need not produce an HTTP response: future strategies may notify an external application, publish an event, audit or perform a custom action.

One **conceptual** global error strategy (JSON not yet implemented):

```json
{
  "handlers": {
    "defaultError": {
      "on": "OnError",
      "pointcut": { "scope": "capability" },
      "strategy": { "type": "response", "target": "@500" }
    }
  }
}
```

The default response-500 example is not the full handler model. A customized error handler is a future design requirement.

## Edge overrides, without segments

A handler may be specified on an **edge** to affect its own transition or the invocation directly started by that edge. Example of a possible **local** notification:

```json
{
  "from": "prepare",
  "intention": "ready",
  "to": "callPartner",
  "handlers": {
    "OnError": {
      "add": ["notifyPartner"]
    }
  }
}
```

**No segments for now.** An edge-local override is **not automatically inherited** by all descendants of that edge. This deliberately avoids ambiguous "segment ends at join" inference. The final decision between edge-transition pointcuts and directly activated-Node pointcuts is open until a real handler case exists.

The global handler should not be silently suppressed by a local notification, nor may any handler disable the mandatory exception logging, cancellation or drain invariants. Candidate composition verbs `add`/`replace`/`disable` are merely proposals, not accepted semantics. Precedence, filtering and deduplication will be designed **as concrete needs arise**.

## Handler order and failures

Invariants:
- Every Process/engine exception is logged and immediately signals cancellation of the business execution.
- Every error from a handler must also be logged.
- No handler restarts the cancelled functional graph.
- An error handler needing to execute after cancellation must have a deliberate context/token/lifetime policy; blindly reusing the cancelled token is incorrect.
- Handler-on-handler failures must not recursively emit unbounded `OnError` events.
- Capability-owned work is awaited before a final reply can be produced.

**Still open:** dispatch before/after draining; first-failure global `OnError` versus one per distinct exception; strategy composition/priority; handler resource scope, time limits and cancellation; handling conflicting handler-generated responses. These questions belong to a future concrete handler scenario, **not** to the first vNext implementation by assumption.

See [execution](03-graph-execution.md), [ResponseResolver](05-response-resolution.md) and [roadmap](07-decisions-and-roadmap.md).
