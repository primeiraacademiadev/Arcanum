# Idea — GUI prototyping guided by architectural strategies

**Status:** exploration backlog / RFU · 2026-11-10 · [GitHub issue #21](https://github.com/primeiraacademiadev/Arcanum/issues/21). Not part of Capability JSON 0.4 delivery.

## Hypothesis

Define a repeatable process for **prototyping user interfaces while comparing and selecting architectural strategies**. Human/AI collaboration should make design intent, interaction models, architectural trade-offs and implementation constraints explicit *before* investing in production GUI code.

## Questions for future exploration

- What is the input specification: user journeys, roles, data contracts, constraints, design system, accessibility, and non-functional requirements?
- How do we compare alternatives such as server/client rendering, component composition, state management, orchestration, integration patterns and observability?
- Can the same conceptual GUI specification generate *multiple* working prototypes with measurable evaluation criteria?
- How should prototypes capture architectural rationale (ADRs), implementation feedback and progressive refinement?
- How might this workflow connect with Arcanum Capability JSON and existing reusable Design System components **without making UI technology part of the core execution graph**?

## First experiment (future)

Choose one narrow end-to-end user journey, describe its data contracts and user intent, produce two alternative architectures and GUI prototypes, assess UX/accessibility/testability/change cost, and record the decision. **No platform or tooling is being implemented as part of this note.**
