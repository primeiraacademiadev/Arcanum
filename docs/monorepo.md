# Arcanum — Monorepo structure and CI proposal

Status: proposed · 2026-11-07

## One repository, multiple projects and independent deployables

```text
Arcanum/
  Arcanum.slnx
  global.json
  Directory.Build.props
  Directory.Packages.props
  .github/workflows/pr-verify.yml
  docs/
    architecture.md
    decisions.md
    prototype-v1.md
    framework.md
    monorepo.md
    adr/
  src/
    Arcanum.Contracts/
    Tessera.Arca/
      Arcanum.Arca.Api/
      Arcanum.Arca.Core/
      Arcanum.Arca.Postgres/
    Shipping/
      Arcanum.Shipping.Outbox/
    Framework/
      Arcanum.Framework.Workflows/
    Demo/
      Arcanum.Demo.ProducerA/
      Arcanum.Demo.ProducerB/
    Web/
      Arcanum.Auditor.Web/
  tests/
    Arcanum.Arca.UnitTests/
    Arcanum.Arca.IntegrationTests/
    Arcanum.Shipping.Tests/
    Arcanum.Demo.E2ETests/
  deploy/
    compose.yaml
```

**This is a target layout, not a requirement to generate every project on P0.** Create projects at the first increment needing them. The auditor UI may use ASP.NET Core Razor Components/Blazor to retain one .NET toolchain for the demo, subject to a small frontend decision.

- `Contracts`: stable wire model shared by producer packages and Concentrator, without domain/persistence dependencies.
- `Arca.Api`: Minimal API endpoints, workflow composition, auth and response middleware.
- `Arca.Core`: domain rules (classification, authorization orchestration, querying).
- `Arca.Postgres`: persistence, SQL/EF Core migrations, internal UoW.
- `Shipping.Outbox`: reusable producer-side registration/writer/dispatcher. Business mutation and outbox write must share each producer's *local* transactional database; no cross-system UoW.
- `Framework.Workflows`: reusable activity chain and typed context/result abstractions, extracted from concrete use in Arca. Do not turn into a general service locator.
- `Demo.ProducerA/B`: separate demonstrators of visibility and shipping.
- `Auditor.Web`: confidential query experience and local auth demo.

Keep references directed inwards: host → application/core → contracts; persistence implements ports, not the other way around. Do not share EF entities or repositories with producers.

## Pull request CI

Initially, use **one always-running required check** on every PR:
1. Checkout; install pinned supported SDK from `global.json` (.NET 10 LTS proposed).
2. Restore locked dependencies; build whole solution with warnings policy.
3. Run unit and integration tests using temporary PostgreSQL.
4. Run an E2E acceptance smoke for the demo when available.
5. Publish summarized test results and diagnostics.

NuGet dependency caching via `setup-dotnet` uses committed `packages.lock.json`; pin shared package versions in `Directory.Packages.props`.

A monorepo simplifies atomic contract changes, test coverage and dependency management; **it does not inherently make jobs faster**. Full solution builds are the best starting point while small. Later optimize via selective impacted-project test jobs and parallelism, but preserve an always-executed required verification job. GitHub Actions workflow-level `paths` filters can leave required checks pending on skipped PRs; avoid this trap.

For deployments, build/publish only the changed deployables **after** PR verification; libraries are not deployed independently. Keep deployment pipelines and secrets independent by target component as the platform grows.
