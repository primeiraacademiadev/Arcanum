# Arcanum


## Capability Framework — arquitetura da próxima versão

A [especificação Capability JSON (Draft 0.4)](docs/capability-framework/08-capability-json-v0.4.md)
organiza Models, NodeTemplates, contexto de dados, execução, eventos/handlers,
ResponseResolver, decisões e roadmap. **O núcleo vNext já é executável** em paralelo com o JSON legacy; handlers,
Operations e respostas anunciadas antecipadamente continuam RFU. Consultar também
[ADR-0003](docs/adr/0003-capability-templates-and-boundaries.md).


Plataforma de classificação de recursos, controlo de acessos e concentração de eventos de auditoria.

## Primeiro protótipo: Capability Framework

O protótipo valida a execução declarativa de **Capabilities JSON**, com Processes .NET
resolvidos pelo nome completo da classe, injeção de recursos no construtor e de
dados nos métodos `Execute`/`ExecuteAsync`.

- `src/Arcanum.Framework/` — definições, resolvers, modelos, exceções e registo no Host.
- `src/Arcanum.Demo.Api/Processes/` — Processes de demonstração, um por ficheiro.
- `src/Arcanum.Demo.Api/Models/` e `Services/` — contratos e serviços scoped da demo.
- `src/Arcanum.Demo.Api/capabilities/` — grafos de exemplo em JSON.
- `tests/Arcanum.Framework.Tests/` — testes de execução, DI, concorrência e limites.

**O JSON da Capability determina quais as classes executáveis.** A framework
resolve apenas os tipos aí declarados, exclusivamente nos assemblies autorizados,
e exige um método `Execute` ou `ExecuteAsync`. Não há `IProcess`, sufixo
obrigatório ou atributo de ativação. `[Process]` é opcional e apenas documental.

Cada `NodeId` identifica um ponto do grafo independentemente da classe Process.
O paralelismo é declarado pela intenção reservada `@spawn` nas ligações
do grafo. Não depende de múltiplas intenções devolvidas pelo Process.
O `CapabilityResolver` aguarda todas as execuções iniciadas e mantém um
Execution Trail com breadcrumbs; em `Trace` captura snapshots limitados.
Qualquer exceção detetada cancela a execução, é registada no logger e o
resolver aguarda todas as tarefas iniciadas antes de terminar com falha.

O `DataResolver` utiliza primeiro o nome do parâmetro e, se necessário,
uma correspondência inequívoca por tipo. `[FromCapabilityData("name")]`
é um override. `[ReturnName("name")]` define o nome do resultado.

Cada Capability define um `contract` declarativo: entradas tipadas provenientes
de JSON ou defaults e respostas tipadas para os resultados terminais. O adaptador
HTTP da framework cria os dados e devolve as respostas; `Program.cs` não faz
conversões próprias da Capability.

```bash
dotnet restore Arcanum.slnx
dotnet test Arcanum.slnx
dotnet run --project src/Arcanum.Demo.Api
```

Os exemplos vNext estão em `demo.capability-json.json`,
`demo.capability-json-parallel.json` e `demo.capability-json-timeout.json`.
O `tools/Arcanum.ContractSync` analisa assinaturas C# e propõe alterações
aos contratos técnicos do JSON sem reescrever a topologia.

Consultar [Capability JSON 0.4](docs/capability-framework/08-capability-json-v0.4.md),
[JSON Schema](schemas/capability-json-v0.4.schema.json),
[instruções de execução](docs/prototype-implementation.md),
[contrato das Capabilities](docs/capabilities.md) e
[visão do produto](docs/vision.md).

**Não expor a Minimal API demonstradora à Internet:** o endpoint ainda não tem autenticação.
A ingestão de auditoria, PostgreSQL, LoA/RBAC e outbox serão incrementos posteriores.
