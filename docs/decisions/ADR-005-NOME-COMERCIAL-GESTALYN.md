# ADR-005: Nome comercial Gestalyn e CLI `gestalyn`

- **Status:** Aceito
- **Data:** 2026-11-11
- **Decisão do responsável:** nome da marca e da CLI; alcance limitado à marca

## Contexto

A especificação mestre definia "ProjectManager" como nome de trabalho, com o nome comercial "pendente de definição" e a CLI com o nome provisório `projectctl`. O repositório remoto já se chamava Gestalyn.

## Decisão

- **Nome comercial: Gestalyn.** Aparece em tudo o que o usuário vê: interface Web (títulos, menu, página inicial), título do documento OpenAPI ("Gestalyn API"), metadado `Product` dos assemblies e documentação.
- **CLI: `gestalyn`.** O executável passa a ser `gestalyn` (`gestalyn task list` etc.). O nome provisório `projectctl` deixa de valer.
- **Nome de trabalho no código continua "ProjectManager".** A solution, os projetos, os namespaces `ProjectManager.*`, as pastas, o banco local (`projectmanager`) e o nome do projeto Docker Compose não mudam.

## Alternativas consideradas

| Alternativa | Motivo da rejeição |
|---|---|
| Renomeação completa do código para `Gestalyn.*` | Toca todos os arquivos, a estrutura aprovada da solution, o banco e a configuração local, sem benefício para o usuário final |
| Manter a CLI como `projectctl` | Nome desalinhado da marca, justamente na ferramenta usada por pessoas e agentes |
| CLI como `gestalynctl` | Mais longo; o responsável preferiu `gestalyn` |

## Consequências

- O código interno (`ProjectManager.*`) e a marca (Gestalyn) têm nomes diferentes. Documentação e AGENTS.md deixam isso explícito.
- Relatórios de fases encerradas mantêm o nome `projectctl`, porque registram o que foi executado na época.
- A Fase 11 da especificação passa a se chamar "CLI gestalyn". A mudança está registrada no topo da especificação mestre.
