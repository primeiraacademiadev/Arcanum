# Arcanum — Prototype-first delivery plan

Status: proposed plan · 2026-11-07

## Delivery principle

Build a working **audit demonstration** first. Prove one vertical end-to-end flow before building a general resource catalog, a standalone policy engine or multiple distributed Tessera services. Functional completeness of the demo, with explicit V1 security limitations, is the first milestone.

### Demo storyline

1. Two independent demo applications persist business changes and audit outbox events in the **same local UoW**.
2. A shared .NET outbox shipping component reliably delivers events over HTTP to the Concentrator.
3. The Concentrator authenticates the producer, validates the payload, deduplicates, classifies both event and detail based on **application + event type**, and persists them atomically in PostgreSQL.
4. A confidential auditor WebApp authenticates local users, applies simple RBAC permission and ordered 4-digit LoA, and displays only authorized event information and details.
5. Searchable terms use type-separated HMAC-SHA256 equality indexes, while their values and details remain **plaintext in V1**.
6. Auditor queries and denials create separate internal audit events, related to a target event where appropriate; no recursive self-auditing.
7. Failures/retries and divergent visibility are observable in tests and the demo.

### V1 acceptance gates

- Outbox delivery survives temporary unavailability without losing business events.
- Retries do not duplicate concentrator events; conflicting payload reuse is rejected.
- Two users with different RBAC/LoA see different data from the same event store.
- Detail access checks event and detail classification independently, plus RBAC/scope.
- Authorization is applied **before pagination**, and responses do not leak unauthorized fields.
- Search by HMAC works across application boundaries with correctly normalized values.
- The internal query-audit workflow records the origin actor and related event.
- A single local setup starts the Concentrator, PostgreSQL, WebApp and two producers; tests cover key flows.

### Delivery increments

| Stage | Deliverable | Gate |
| --- | --- | --- |
| P0 | Monorepo .NET 10 skeleton, compile/test workflow, local PostgreSQL | A green PR pipeline and one command to run infrastructure |
| P1 | Contract + Concentrator publish pipeline + producer A outbox | An event survives retry and appears in the store exactly once |
| P2 | Producer B + event/detail classification rules | Two event types/applications classified at different levels |
| P3 | Local users, basic RBAC+LoA and Confidential Auditor WebApp | Different users see different events/details |
| P4 | HMAC search + internal query auditing + acceptance E2E tests | Demonstration scenario passes automatically |

An increment should be vertically demonstrable; the table is a sequence, not a promise of fixed release dates.

### V1 deliberate exclusions

No encryption of event details, HSM, real IdP, LoA elevation workflows, generic Atlas resource graph, arbitrary policy DSL or OPA/OpenFGA dependency. These remain documented evolutions, not prerequisites.

## Tessera growth path

- **Arca**: audit ingestion, store, confidential querying — first real capability.
- **Gradus**: initially simple classifications and local user LoA inside prototype; extract when a second consumer/use case proves its API.
- **Clavis**: initially a small RBAC authorization component; later resource-level permissions and role-gated approvals.
- **Atlas**: future generic resource catalog, hierarchical inheritance and cross-domain relations.
- **Custos / Vigil**: future policy-enforcement integration and monitoring.

Do not create empty services simply to match conceptual module names. Begin with clear namespaces/projects and replace local implementations with new modules behind interfaces **when necessary**.

## Related issue map

- Initial technical baseline: new monorepo/CI issue.
- Ingestion: #4, shipping/demo: #5, query: #6, HMAC: #7, LoA: #3.
- Framework extraction: new issue.
- Domain boundaries #1 and generic Atlas model #2 remain important, but must **not block** prototype P0/P1.
- Policy technology spike #9 and LoA elevation #8 are post-prototype.
