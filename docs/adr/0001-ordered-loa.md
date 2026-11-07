# ADR-0001: Ordered hierarchical LoA identifiers

Status: **Accepted in concept** · 2026-11-07

## Context

Arcanum classifies resources using a hierarchy. The initial audit prototype associates policies with applications and event types, but the classification scheme must support expansion within classes.

## Decision

Use four-digit ordered numeric codes, logically formatted with leading zeros:

- `0xxx`: public
- `1xxx`: internal
- `2xxx`: confidential
- `3xxx`: restricted

Higher values are more restrictive: `0200 > 0100`, `1130 >= 1120`. Lower positions may refine the hierarchy within a class (e.g. `11xx` departmental; `112x` refinement level 2). The exact catalog of subclasses is not fixed in V1.

Authorization must satisfy `EffectiveUserLoA >= RequiredResourceLoA`, **plus** RBAC permission for the requested operation and any applicable scope restrictions. LoA alone does not authorize an action or granting privileges.

## Consequences

- A numeric comparison is valid for **LoA only**, not a substitute for application/department scope authorization.
- LoA values must be formatted consistently (e.g. zero-padded on display).
- Resource and detail classifications may differ, but detail access also requires permission for the parent event.
- An unresolved classification is not silently public.

## Deferred

- Complete subclass catalog, calculated effective LoA, scoped temporary grants and request/approval workflows.
