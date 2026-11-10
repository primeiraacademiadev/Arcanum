# 02 — Data Context, typed bindings and CLR semantics

> **Draft 0.4 update:** Data Context variable **name is unique**, with one declared CLR type; `(name, type)` describes its contract rather than independent composite keys. .NET **polymorphic assignability** is accepted. A named output **unconditionally replaces** a compatible stored value. Draft 0.4 uses explicit input names with **no compatible-type fallback**. Legacy binding remains for compatibility. See [executable specification](08-capability-json-v0.4.md).

**Status:** historical design detail, updated by Draft 0.4 executable contract. [Back to index](README.md).

## Variables identified by `(name, type)`

A Data Context variable has a **name and a concrete CLR type** (possibly referenced through a Model alias). A Process/Operation requesting a binding must find that named, correctly typed variable or **throw**. No implicit creation, random compatible-type lookup or ambiguity fallback is intended for vNext.

A missing value differs from a declared nullable variable whose value is `null`. An output returned by `ProcessResult<T>` is stored under its Node's **explicit output name** and declared type; the new value **replaces** the prior `(name, type)` value at completion.

**Open:** whether distinct types can coexist under one identical name, exact CLR-type equality versus declared assignability, and detailed nullable metadata. The agreed principle is unambiguous identification, not implementation of those open cases by guesswork.

## Explicit mapping

```json
{
  "template": "save",
  "bindings": {
    "inputs": { "input": "myDataXObject" },
    "outputs": { "result": "firstSaveResult" }
  }
}
```

The template declares `input: MyInput` and `result: MyOutput`. During compilation, validate the CLR Process method, constructor resources and declared Models. At runtime, resolve actual input values, invoke the method, obtain its single `ProcessResult<T>`, write the named typed output, and pass its intention to the graph.

The runtime `CancellationToken` is **not business data**; it is supplied from execution context. Constructor dependencies are obtained from the same Host DI scope, not from the functional Data Context. Preparing a delegate at startup must **not** instantiate scoped Processes or capture request data early.

## May a Process access the entire Data Context?

**Proposed initial restriction (not independently approved):** no unrestricted Data Context argument for application Processes. They receive only declared typed arguments and return typed data. That makes dependencies visible and delegates/validation predictable. Future Operations and handlers may require controlled context access with their own explicit contract.

## Normal .NET parameter and mutation behavior

| Form | Semantics |
| --- | --- |
| Reference type parameter | Reference passed by value. Mutating the referenced object is visible to all holders of that instance; reassigning the local parameter does not replace the context entry |
| Value type parameter | Value normally copied. Changing the copy does not update the stored value |
| Returned `ProcessResult<T>.Data` | Explicitly assigned to the Node's output `(name, type)` after the method completes, replacing the previous value |
| Complex object containing references | Usual CLR aliasing rules, including references inside copied structures |
| `ref`/`out` arguments | Distinct CLR constraints, particularly with async; **not designed for vNext** |

**Shared data is intentionally not isolated.** Parallel Processes can mutate the same object references and share scoped infrastructure services; interference and nondeterministic observations are accepted effects. The scheduler's ownership of named-result bookkeeping does not make arbitrary object or DbContext use thread-safe.

## Startup and invocation delegates

The preparation layer should verify: trusted Process type, singular valid `Execute` or `ExecuteAsync`; constructor DI requirements; method inputs and result type; Node bindings; output name; compatible Models. Build/cache invocation delegates and descriptors, not instantiated scoped resources. Runtime missing variables cause exceptions, logging and cancellation.

## DAO versus DTO

**Recommendation:** distinguish persistent-data and transfer-contract responsibilities conceptually, **without mandatory one-to-one class duplication**. Separation yields independent schema/version evolution, safer field exposure, distinct validation/serialization and clearer external identity. Its costs are mapping code/tests, allocations and more types to maintain. A single CLR type may intentionally play both roles where safe. The `models` registry imposes no DAO/DTO taxonomy by default.

## Difference from PR #20

The current Data Scope is keyed by **name** and supports explicit attributes, parameter-name resolution and compatible-type fallback. vNext wants explicit Node/template bindings and an unambiguous `(name, type)` resolution contract. See [migration matrix](07-decisions-and-roadmap.md#implementation-gap-matrix).
