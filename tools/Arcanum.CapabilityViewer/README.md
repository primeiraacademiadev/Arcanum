# Arcanum Capability Atlas — visualizador de Capability JSON

**Estado:** protótipo independente, apenas leitura.  
**Branch de desenvolvimento:** PR #20.  
**Formatos:** Capability JSON Draft 0.4 e JSON legacy do protótipo.

O Atlas representa uma Capability num **canvas de dimensão ilimitada**, com navegação por arrasto e zoom, um catálogo de documentos e um inspetor de contratos, Data Context e recursos de DI.

## Executar

A forma recomendada é arrancar a Demo API, que também disponibiliza os metadados dos construtores C#:

```sh
dotnet run --project src/Arcanum.Demo.Api
```

Abrir **/viewer/** no endereço local apresentado pelo Host. Para reproduzir o endereço usado nos testes:

```sh
ASPNETCORE_URLS=http://127.0.0.1:5086 dotnet run --project src/Arcanum.Demo.Api --no-launch-profile
```

Depois abrir **http://127.0.0.1:5086/viewer/**.

Alternativamente, a versão estática funciona sem .NET (sem a lista inicial dos exemplos e sem inferência CLR de DI):

```sh
python3 -m http.server 8765 --directory tools/Arcanum.CapabilityViewer/wwwroot
```

Abrir **http://127.0.0.1:8765/** e usar **Carregar pasta** ou **+ Ficheiros**.

## Funcionalidades disponíveis

| Necessidade | Implementação |
| --- | --- |
| Carregar uma pasta | Picker nativo do browser (`webkitdirectory`), com seleção recursiva de JSON |
| Carregar vários ficheiros | Picker múltiplo; importações sucessivas coexistem |
| Escolher ou fechar | Lista lateral, pesquisa, fechar individualmente ou fechar todos |
| Espaço infinito | Pan por arrasto, zoom por roda, botões +/− e enquadramento |
| Grafo e edges | Layout por níveis, ligações curvas, setas e etiquetas das intenções; `@spawn` tracejado |
| Nodes e classes | Nome/ID lógico, template e classe Process na própria representação |
| Contrato | Nó sintético de entrada, com inputs e resposta no detalhe |
| Detalhe de Node | Template, Process CLR, descrição, inputs/outputs e bindings, DI |
| Data Context | Variáveis estáticas: nome único, tipo CLR, origem/default, leitores/produtores e marcação de resposta |
| DI | Recursos declarados em `template.resources` e recursos inferidos dos construtores de Processes trusted da Demo API |
| Destaque | Selecionar/desmarcar uma variável ou recurso realça os Nodes participantes; pode combinar seleções Data e DI |

Atalhos: **+/-** para zoom, **0** para enquadrar, **Esc** para remover destaques e seleção.

## Significado dos dados

**O visualizador analisa a especificação, não uma execução.** A lista de Data Context contém nomes, tipos, fontes e bindings inferidos do JSON; não contém **valores vivos** dos objetos, que só existem durante a execução da Capability. A associação entre variável e Node é estática: **consome** / **produz**.

A lista de DI é construída a partir de:

1. `templates[*].resources` explícitos no JSON.
2. Metadados fornecidos por `GET /viewer/api/snapshot`, que inspeciona os construtores **dos Processes registados no Host**. Não carrega assemblies arbitrários.

O painel omite automaticamente `ILogger<T>` do inventário, pois é um recurso de infraestrutura do Host. Se carregares JSON externo cujos Processes não estão registados na Demo API, não será possível inferir os recursos ausentes do JSON; os declarados continuam a aparecer.

O grafo legacy é suportado, incluindo destinos `@complete`/`@stop`, e pode usar metadados CLR para apresentar inputs e outputs onde o JSON não os declara. A ausência desses metadados é uma limitação de inferência.

## Arquitetura

O visualizador não exige React, bundlers ou bibliotecas externas: JavaScript ES Modules, SVG para o grafo e CSS.

- `wwwroot/model.mjs`: parsing/projeção dos dois formatos, índice das variáveis e recursos, layout limitado por ciclos.
- `wwwroot/viewer.mjs`: biblioteca de ficheiros, pan/zoom, seleção, SVG, detalhes e toggles.
- `wwwroot/index.html` / `styles.css`: estrutura e estilos.
- `tests/model.test.mjs`: testes de projeção, participação de dependências, formatos e ciclos.
- `src/Arcanum.Demo.Api/Program.cs`: endpoints de leitura e metadados dos Processes confiáveis.

A Demo API serve apenas os assets pré-definidos e snapshots das Capabilities do diretório configurado no Host. Não oferece endpoint de escrita, avaliação de código JSON ou acesso arbitrário a caminhos no servidor.

**Segurança:** o browser lê os ficheiros locais selecionados pelo utilizador, sem upload para a Demo API. O conteúdo do JSON é tratado como texto e escapado antes de ser mostrado; não é interpretado como código. Como a Demo API não tem autenticação, mantém a ferramenta **apenas em ambiente local**, sem exposição pública.

## Validar

```sh
node --check tools/Arcanum.CapabilityViewer/wwwroot/model.mjs
node --check tools/Arcanum.CapabilityViewer/wwwroot/viewer.mjs
node --test tools/Arcanum.CapabilityViewer/tests/model.test.mjs
dotnet test Arcanum.slnx
bash scripts/smoke-api.sh
```

A CI do PR executa testes Node, build .NET, xUnit, ContractSync e smoke HTTP.

## Limites e extensões futuras

- O layout é automático e não guarda posições manuais; pode ficar denso com grafos grandes.
- A projeção de dependências é **estática** e não substitui o Execution Trail em runtime.
- Não existe edição do Capability JSON nem criação/remoção de Nodes e Edges.
- Não implementa pesquisa de dependências transitivas, namespaces multificheiro ou importação de assemblies externos pelo browser.
- O picker de pasta depende de APIs do browser; Chrome/Edge em desktop são recomendados.
- A biblioteca de pastas externas não persiste após reload.

Próximos passos possíveis: mini-mapa, layout persistente, pesquisa por tipo/Process, importação de Execution Trail, comparação JSON/C# na UI e navegação entre Capabilities relacionadas.
