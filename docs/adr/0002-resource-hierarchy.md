# ADR-0002: Canonical resource tree plus typed relations

Status: **Proposed** · 2026-11-07

## Context

Arcanum must classify and authorize heterogeneous resources: applications/events, organizational assets, API operations, databases/schemas/tables/columns, documents and possibly selected data values. A tree gives deterministic policy inheritance, while domain relationships are not always a tree (e.g. a database cell belongs to a row and column).

## Proposal

- Model a resource as a typed identity with a single canonical parent used for hierarchy.
- Model additional typed directed relations separately, without default inheritance.
- Associate policies with resources or selectors over dynamic resource sets; do not necessarily materialize every event or value as a catalog node.
- Explore PostgreSQL adjacency list plus `ltree` and a typed relation table before introducing a graph database.
- Treat policy inheritance and conflicts explicitly; do not assume that generic edges grant rights.

## Open questions / validation

1. What defines the canonical parent for resources with multiple natural parents?
2. Are policy selectors typed and composable, and can they become efficient SQL predicates?
3. How are classifying policies combined (maximum, override, context)?
4. What is the boundary between catalog resources and audit-record instances?
5. How are decisions explained and reconstructed historically?

This is a hypothesis for a technology spike, not yet an accepted architectural constraint.
