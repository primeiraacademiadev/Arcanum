# Arcanum — Incremental workflow and integration framework

Status: working proposal · 2026-11-07

## Purpose

The framework has **two complementary outputs**:

1. **Runtime building blocks** (code): composable workflows and reusable integration components.
2. **Architecture/process documentation** (contracts): a shared vocabulary and templates for workflows, activities, integrations and their operational guarantees.

The V1 does **not** require a standalone framework service. Code begins within Arca and is extracted into `Arcanum.Framework.Workflows` only after real reuse proves the abstraction.

## Workflow runtime model

- A named workflow is an ordered chain of narrowly responsible activities.
- Each activity handles one responsibility and can call `Next` or end with a typed result.
- A typed, scoped context exposes request state, CancellationToken, current outcome and scoped service provider according to the chosen composition design. Prefer explicit construction/injection for stable dependencies.
- A host-level response middleware maps workflow outcomes and exceptions to HTTP codes/ProblemDetails.
- Enforce transaction boundaries intentionally: persistence and outbox activities coordinate through a local Unit of Work, not arbitrary commits.
- Integrations remain at the edges; workflows are testable using fake ports.

Candidate reused activities: request validation, authorization, idempotency check, resolve effective classification, persist, transaction commit, record internal audit. Reuse only where semantics match, not by matching names.

## Workflow specification (document with code)

Each workflow should document:

```yaml
id: audit.publish
owner: tessera.arca
trigger: http.post
contract: audit-event.v1
activities:
  - authenticate-producer
  - validate-contract
  - verify-idempotency
  - classify-event-and-details
  - persist-in-uow
  - commit
outcomes:
  success: durable-ingestion-ack
  rejected: problem-details
audit:
  eligible: true
  recursion_policy: internal-events-do-not-self-audit
observability:
  correlation: request-id,event-uuid
tests:
  - duplicate-delivery
  - invalid-producer
  - conflicting-payload
```

This YAML is an illustrative **documentation pattern**, not a requirement to build a YAML execution engine in V1.

## Integration specification

For each integration, record: producer/consumer, data ownership, versioned contract, protocol, authentication, delivery guarantee (e.g. at-least-once), idempotency key, retry/backoff, timeout, failure handling, metrics/traces and contract tests. Provide an explicit sequence diagram for critical paths.

## Extraction rule

Implement first concrete behavior, then reuse it in a second workflow, then extract a generic building block with tests and docs. Avoid premature general-purpose registries, event buses, plugin engines and external platform services. Maintain an examples folder and diagrams as the framework grows.

## Architectural consistency checks

- Each activity has one clear reason to change.
- The framework does not depend on a particular Tessera business domain.
- Transport-specific HTTP response mapping remains at the API edge.
- No hidden distributed transactions, recursive self-auditing or implicit privilege escalation.
- Each documented integration describes measurable delivery and failure behavior.
