# Arcanum

Plataforma de classificação de recursos, controlo de acessos e concentração de eventos de auditoria.

## Primeiro protótipo: Capability Framework

O protótipo valida a execução declarativa de **Capabilities JSON**, com Processes .NET
resolvidos pelo nome completo da classe, injeção de recursos no construtor e de
dados nos métodos `Execute`/`ExecuteAsync`.

- `src/Arcanum.Framework/` — definições, resolvers, modelos, exceções e registo no Host.
- `src/Arcanum.Demo.Api/Processes/` — Processes de demonstração, um por ficheiro.
- `src/Arcanum.Demo.Api/Models/` e `Services/` — contratos e serviços scoped da demo.
- `src/Arcanum.Demo.Api/capabilities/` — três grafos de exemplo em JSON.
- `tests/Arcanum.Framework.Tests/` — testes de execução, DI, concorrência e limites.

Não existe um marcador `IProcess`. A descoberta usa a convenção `*Process` nos
assemblies explicitamente registados, com um `Execute`/`ExecuteAsync` válido.
Os parâmetros do método são ligados pelo tipo quando o dado é único;
`[FromCapabilityData("name")]` só é necessário em caso de ambiguidade.

```bash
dotnet restore Arcanum.slnx
dotnet test Arcanum.slnx
dotnet run --project src/Arcanum.Demo.Api
```

Consultar [instruções de execução](docs/prototype-implementation.md),
[contrato das Capabilities](docs/capabilities.md) e
[visão do produto](docs/vision.md).

**Não expor a Minimal API demonstradora à Internet:** o endpoint ainda não tem autenticação.
A ingestão de auditoria, PostgreSQL, LoA/RBAC e outbox serão incrementos posteriores.
