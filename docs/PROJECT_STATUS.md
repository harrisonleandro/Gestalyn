# Estado do projeto

> Controle de progresso exigido pela seção 15 da [especificação mestre](specifications/ESPECIFICACAO-MESTRE.md).
> Estados permitidos: Not Started · Planned · In Progress · Blocked · In Review · Completed.
> Uma tarefa só fica `Completed` quando seus critérios são verificados.

**Atualizado em:** 2026-11-10

## Fase atual

**Fase 04, Organizações e permissões: Planned.** A especificação aguarda aprovação das decisões D1 a D5, e a implementação depende da Fase 03 em `develop`. A **Fase 03 está Completed** (CI aprovado no PR #3); o merge do PR #3 aguarda autorização do responsável.

## Estado das fases

| Fase | Nome | Estado | Especificação | Relatório |
|---|---|---|---|---|
| 01 | Fundação da solução .NET | Completed | [FASE-01-FUNDACAO.md](specifications/FASE-01-FUNDACAO.md) | [FASE-01-RELATORIO.md](reports/FASE-01-RELATORIO.md) |
| 02 | Persistência e modelo de domínio | Completed | [FASE-02-PERSISTENCIA.md](specifications/FASE-02-PERSISTENCIA.md) | [FASE-02-RELATORIO.md](reports/FASE-02-RELATORIO.md) |
| 03 | Identidade e autenticação | Completed | [FASE-03-IDENTIDADE.md](specifications/FASE-03-IDENTIDADE.md) | [FASE-03-RELATORIO.md](reports/FASE-03-RELATORIO.md) |
| 04 | Organizações e permissões | Planned | [FASE-04-ORGANIZACOES.md](specifications/FASE-04-ORGANIZACOES.md) | — |
| 05 | Projetos e equipes | Not Started | — | — |
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

## Tarefas pendentes

- Integrar a Fase 03 em `develop` (PR #3, CI aprovado; merge com autorização).
- Aprovar as decisões D1 a D5 da Fase 04.

## Bloqueios

Nenhum bloqueio técnico. O merge do PR #3 aguarda autorização do responsável.

## Testes executados

| Projeto | Testes | Cobertura |
|---|---|---|
| ProjectManager.UnitTests | 28 | Regras de dependência entre projetos (7), invariantes do domínio (11), CLI (5) e tokens (5) |
| ProjectManager.IntegrationTests | 43 | Persistência (7), isolamento multi-tenant (8), autenticação (22: cadastro e login, sessão, recuperação de senha, segurança), health check com e sem banco (3), OpenAPI por ambiente (2) e Problem Details 404/500 (2). Requer Docker (PostgreSQL e Mailpit). |

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

## Próxima atividade

1. Merge do PR #3 em `develop` (CI aprovado), com autorização.
2. Fase 04 (organizações e permissões): implementar após o merge do PR #3 e a aprovação de D1 a D5, na branch `feature/fase-04-organizacoes`.
