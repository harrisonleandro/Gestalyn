# Estado do projeto

> Controle de progresso exigido pela seção 15 da [especificação mestre](specifications/ESPECIFICACAO-MESTRE.md).
> Estados permitidos: Not Started · Planned · In Progress · Blocked · In Review · Completed.
> Uma tarefa só fica `Completed` quando seus critérios são verificados.

**Atualizado em:** 2026-11-10

## Fase atual

**Fase 05, Projetos e equipes: In Review.** Implementada e validada localmente (136/136 testes, duas mutações detectadas e execução real da API com usuários reais), em commits locais na branch `feature/fase-05-projetos`. Faltam o push, o CI e a integração em `develop`, depois das Fases 03 e 04.

**Fase 04: In Review**, com o PR #4 aberto e o CI na fila do GitHub. **Fase 03: Completed.** O merge do PR #3 foi autorizado, mas o CI do último commit terminou com falha na publicação do artefato de resultados (o build e os testes passaram).

## Estado das fases

| Fase | Nome | Estado | Especificação | Relatório |
|---|---|---|---|---|
| 01 | Fundação da solução .NET | Completed | [FASE-01-FUNDACAO.md](specifications/FASE-01-FUNDACAO.md) | [FASE-01-RELATORIO.md](reports/FASE-01-RELATORIO.md) |
| 02 | Persistência e modelo de domínio | Completed | [FASE-02-PERSISTENCIA.md](specifications/FASE-02-PERSISTENCIA.md) | [FASE-02-RELATORIO.md](reports/FASE-02-RELATORIO.md) |
| 03 | Identidade e autenticação | Completed | [FASE-03-IDENTIDADE.md](specifications/FASE-03-IDENTIDADE.md) | [FASE-03-RELATORIO.md](reports/FASE-03-RELATORIO.md) |
| 04 | Organizações e permissões | In Review | [FASE-04-ORGANIZACOES.md](specifications/FASE-04-ORGANIZACOES.md) | [FASE-04-RELATORIO.md](reports/FASE-04-RELATORIO.md) |
| 05 | Projetos e equipes | In Review | [FASE-05-PROJETOS.md](specifications/FASE-05-PROJETOS.md) | [FASE-05-RELATORIO.md](reports/FASE-05-RELATORIO.md) |
| 06 | Fases, marcos e tarefas | Not Started | — | — |
| 07 | Workflow configurável e histórico | Not Started | — | — |
| 08 | Auditoria, documentação e dashboards | Not Started | — | — |
| 09 | Interface web completa | Not Started | — | — |
| 10 | API e autenticação de agentes | Not Started | — | — |
| 11 | CLI gestalyn | Not Started | — | — |
| 12 | MCP e integrações com agentes | Not Started | — | — |
| 13 | Testes integrados e hardening | Not Started | — | — |
| 14 | Infraestrutura e publicação cloud | Not Started | — | — |

## Tarefas da Fase 01

| ID | Tarefa | Estado |
|---|---|---|
| F01-001 | Inspecionar ambiente e repositório | Completed |
| F01-002 | Verificar SDK .NET 10 | Completed |
| F01-003 | Verificar Git | Completed |
| F01-004 | Criar Solution | Completed |
| F01-005 | Criar projetos iniciais | Completed |
| F01-006 | Configurar referências | Completed |
| F01-007 | Configurar API | Completed |
| F01-008 | Configurar Blazor | Completed |
| F01-009 | Configurar CLI | Completed |
| F01-010 | Configurar testes | Completed |
| F01-011 | Configurar padrões de código | Completed |
| F01-012 | Configurar Git e documentação | Completed |
| F01-013 | Configurar GitHub Actions | Completed |

Evidências de cada tarefa estão na [especificação da Fase 01](specifications/FASE-01-FUNDACAO.md#tarefas).

## Tarefas da Fase 02

| ID | Tarefa | Estado |
|---|---|---|
| F02-001 | Projetar entidades | Completed |
| F02-002 | Definir identificadores | Completed |
| F02-003 | Definir relacionamentos | Completed |
| F02-004 | Implementar entidades de domínio necessárias | Completed |
| F02-005 | Criar configurações EF Core | Completed |
| F02-006 | Configurar PostgreSQL | Completed |
| F02-007 | Criar DbContext | Completed |
| F02-008 | Implementar migrations | Completed |
| F02-009 | Implementar auditoria básica de persistência | Completed |
| F02-010 | Configurar índices | Completed |
| F02-011 | Implementar restrições de integridade | Completed |
| F02-012 | Testar persistência | Completed |
| F02-013 | Testar isolamento multi-tenant | Completed |

Evidências na [especificação da Fase 02](specifications/FASE-02-PERSISTENCIA.md#execução-e-ajustes-em-relação-ao-plano) e no [relatório](reports/FASE-02-RELATORIO.md).

## Tarefas da Fase 03

| ID | Tarefa | Estado |
|---|---|---|
| F03-001 | Configurar ASP.NET Core Identity | Completed |
| F03-002 | Criar cadastro | Completed |
| F03-003 | Criar login | Completed |
| F03-004 | Criar logout | Completed |
| F03-005 | Implementar sessão e autenticação da interface web | Completed |
| F03-006 | Implementar autenticação da API | Completed |
| F03-007 | Implementar recuperação de senha | Completed |
| F03-008 | Implementar validação de credenciais | Completed |
| F03-009 | Implementar expiração e renovação de sessão | Completed |
| F03-010 | Implementar proteção contra tentativas abusivas | Completed |
| F03-011 | Criar testes de autenticação | Completed |

Evidências na [especificação da Fase 03](specifications/FASE-03-IDENTIDADE.md#execução-e-ajustes-em-relação-ao-plano) e no [relatório](reports/FASE-03-RELATORIO.md).

## Tarefas da Fase 04

| ID | Tarefa | Estado |
|---|---|---|
| F04-001 | Criar organizações | Completed |
| F04-002 | Listar organizações do usuário | Completed |
| F04-003 | Selecionar organização ativa | Completed |
| F04-004 | Implementar membros | Completed |
| F04-005 | Implementar convites | Completed |
| F04-006 | Implementar aceitação de convites | Completed |
| F04-007 | Implementar papéis | Completed |
| F04-008 | Implementar permissões | Completed |
| F04-009 | Implementar autorização | Completed |
| F04-010 | Implementar remoção de membros | Completed |
| F04-011 | Implementar alteração de papéis | Completed |
| F04-012 | Criar testes de isolamento | In Review (local aprovado; CI pendente) |

Evidências na [especificação da Fase 04](specifications/FASE-04-ORGANIZACOES.md#execução-e-ajustes-em-relação-ao-plano) e no [relatório](reports/FASE-04-RELATORIO.md).

## Tarefas da Fase 05

| ID | Tarefa | Estado |
|---|---|---|
| F05-001 | Criar projetos | Completed |
| F05-002 | Listar projetos | Completed |
| F05-003 | Consultar detalhes | Completed |
| F05-004 | Editar projetos | Completed |
| F05-005 | Arquivar projetos | Completed |
| F05-006 | Gerenciar membros | Completed |
| F05-007 | Implementar papéis específicos de projeto | Completed |
| F05-008 | Implementar tags | Completed |
| F05-009 | Implementar repositórios associados | Completed |
| F05-010 | Implementar responsáveis | Completed |
| F05-011 | Implementar datas e prioridades | Completed |
| F05-012 | Criar testes | In Review (local aprovado; CI pendente) |

Evidências na [especificação da Fase 05](specifications/FASE-05-PROJETOS.md#execução-e-ajustes-em-relação-ao-plano) e no [relatório](reports/FASE-05-RELATORIO.md).

## Tarefas pendentes

- Merge do PR #3 (Fase 03) em `develop`: autorizado; o CI do último commit precisa ser executado de novo, porque falhou só na publicação do artefato.
- PR #4 (Fase 04): CI na fila do GitHub; merge depois do PR #3, com autorização.
- Fase 05: push de `feature/fase-05-projetos`, CI e PR para `develop` (com autorização), depois das Fases 03 e 04.

## Bloqueios

Nenhum bloqueio no desenvolvimento local. No GitHub:

- A execução [43095047963](https://github.com/harrisonleandro/Gestalyn/actions/runs/43095047963) do PR #3 terminou com falha. Restauração, build e testes passaram; só a etapa "Publicar resultados dos testes" (`actions/upload-artifact`) falhou, porque o serviço de artefatos respondeu HTML em vez de JSON nas 5 tentativas. É uma falha de infraestrutura do GitHub, mas o CI não está aprovado e o merge aguarda uma nova execução.
- Uma execução manual (`workflow_dispatch`) terminou com `startup_failure`, sem jobs, e o CI do PR #4 continua na fila.

## Testes executados

| Projeto | Testes | Cobertura |
|---|---|---|
| ProjectManager.UnitTests | 59 | Regras de dependência (7), domínio (42: entidades da Fase 02; organizações, papéis e convites; projetos, etiquetas, repositórios, arquivamento e acesso efetivo), CLI (5) e tokens (5) |
| ProjectManager.IntegrationTests | 77 | Persistência (7), isolamento por filtro (8), autenticação (22), organizações (17: fluxos, convites com e-mail real, permissões, isolamento com usuários reais), projetos (17: fluxos, visibilidade, papéis de projeto, equipe, cabeçalho de organização, isolamento, FKs compostas e migração de dados), health check (3), OpenAPI (2) e erros (2). Requer Docker (PostgreSQL e Mailpit). |

## Última validação

| Onde | Data (UTC) | Commit | Resultado |
|---|---|---|---|
| Local (Windows, SDK 10.0.404, Docker 29.8.2) | 2026-11-10 23:50 | `f803464` (`feature/fase-02-persistencia`) | restore exit 0 · build Release 0 avisos e 0 erros · testes 45/45 (23 unitários, 22 de integração com PostgreSQL 18) |
| GitHub Actions (`ubuntu-latest`), `main` | 2026-11-10 | `c6bd32a` | [run 42901411495](https://github.com/harrisonleandro/Gestalyn/actions/runs/42901411495): success |
| GitHub Actions (`ubuntu-latest`), `develop` | 2026-11-10 | `26e4b5c` | [run 42913460175](https://github.com/harrisonleandro/Gestalyn/actions/runs/42913460175): success, 12/12 e 6/6 |
| GitHub Actions (`ubuntu-latest`), `feature/fase-02-persistencia` | 2026-11-11 | `085d0f1` | [run 42927992315](https://github.com/harrisonleandro/Gestalyn/actions/runs/42927992315): success, 0 avisos, 23/23 e 22/22 (PostgreSQL via Testcontainers) |
| Local, `feature/marca-gestalyn` | 2026-11-11 | `ca0c337` + renomeação da marca | build Release 0 avisos e 0 erros · testes 45/45 · Web, API e CLI `gestalyn` executadas |
| Local, `feature/fase-03-identidade` | 2026-11-11 01:40 | `e70352c` | restore exit 0 · build Release 0 avisos e 0 erros · testes 71/71 (28 unitários, 43 de integração com PostgreSQL e Mailpit) · fluxos validados no navegador |
| GitHub Actions (`ubuntu-latest`), PR #3 | 2026-11-11 | `b5c4e3a` | [run 43094635052](https://github.com/harrisonleandro/Gestalyn/actions/runs/43094635052): success, 0 avisos, 28/28 e 43/43 (PostgreSQL e Mailpit via Testcontainers) |
| Local, `feature/fase-04-organizacoes` | 2026-11-11 20:41 | `c347fd9` | restore exit 0 · build Release 0 avisos e 0 erros · testes 95/95 (35 unitários, 60 de integração) · convite validado no navegador |
| GitHub Actions (`ubuntu-latest`), PR #3 | 2026-11-11 | `8b8a7b5` | [run 43095047963](https://github.com/harrisonleandro/Gestalyn/actions/runs/43095047963): **failure** só na publicação do artefato (serviço de artefatos do GitHub); restore, build e testes passaram |
| Local, `feature/fase-05-projetos` | 2026-11-11 21:40 | `c30da85` | restore exit 0 · build Release 0 avisos e 0 erros · testes 136/136 (59 unitários, 77 de integração) · API executada com usuários reais · migration aplicada no banco local |

## Decisões recentes

| Data | Decisão | Registro |
|---|---|---|
| 2026-11-10 | Monólito modular em camadas, com regras de dependência verificadas por teste | [ADR-001](decisions/ADR-001-MODULAR-MONOLITH.md) |
| 2026-11-10 | Especificação mestre adotada como fonte de verdade | [ESPECIFICACAO-MESTRE.md](specifications/ESPECIFICACAO-MESTRE.md) |
| 2026-11-10 | MCP acessa casos de uso via Application ou contratos próprios; Worker via Application e Infrastructure | [ADR-001, revisão](decisions/ADR-001-MODULAR-MONOLITH.md#revisão-de-2026-11-10-alinhamento-com-a-especificação-mestre) |
| 2026-11-10 | Multi-tenancy: banco e schema compartilhados, `OrganizationId`, UUID e validação no backend | [ARCHITECTURE.md §3](architecture/ARCHITECTURE.md) |
| 2026-11-10 | Fluxo de branches `main`/`develop`/`feature/*`; CI em `main` e `develop` | [PROJECT_STRUCTURE.md](architecture/PROJECT_STRUCTURE.md#branches) |
| 2026-11-10 | OpenAPI exposto apenas em Development | [ARCHITECTURE.md §5.1](architecture/ARCHITECTURE.md) |
| 2026-11-10 | Fase 02: entidades mínimas Organization, Project e ProjectPhase (D1, aprovada) | [FASE-02-PERSISTENCIA.md](specifications/FASE-02-PERSISTENCIA.md) |
| 2026-11-10 | EF Core + Npgsql, `snake_case`, UUID v7, `xmin`, migrations explícitas | [ADR-002](decisions/ADR-002-PERSISTENCIA-EF-CORE-POSTGRESQL.md) |
| 2026-11-10 | Isolamento em três camadas (filtro, validação de gravação, FK composta); RLS avaliado e adiado | [ADR-003](decisions/ADR-003-ISOLAMENTO-MULTI-TENANT.md) |
| 2026-11-10 | Testes de integração com PostgreSQL real (Testcontainers) | [ADR-004](decisions/ADR-004-TESTES-COM-POSTGRESQL.md) |
| 2026-11-11 | Fase 02 integrada em `develop` (PR #1, merge `ca0c337`) | [harrisonleandro/Gestalyn#1](https://github.com/harrisonleandro/Gestalyn/pull/1) |
| 2026-11-11 | Nome comercial **Gestalyn** e CLI `gestalyn`; código mantém `ProjectManager.*` | [ADR-005](decisions/ADR-005-NOME-COMERCIAL-GESTALYN.md) |
| 2026-11-11 | Marca integrada em `develop` (PR #2, merge `a8dae04`) | [harrisonleandro/Gestalyn#2](https://github.com/harrisonleandro/Gestalyn/pull/2) |
| 2026-11-11 | Fase 03: JWT de 15 min com sessão revogável, refresh token com rotação e detecção de reuso, Web como BFF, confirmação de e-mail obrigatória (D1 a D5) | [ADR-006](decisions/ADR-006-AUTENTICACAO-E-SESSAO.md) |
| 2026-11-11 | E-mail via SMTP (MailKit), Mailpit em desenvolvimento e testes | [ADR-007](decisions/ADR-007-ENVIO-DE-EMAIL.md) |
| 2026-11-11 | Organização ativa pela rota, validada pela associação; `MembershipDirectory` como única leitura sem filtro; RLS para a Fase 13 | [ADR-008](decisions/ADR-008-ORGANIZACAO-ATIVA.md) |
| 2026-11-11 | Papéis de sistema por organização, autorização por permissão, convites vinculados ao e-mail (7 dias, uso único) | [ADR-009](decisions/ADR-009-PAPEIS-PERMISSOES-E-CONVITES.md) |
| 2026-11-11 | Fase 05: visibilidade por equipe, papéis fixos de projeto, equipe só com membros da organização (FK com cascata), responsável único, repositórios só como metadados, etiquetas por projeto e cabeçalho `X-Gestalyn-Organization` (D1 a D5) | [ADR-010](decisions/ADR-010-PROJETOS-E-EQUIPES.md) |

## Próxima atividade

1. Executar de novo o CI do PR #3 e, aprovado, fazer o merge em `develop` (já autorizado).
2. CI do PR #4 e merge em `develop` (com autorização). Com o CI aprovado, a Fase 04 passa a Completed.
3. Push de `feature/fase-05-projetos`, CI e PR para `develop` (com autorização). Com o CI aprovado e as alterações integradas, a Fase 05 passa a Completed.
4. Fase 06 (fases, marcos e tarefas), somente após autorização.
