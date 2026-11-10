# Arcanum — Decision Register

Status terms: **Agreed** = stated decision; **Proposed** = design direction still open; **Deferred** = intentional evolution.

| ID | Status | Decision / question |
| --- | --- | --- |
| D-001 | Agreed | Arcanum platform; Tessera module naming family. |
| D-002 | Agreed | Concentrator determines effective event and detail LoA; producers do not. |
| D-003 | Agreed | Four-digit, ordered LoA; higher codes impose stricter access (e.g. 0200 > 0100; 1130 >= 1120); optional supplementary scope checks. |
| D-004 | Agreed | Roles/permissions (RBAC) govern operations; possession of high LoA does **not** confer permission to approve LoA elevations. |
| D-005 | Agreed | Audit event identifies its originating actor (type, issuer, subject); consultation creates a separate audit event; internal auditor events may use related_event. |
| D-006 | Agreed | Event/detail classifications are separate; viewing details requires access to both. |
| D-007 | Agreed | V1 ingestion: .NET Minimal API, PostgreSQL, producer-side transactional outbox, HTTP shipping, UoW, activity chain and response middleware. |
| D-008 | Agreed | V1 has no detail encryption; maintain plaintext fields and HMAC-SHA256 searchable equality hash (type-separated, global secret). |
| D-009 | Agreed | Private keys never leave Concentrator / HSM; producers receive public keys when crypto is introduced. |
| D-010 | Deferred | Elevation request + approval + temporary LoA grant; approving requires a role/permission, subject to separation of duties and configured authority limits. |
| D-011 | Deferred | Real IdP integration, HSM externalization, encryption at rest and transport-level crypto beyond HTTPS. |
| D-012 | Proposed | Canonical resource tree with supplementary typed graph relations; do not propagate authorization along arbitrary edges. |
| D-013 | Proposed | Separate resource catalog, classification/LoA management, authorization and audit as conceptual modules; service/process boundaries undecided. |
| D-014 | Proposed | PostgreSQL `ltree` for canonical paths; evaluate OPA and OpenFGA against concrete test cases before adoption. |
| D-015 | Proposed | Query authorization and LoA filtering must be pushed down before pagination, with a clear decision explanation. |
| D-016 | Open | Role-permission schema and location of authoritative PDP; candidate Tessera Clavis. |
| D-017 | Open | Policy inheritance, selectors, overrides, effective classification, versioning and historical decision reconstruction. |
| D-018 | Open | Exact publish JSON, actor taxonomy, idempotency response semantics and handling of malformed inputs. |

Decisions can be promoted to ADRs when we have a specific contextual trade-off and rationale. Do not turn the entire register into irreversible commitments.


## Capability Framework — next-prototype decisions (2026-11-09)

See [the structured vNext register](capability-framework/07-decisions-and-roadmap.md)
and [ADR-0003](adr/0003-capability-templates-and-boundaries.md) for context and
for distinctions between agreed direction, proposals and RFU.

| ID | Status | Decision / question |
| --- | --- | --- |
| CF-01 | Agreed direction | Model → NodeTemplate → Node; separate identities, typed contracts and named result bindings. |
| CF-02 | Agreed direction | Data Context variables identified by (name, type), shared with normal CLR reference/value semantics. |
| CF-03 | Agreed direction | Keep @spawn; reachability-based convergence, global drain and immediate error cancellation. |
| CF-04 | Agreed direction | Per-Capability policies instead of Options; Host Options remain, plus overridable global timeout. |
| CF-05 | Implemented core / RFU | Implicit ResponseResolver reads typed Data Context after completion; announced response conflicts/hash are RFU. |
| CF-06 | Agreed direction | Transversal events/handlers and edge-level overrides; no graph segments for now. |
| CF-07 | Deferred (RFU) | Process-backed @call/@publish/@save/@load/@query, requiring Models and optional targeting. |
| CF-08 | Mixed | Draft 0.4 template schema and sync tool implemented; pointcut/handler ordering and explicit response contract still RFU. |

**Draft 0.4 (2026-11-10):** [Executable Capability JSON specification](capability-framework/08-capability-json-v0.4.md), [schema](../schemas/capability-json-v0.4.schema.json) and [status matrix](capability-framework/07-decisions-and-roadmap.md).
