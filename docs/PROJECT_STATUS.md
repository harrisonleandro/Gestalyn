# Estado do projeto

> Controle de progresso exigido pela seção 15 da [especificação mestre](specifications/ESPECIFICACAO-MESTRE.md).
> Estados permitidos: Not Started · Planned · In Progress · Blocked · In Review · Completed.
> Uma tarefa só fica `Completed` quando seus critérios são verificados.

**Atualizado em:** 2026-11-10

## Fase atual

**Fase 01, Fundação da solução .NET: Completed.** A Fase 02 só começa com autorização explícita do responsável.

## Estado das fases

| Fase | Nome | Estado | Especificação | Relatório |
|---|---|---|---|---|
| 01 | Fundação da solução .NET | Completed | [FASE-01-FUNDACAO.md](specifications/FASE-01-FUNDACAO.md) | [FASE-01-RELATORIO.md](reports/FASE-01-RELATORIO.md) |
| 02 | Persistência e modelo de domínio | Not Started | — | — |
| 03 | Identidade e autenticação | Not Started | — | — |
| 04 | Organizações e permissões | Not Started | — | — |
| 05 | Projetos e equipes | Not Started | — | — |
| 06 | Fases, marcos e tarefas | Not Started | — | — |
| 07 | Workflow configurável e histórico | Not Started | — | — |
| 08 | Auditoria, documentação e dashboards | Not Started | — | — |
| 09 | Interface web completa | Not Started | — | — |
| 10 | API e autenticação de agentes | Not Started | — | — |
| 11 | CLI projectctl | Not Started | — | — |
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

## Tarefas pendentes

- Confirmar se **Gestalyn** (nome do repositório) é o nome comercial; a especificação mestre o mantém como "pendente de definição".

## Bloqueios

Nenhum bloqueio técnico. O início da Fase 02 aguarda autorização do responsável.

## Testes executados

| Projeto | Testes | Cobertura |
|---|---|---|
| ProjectManager.UnitTests | 12 | Regras de dependência entre projetos (7) e CLI (5) |
| ProjectManager.IntegrationTests | 6 | Health check (2), OpenAPI por ambiente (2) e Problem Details 404/500 (2) |

## Última validação

| Onde | Data (UTC) | Commit | Resultado |
|---|---|---|---|
| Local (Windows, SDK 10.0.404) | 2026-11-10 22:15 | `c6bd32a` + alterações documentais em `develop` | restore exit 0 · build Release 0 avisos e 0 erros · testes 18/18 |
| GitHub Actions (`ubuntu-latest`), `main` | 2026-11-10 | `c6bd32a` | [run 42901411495](https://github.com/harrisonleandro/Gestalyn/actions/runs/42901411495): success |
| GitHub Actions (`ubuntu-latest`), `develop` | 2026-11-10 | `26e4b5c` | [run 42913460175](https://github.com/harrisonleandro/Gestalyn/actions/runs/42913460175): success, 12/12 e 6/6 |

## Decisões recentes

| Data | Decisão | Registro |
|---|---|---|
| 2026-11-10 | Monólito modular em camadas, com regras de dependência verificadas por teste | [ADR-001](decisions/ADR-001-MODULAR-MONOLITH.md) |
| 2026-11-10 | Especificação mestre adotada como fonte de verdade | [ESPECIFICACAO-MESTRE.md](specifications/ESPECIFICACAO-MESTRE.md) |
| 2026-11-10 | MCP acessa casos de uso via Application ou contratos próprios; Worker via Application e Infrastructure | [ADR-001, revisão](decisions/ADR-001-MODULAR-MONOLITH.md#revisão-de-2026-11-10-alinhamento-com-a-especificação-mestre) |
| 2026-11-10 | Multi-tenancy: banco e schema compartilhados, `OrganizationId`, UUID e validação no backend | [ARCHITECTURE.md §3](architecture/ARCHITECTURE.md) |
| 2026-11-10 | Fluxo de branches `main`/`develop`/`feature/*`; CI em `main` e `develop` | [PROJECT_STRUCTURE.md](architecture/PROJECT_STRUCTURE.md#branches) |
| 2026-11-10 | OpenAPI exposto apenas em Development | [ARCHITECTURE.md §5.1](architecture/ARCHITECTURE.md) |

## Próxima atividade

Fase 02 (Persistência e modelo de domínio), após autorização:

1. Etapa A: inspeção.
2. Etapa B: criar `docs/specifications/FASE-02-PERSISTENCIA.md`.
3. Registrar ADRs de persistência (EF Core + Npgsql, migrations, auditoria) e da estratégia de testes com PostgreSQL (Docker local e Testcontainers no CI).
