# 05 — ResponseResolver and terminal decisions

**Status:** separate ResponseResolver and single-response policy **agreed direction**; wire details **proposed**. [Index](README.md).

## Three separate responsibilities

| Stage | Owner |
| --- | --- |
| Interpret business intention and graph/system routing | Validated graph plan and `CapabilityResolver` |
| Recognize terminal candidate, coordinate remaining work/cancellation | `CapabilityResolver` / Execution Supervisor |
| Retrieve typed response data from Data Context, validate and construct reply | `ResponseResolver` |
| Map structured reply to HTTP status, headers/body or another channel | Transport adapter |

The CapabilityResolver should distinguish **continue to another Node**, **system control** (`@spawn`), and **response** at graph preparation time rather than repeatedly parsing arbitrary destination strings.

## Typed response from Data Context

A response is a named variable of the CLR type specified by the Capability's contract, **not automatically the last Process result**. Example proposed JSON:

```json
{
  "models": {
    "MyOutput": { "type": "Domain.MyOutput" }
  },
  "contract": {
    "responses": {
      "@200": {
        "model": "MyOutput",
        "from": "firstSaveResult"
      },
      "@204": {
        "body": null
      }
    }
  }
}
```

For typed responses, the resolver reads `(name, type)` from Data Context. A missing or incompatible variable is an exception; `@204` has no body. The wire schema (including optional bodies, headers and channels) remains open.

## Terminal directives and transport

Candidate response directives: `@200`, `@204`, `@302`, `@401`, `@403`, `@404`, `@500`. They are not Process intentions. Existing PoC terminal names `@complete`, `@stop` and `@fail` are **currently implemented**, not the future HTTP-style response model.

`@302` requires a redirect location in a structured response contract; adapters must validate redirects against configured trust rules (avoid open redirects). `@401` may require a transport-specific authentication challenge. A Worker cannot simply "write HTTP" based on a graph string.

The shorthand `{intention}:@302?{url}` is recorded for future compact diagram/DSL representation, **not** an accepted JSON parser grammar.

## Multiple candidate responses

**vNext default: exception when more than one terminal response decision appears, even if statuses match.** Do not choose by first/last observed completion or attempt an implicit merge.

Several Processes may contribute *intermediate data* to one eventual response. That is different from multiple competing terminal decisions. On conflict: log, cancel and drain the started work, preserve diagnostics. A cancellation does not authorize silently dropping running work.

Possible future strategies, **RFU only**:

| Strategy | Behavior |
| --- | --- |
| `exception` | Current agreed default |
| `additive` | Declaratively merge compatible results |
| `firstResultAndCancelOthers` | Select first reply then cooperatively cancel others |

Future merging must have typed deterministic validation. First-result cancellation must still await started workers.

## OnError response

An `OnError` handler can later choose `@500` or a custom error response. That does not erase the fault. Distinguish an error-handler-generated reply from a normal competing business terminal; handler ordering and final precedence require their own decision. The transport adapter uses a structured final response, not raw exception-to-HTTP mapping inside the graph engine.

Related: [events](04-events-and-handlers.md), [data](02-data-context-and-binding.md).
