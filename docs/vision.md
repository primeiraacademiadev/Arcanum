# Arcanum — Product Vision

Status: **working draft** · 2026-11-07

## Purpose

Arcanum is a platform for classifying organizational resources, governing access to them, and preserving verifiable audit records. The project began as a secure Audit Concentrator prototype and is evolving toward resource-oriented access governance.

The platform name is **Arcanum**. **Tessera** is the modular product family; module names denote capabilities rather than mandatory deployable services.

## Capability map

| Capability | Working module name | Purpose |
| --- | --- | --- |
| Resource catalog | Tessera Atlas | Resource types, hierarchy, relations, selectors |
| Classification / LoA | Tessera Gradus | Ordered levels, classification policies and user LoA grants |
| Resource authorization | Tessera Clavis | Roles, permissions, scopes, authorization decisions |
| Policy enforcement | Tessera Custos | Enforcement points and policy integrations; candidate capability |
| Audit storage / query | Tessera Arca | Event ingestion, storage, confidentiality-aware queries, auditing |
| Monitoring | Tessera Vigil | Detection, alerts, operational monitoring; future candidate |

The exact module boundaries remain **provisional**. In particular, ownership of the final authorization decision (PDP) and enforcement (PEP) must be validated through concrete workflows. We should not equate a conceptual module to a microservice.

## Delivery strategy: prototype first, platform by iteration

1. **First, demonstrate the Audit Concentrator (V1)**: two demo producers, transactional outbox and HTTP shipping, Concentrator (.NET Minimal API + PostgreSQL), classification of events and details, confidential auditor WebApp, local identities/RBAC/LoA, and equality search over hashed search terms. This is the immediate priority and its E2E acceptance tests define completion.
2. **Then, extract the common framework progressively**: reusable activity chains, typed workflow contexts, outcomes, integration contracts and documentation, driven by actual second uses rather than invented abstractions.
3. **Finally, grow into generic resource governance**: organization-agnostic resource types, canonical hierarchy plus additional relations, classification policies, resource RBAC/scopes, and LoA elevation workflows.

Start with **one monorepo and distinct .NET projects**, not mandatory microservices. Keep Tessera module boundaries visible in code but introduce new modules/deployables only when needed. See [prototype delivery](prototype-v1.md), [monorepo proposal](monorepo.md) and [framework](framework.md).

The V1 is intended as a demonstrator, not a production-security claim. Event details remain **plaintext** in V1. The HMAC equality index is retained as an exercise in search semantics, **not** as a substitute for encryption.

## Design objectives

- The Concentrator classifies incoming events; producers report facts but do not dictate final event/detail LoA.
- Audit actors represent the origin of each event, not subsequent viewers.
- Every consultation by the confidential auditor generates its **own audit event**, optionally relating to a consulted event.
- LoA is a globally **ordered** four-digit classification (0xxx public, 1xxx internal, 2xxx confidential, 3xxx restricted), with room for hierarchy inside each class.
- Authorization combines effective user LoA, RBAC operation permission and optional scope conditions.
- Resource representation should work for organizations, applications, APIs, databases, schemas, tables, columns and other domains, without creating one persistent node per data value.
- Core architectural decisions must be explainable and testable; keep technology candidates separate from approved choices.

## Out of scope for Audit V1 (retained as evolution ideas)

- Encrypting event details and managing key lifecycle with HSM.
- Real external IdP.
- LoA elevation request/approval workflow and temporary grants arising from approval.
- Multi-approver workflows.
- Generalized graph policy propagation, advanced monitoring, tamper evidence and enterprise-scale integration.

See [architecture](architecture.md), [decisions](decisions.md), and linked GitHub issues.
