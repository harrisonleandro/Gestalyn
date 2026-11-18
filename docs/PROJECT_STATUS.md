# Estado do projeto

> Controle de progresso exigido pela seção 15 da [especificação mestre](specifications/ESPECIFICACAO-MESTRE.md).
> Estados permitidos: Not Started · Planned · In Progress · Blocked · In Review · Completed.
> Uma tarefa só fica `Completed` quando seus critérios são verificados.

**Atualizado em:** 2026-11-15

## Fase atual

**Fase 10, API e autenticação de agentes: In Progress.** As decisões arquiteturais foram aprovadas pelo responsável em 2026-11-15: as 24 da revisão técnica e as 4 complementares (exibição única do token na Web, rate limiting no PostgreSQL, retenção e transição para Cancelada). A [especificação](specifications/FASE-10-AGENTES.md) e o [ADR-016](decisions/ADR-016-AGENTES.md) foram corrigidos depois da revisão técnica e **aprovados pelo responsável em 2026-11-18**. O [PR #18](https://github.com/harrisonleandro/Gestalyn/pull/18) foi integrado (merge `d6c34fc`) e a branch `feature/fase-10-agentes` foi rebaseada em `develop`; a implementação segue as etapas do plano da especificação. As etapas 1 a 4 estão implementadas em commits locais e passaram por uma revisão técnica independente em 2026-11-18, cujas correções (C1 a C4) também estão implementadas e testadas. As etapas 5 a 12 estão pendentes. **A autenticação de agentes não está pronta para produção**: faltam a auditoria persistente de operações, o último uso das credenciais e a proteção contra abuso de autenticação (etapa 10). A interface de agentes transferida da Fase 09 (F09-018) e o teste "agentes não aprovam o próprio trabalho" estão incluídos. O tempo real continua fora, sem necessidade comprovada.

**Fase 09, Interface web completa: Completed.** Validada localmente (372/372 testes, quatro mutações da interface detectadas e execução real no navegador com Gerente, Colaboradora e Observador, em 375 px e 1440 px). A interface de agentes (F09-018) foi transferida para a Fase 10 e o tempo real (F09-021) não foi implementado, por falta de necessidade comprovada (ADR-015). Validada também no CI ([run 43716683611](https://github.com/harrisonleandro/Gestalyn/actions/runs/43716683611): 0 avisos, 178/178, 43/43 e 151/151) e integrada em `develop` pelo [PR #17](https://github.com/harrisonleandro/Gestalyn/pull/17) (merge `5960bde`). A próxima é a Fase 10, que depende de autorização.

**Fase 08, Auditoria, documentação e dashboards: Completed.** Validada localmente (328/328 testes, quatro mutações detectadas, migrations aplicadas no banco de desenvolvimento e execução real da API com atividades, documentos, decisões e dashboards) e no CI ([run 43672967965](https://github.com/harrisonleandro/Gestalyn/actions/runs/43672967965): 0 avisos, 178/178 e 150/150), e integrada em `develop` pelo [PR #15](https://github.com/harrisonleandro/Gestalyn/pull/15) (merge `0e9dbdd`).

**Fase 07, Workflow configurável e histórico: Completed.** Validada localmente (280/280 testes, quatro mutações detectadas, migração de dados aplicada no banco de desenvolvimento e execução real da API com aprovação e dependências) e no CI ([run 43666361156](https://github.com/harrisonleandro/Gestalyn/actions/runs/43666361156): 0 avisos, 148/148 e 132/132), e integrada em `develop` pelo [PR #13](https://github.com/harrisonleandro/Gestalyn/pull/13) (merge `a6f87f9`).

**Fase 06, Fases, marcos e tarefas: Completed.** Validada localmente (195/195 testes, duas mutações detectadas e execução real da API com upload e download de anexo) e no CI ([run 43136119632](https://github.com/harrisonleandro/Gestalyn/actions/runs/43136119632): 0 avisos, 99/99 e 96/96), e integrada em `develop` pelo [PR #7](https://github.com/harrisonleandro/Gestalyn/pull/7) (merge `8025a23`).

**Fase 05, Projetos e equipes: Completed.** Validada localmente (136/136 testes, duas mutações detectadas e execução real da API com usuários reais) e no CI ([run 43107035853](https://github.com/harrisonleandro/Gestalyn/actions/runs/43107035853): 0 avisos, 59/59 e 77/77), e integrada em `develop` pelo [PR #5](https://github.com/harrisonleandro/Gestalyn/pull/5) (merge `6a32f58`).

**Fases 03 e 04: Completed e integradas em `develop`**: [PR #3](https://github.com/harrisonleandro/Gestalyn/pull/3) (merge `647573d`) e [PR #4](https://github.com/harrisonleandro/Gestalyn/pull/4) (merge `55926bc`), ambos com o CI aprovado.

**Correções da auditoria técnica de 2026-11-12: Completed.** A auditoria do `develop` (`7a5c170`) confirmou cinco problemas, todos corrigidos e integrados com o CI aprovado:

| Achado | Correção | Estado |
|---|---|---|
| P1: reuso de refresh token dentro da janela de 30 s (o invasor que usa o token primeiro mantém a sessão) | [PR #9](https://github.com/harrisonleandro/Gestalyn/pull/9), merge `cf5bcc0` ([ADR-012](decisions/ADR-012-ROTACAO-IDEMPOTENTE-DE-REFRESH-TOKEN.md)) | Completed |
| P2: nome em branco no PATCH responde 500 (projeto, fase, marco e tarefa) | [PR #10](https://github.com/harrisonleandro/Gestalyn/pull/10), merge `67c8059` | Completed |
| P2: página muito alta responde 500 (projetos e tarefas) | [PR #10](https://github.com/harrisonleandro/Gestalyn/pull/10), merge `67c8059` | Completed |
| P2: `main` e `develop` sem proteção no GitHub | Rulesets "Proteção de develop" e "Proteção de main" (2026-11-14) | Completed |
| P2: documentação desatualizada | [PR #11](https://github.com/harrisonleandro/Gestalyn/pull/11), merge `41cf693`, e este registro | Completed |

## Estado das branches

| Branch | Commit | Conteúdo |
|---|---|---|
| `main` | `c6bd32a` | Fase 01. Recebe `develop` só por PR de release, num marco definido pelo responsável. |
| `develop` | `d6c34fc` | Fases 01 a 09, correções da auditoria e registro da Fase 09 como concluída |

**Proteção** (rulesets ativos desde 2026-11-14, sem exceções de bloqueio), nas duas branches:
- não podem ser excluídas nem receber force push;
- só recebem mudanças por PR, com os comentários de revisão resolvidos;
- exigem o check "Build e testes", vindo do GitHub Actions;
- `main` também exige a branch atualizada antes do merge.

Não há aprovação de revisor obrigatória enquanto houver um único mantenedor.

## Estado das fases

| Fase | Nome | Estado | Especificação | Relatório |
|---|---|---|---|---|
| 01 | Fundação da solução .NET | Completed | [FASE-01-FUNDACAO.md](specifications/FASE-01-FUNDACAO.md) | [FASE-01-RELATORIO.md](reports/FASE-01-RELATORIO.md) |
| 02 | Persistência e modelo de domínio | Completed | [FASE-02-PERSISTENCIA.md](specifications/FASE-02-PERSISTENCIA.md) | [FASE-02-RELATORIO.md](reports/FASE-02-RELATORIO.md) |
| 03 | Identidade e autenticação | Completed | [FASE-03-IDENTIDADE.md](specifications/FASE-03-IDENTIDADE.md) | [FASE-03-RELATORIO.md](reports/FASE-03-RELATORIO.md) |
| 04 | Organizações e permissões | Completed | [FASE-04-ORGANIZACOES.md](specifications/FASE-04-ORGANIZACOES.md) | [FASE-04-RELATORIO.md](reports/FASE-04-RELATORIO.md) |
| 05 | Projetos e equipes | Completed | [FASE-05-PROJETOS.md](specifications/FASE-05-PROJETOS.md) | [FASE-05-RELATORIO.md](reports/FASE-05-RELATORIO.md) |
| 06 | Fases, marcos e tarefas | Completed | [FASE-06-TAREFAS.md](specifications/FASE-06-TAREFAS.md) | [FASE-06-RELATORIO.md](reports/FASE-06-RELATORIO.md) |
| 07 | Workflow configurável e histórico | Completed | [FASE-07-WORKFLOW.md](specifications/FASE-07-WORKFLOW.md) | [FASE-07-RELATORIO.md](reports/FASE-07-RELATORIO.md) |
| 08 | Auditoria, documentação e dashboards | Completed | [FASE-08-AUDITORIA.md](specifications/FASE-08-AUDITORIA.md) | [FASE-08-RELATORIO.md](reports/FASE-08-RELATORIO.md) |
| 09 | Interface web completa | Completed | [FASE-09-WEB.md](specifications/FASE-09-WEB.md) | [FASE-09-RELATORIO.md](reports/FASE-09-RELATORIO.md) |
| 10 | API e autenticação de agentes | In Progress | [FASE-10-AGENTES.md](specifications/FASE-10-AGENTES.md) | — |
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
| F04-012 | Criar testes de isolamento | Completed |

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
| F05-012 | Criar testes | Completed |

Evidências na [especificação da Fase 05](specifications/FASE-05-PROJETOS.md#execução-e-ajustes-em-relação-ao-plano) e no [relatório](reports/FASE-05-RELATORIO.md).

## Tarefas da Fase 06

| ID | Tarefa | Estado |
|---|---|---|
| F06-001 | Implementar fases | Completed |
| F06-002 | Implementar marcos | Completed |
| F06-003 | Implementar tarefas | Completed |
| F06-004 | Implementar múltiplos responsáveis | Completed |
| F06-005 | Implementar prioridades | Completed |
| F06-006 | Implementar prazos | Completed |
| F06-007 | Implementar comentários | Completed |
| F06-008 | Implementar etiquetas | Completed |
| F06-009 | Implementar dependências | Completed |
| F06-010 | Implementar anexos | Completed |
| F06-011 | Implementar arquivamento | Completed |
| F06-012 | Implementar filtros e paginação | Completed |
| F06-013 | Criar testes | Completed |

Evidências na [especificação da Fase 06](specifications/FASE-06-TAREFAS.md#execução-e-ajustes-em-relação-ao-plano) e no [relatório](reports/FASE-06-RELATORIO.md).

## Tarefas da Fase 07

| ID | Tarefa | Estado |
|---|---|---|
| F07-001 | Modelar o workflow por projeto | Completed |
| F07-002 | Implementar estados configuráveis | Completed |
| F07-003 | Implementar transições | Completed |
| F07-004 | Criar a configuração inicial e migrar os projetos existentes | Completed |
| F07-005 | Configurar o workflow pela API | Completed |
| F07-006 | Validar transições no backend | Completed |
| F07-007 | Implementar categorias e filtros por estado | Completed |
| F07-008 | Implementar estados finais e dependências na conclusão | Completed |
| F07-009 | Implementar aprovação e a regra do próprio trabalho | Completed |
| F07-010 | Implementar o histórico de estados | Completed |
| F07-011 | Tratar estado em uso, invariantes e concorrência | Completed |
| F07-012 | Criar testes | Completed |

Evidências na [especificação da Fase 07](specifications/FASE-07-WORKFLOW.md#execução-e-ajustes-em-relação-ao-plano) e no [relatório](reports/FASE-07-RELATORIO.md).

## Tarefas da Fase 08

| ID | Tarefa | Estado |
|---|---|---|
| F08-001 | Implementar ActivityLog | Completed |
| F08-002 | Implementar ActivityEvent | Completed |
| F08-003 | Registrar alterações de tarefas | Completed |
| F08-004 | Registrar alterações de projetos | Completed |
| F08-005 | Registrar ações de membros | Completed |
| F08-006 | Implementar documentos | Completed |
| F08-007 | Implementar decisões | Completed |
| F08-008 | Implementar dashboard geral | Completed |
| F08-009 | Implementar dashboard de projeto | Completed |
| F08-010 | Implementar cálculo de progresso | Completed |
| F08-011 | Implementar estatísticas | Completed |
| F08-012 | Implementar filtros de atividades | Completed |
| F08-013 | Criar testes | Completed |

Evidências na [especificação da Fase 08](specifications/FASE-08-AUDITORIA.md#execução-e-ajustes-em-relação-ao-plano) e no [relatório](reports/FASE-08-RELATORIO.md).

## Tarefas da Fase 09

| ID | Tarefa | Estado |
|---|---|---|
| F09-001 | Definir design system | Completed |
| F09-002 | Criar layout principal | Completed |
| F09-003 | Criar navegação | Completed |
| F09-004 | Implementar autenticação visual | Completed |
| F09-005 | Implementar seleção de organização | Completed |
| F09-006 | Implementar dashboard | Completed |
| F09-007 | Implementar lista de projetos | Completed |
| F09-008 | Implementar detalhes de projeto | Completed |
| F09-009 | Implementar gestão de membros | Completed |
| F09-010 | Implementar quadro de tarefas | Completed |
| F09-011 | Implementar lista de tarefas | Completed |
| F09-012 | Implementar detalhes de tarefa | Completed |
| F09-013 | Implementar configuração de workflow | Completed |
| F09-014 | Implementar fases e marcos | Completed |
| F09-015 | Implementar atividades | Completed |
| F09-016 | Implementar documentos e decisões | Completed |
| F09-017 | Implementar configurações | Completed |
| F09-018 | Implementar interface de agentes | Transferida para a Fase 10 (ADR-015, D7) |
| F09-019 | Implementar estados de carregamento e erro | Completed |
| F09-020 | Implementar responsividade e acessibilidade | Completed |
| F09-021 | Implementar atualizações em tempo real quando justificadas | Não implementada: sem necessidade comprovada (ADR-015, D2) |

Evidências na [especificação da Fase 09](specifications/FASE-09-WEB.md#execução-e-ajustes-em-relação-ao-plano) e no [relatório](reports/FASE-09-RELATORIO.md).

## Tarefas pendentes

- Fase 10: etapas 5 a 12 do plano da [especificação](specifications/FASE-10-AGENTES.md#plano-de-implementação), aprovada em 2026-11-18. As etapas 1 a 4 e as correções da revisão independente estão em commits locais, sem envio ao remoto.
- Fase 10, proteções obrigatórias da etapa 10, sem as quais os agentes não podem ser usados em produção:
  - `AgentOperation`: auditoria persistente de toda requisição de agente, inclusive as recusadas;
  - gravação de `last_used_at` nas credenciais;
  - proteção contra abuso de autenticação: limite de falhas por IP para tokens desconhecidos e malformados e rate limiting por credencial.


- Configurações recomendadas pela auditoria e ainda não aplicadas (dependem de decisão):
  - alertas e atualizações de segurança do Dependabot;
  - apagar a branch depois do merge;
  - permitir só actions oficiais do GitHub e de criadores verificados.

## Bloqueios

Nenhum bloqueio.

## Histórico de incidentes do CI

Incidentes já resolvidos, mantidos como referência:

- A execução [43095047963](https://github.com/harrisonleandro/Gestalyn/actions/runs/43095047963) do PR #3 falhou na primeira tentativa, só na publicação do artefato: o serviço de artefatos do GitHub respondeu HTML em vez de JSON. Na segunda tentativa, passou.
- A execução do PR #4 ([43097924272](https://github.com/harrisonleandro/Gestalyn/actions/runs/43097924272)) ficou presa em "queued": o GitHub recusou o cancelamento ("já concluída") e a nova execução ("já em execução"). A validação foi feita por uma execução manual sobre o mesmo commit (`b7b170e`), aprovada.
- Se a falha na publicação de artefatos voltar a ocorrer, avaliar `continue-on-error` nessa etapa (com aprovação do responsável).

## Testes executados

| Projeto | Testes | Cobertura |
|---|---|---|
| ProjectManager.UnitTests | 178 | Regras de dependência (7), domínio (entidades da Fase 02; organizações, papéis e convites; projetos, etiquetas, repositórios, arquivamento e acesso efetivo; fases, marcos, tarefas, comentários, ciclos de dependência, tipos e nomes de anexos; workflow, invariantes e regras de transição com pessoas e agentes; atividades, origem e alterações campo a campo; documentos, versões e decisões; regra de progresso, tempo de ciclo e semanas), validação de entrada e paginação, CLI e tokens |
| ProjectManager.WebTests | 43 | Componentes com bUnit: Markdown seguro (HTML cru, links perigosos, imagens), estados (carregando, erros por código, progresso sem dados), permissões refletidas na tela (menu, novo projeto, configurações do projeto, transições e comentários da tarefa), quadro (colunas, movimentos, aprovação) e mensagem de movimento recusado. API substituída por respostas falsas. |
| ProjectManager.IntegrationTests | 151 | Persistência (7), isolamento por filtro (8), autenticação (22), organizações (18: fluxos, convites com e-mail real, permissões, isolamento com usuários reais, papel e permissões do usuário), projetos (17: fluxos, visibilidade, papéis de projeto, equipe, cabeçalho de organização, isolamento, FKs compostas e migração de dados), fases, marcos e tarefas (planejamento, número sob concorrência, responsáveis, filtros, comentários, dependências e ciclos, anexos protegidos, permissões e isolamento), workflow (15: migração de dados, configuração e invariantes, transições, aprovação, dependências, estado final, histórico, concorrência e isolamento), atividades (10: cobertura de todas as rotas de escrita, autor e origem, operações recusadas, imutabilidade no banco, filtros, registros manuais, permissões, migração da permissão nova e isolamento), documentos e decisões (4), dashboards (4: progresso, painel do projeto, estatísticas e visão da organização), rotação de refresh token, health check (3), OpenAPI (2) e erros (2). Requer Docker (PostgreSQL e Mailpit). |

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
| GitHub Actions (`ubuntu-latest`), PR #3 | 2026-11-11 | `8b8a7b5` | [run 43095047963](https://github.com/harrisonleandro/Gestalyn/actions/runs/43095047963): falha na publicação do artefato na 1ª tentativa; **success** na 2ª |
| GitHub Actions (`ubuntu-latest`), `develop` | 2026-11-11 | `647573d` (merge do PR #3) | [run 43106099052](https://github.com/harrisonleandro/Gestalyn/actions/runs/43106099052): success |
| GitHub Actions (`ubuntu-latest`), `feature/fase-04-organizacoes` | 2026-11-11 | `b7b170e` (cabeça do PR #4) | [run 43106437450](https://github.com/harrisonleandro/Gestalyn/actions/runs/43106437450) (manual): success |
| GitHub Actions (`ubuntu-latest`), PR #5 | 2026-11-11 | `8659a93` | [run 43107035853](https://github.com/harrisonleandro/Gestalyn/actions/runs/43107035853): success, 0 avisos, 59/59 e 77/77 (PostgreSQL e Mailpit via Testcontainers) |
| GitHub Actions (`ubuntu-latest`), PR #7 | 2026-11-12 | `5cb5087` | [run 43136119632](https://github.com/harrisonleandro/Gestalyn/actions/runs/43136119632): success, 0 avisos, 99/99 e 96/96 (PostgreSQL e Mailpit via Testcontainers) |
| GitHub Actions (`ubuntu-latest`), `develop` | 2026-11-14 | `41cf693` (merges dos PRs #9, #10 e #11) | [run 43636303889](https://github.com/harrisonleandro/Gestalyn/actions/runs/43636303889): success, 0 avisos, 127/127 e 117/117 |
| GitHub Actions (`ubuntu-latest`), PR #9 | 2026-11-14 | `a83dd0e` | [run 43636088042](https://github.com/harrisonleandro/Gestalyn/actions/runs/43636088042): success, 0 avisos, 114/114 e 101/101 |
| GitHub Actions (`ubuntu-latest`), PR #10 | 2026-11-14 | `673425a` | [run 43636091061](https://github.com/harrisonleandro/Gestalyn/actions/runs/43636091061): success, 0 avisos, 112/112 e 112/112 |
| Local, branches da auditoria combinadas | 2026-11-14 | `3f8d20a` (verificação local) | build Release 0 avisos · testes 244/244 (127 unitários, 117 de integração) · 5 mutações detectadas · API executada com o cenário do invasor |
| GitHub Actions (`ubuntu-latest`), PR #13 | 2026-11-15 | `f3d000a` | [run 43666361156](https://github.com/harrisonleandro/Gestalyn/actions/runs/43666361156): success, 0 avisos, 148/148 e 132/132 (PostgreSQL e Mailpit via Testcontainers) |
| GitHub Actions (`ubuntu-latest`), PR #15 | 2026-11-15 | `558c1d8` | [run 43672967965](https://github.com/harrisonleandro/Gestalyn/actions/runs/43672967965): success, 0 avisos, 178/178 e 150/150 (PostgreSQL e Mailpit via Testcontainers) |
| GitHub Actions (`ubuntu-latest`), PR #17 | 2026-11-15 | `4407ff4` | [run 43716683611](https://github.com/harrisonleandro/Gestalyn/actions/runs/43716683611): success, 0 avisos, 178/178, 43/43 e 151/151 |
| Local, `feature/fase-09-web` | 2026-11-15 | `409d642` | restore exit 0 · build Release 0 avisos e 0 erros · testes 372/372 (178 unitários, 43 de componentes, 151 de integração) · 4 mutações da interface detectadas · Web e API executadas no navegador com três papéis, em 375 px e 1440 px |
| Local, `feature/fase-08-auditoria` | 2026-11-15 | `2d20219` | restore exit 0 · build Release 0 avisos e 0 erros · testes 328/328 (178 unitários, 150 de integração) · 4 mutações detectadas · migrations aplicadas no banco local · API executada com atividades, documentos, decisões e dashboards |
| Local, `feature/fase-07-workflow` | 2026-11-15 | `93e33e1` | restore exit 0 · build Release 0 avisos e 0 erros · testes 280/280 (148 unitários, 132 de integração) · 4 mutações detectadas · migration aplicada no banco local · API executada com aprovação, dependências e histórico |
| Local, `feature/fase-06-tarefas` | 2026-11-12 01:38 | `b452cdb` | restore exit 0 · build Release 0 avisos e 0 erros · testes 195/195 (99 unitários, 96 de integração) · API executada com upload e download de anexo · migrations aplicadas no banco local |
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
| 2026-11-11 | Fases 03 e 04 integradas em `develop` (PR #3, merge `647573d`; PR #4, merge `55926bc`) | [PR #3](https://github.com/harrisonleandro/Gestalyn/pull/3), [PR #4](https://github.com/harrisonleandro/Gestalyn/pull/4) |
| 2026-11-11 | Fase 05 integrada em `develop` (PR #5, merge `6a32f58`) | [PR #5](https://github.com/harrisonleandro/Gestalyn/pull/5) |
| 2026-11-11 | Fase 05: visibilidade por equipe, papéis fixos de projeto, equipe só com membros da organização (FK com cascata), responsável único, repositórios só como metadados, etiquetas por projeto e cabeçalho `X-Gestalyn-Organization` (D1 a D5) | [ADR-010](decisions/ADR-010-PROJETOS-E-EQUIPES.md) |

| 2026-11-12 | Fase 06 integrada em `develop` (PR #7, merge `8025a23`) | [PR #7](https://github.com/harrisonleandro/Gestalyn/pull/7) |
| 2026-11-14 | Rotação idempotente de refresh token: repetição só dentro de 10 s e com o sucessor ainda não usado; qualquer outro reuso revoga a sessão (auditoria, P1) | [ADR-012](decisions/ADR-012-ROTACAO-IDEMPOTENTE-DE-REFRESH-TOKEN.md), [PR #9](https://github.com/harrisonleandro/Gestalyn/pull/9) |
| 2026-11-14 | Proteção de `main` e `develop` por rulesets: PR obrigatório, CI "Build e testes" obrigatório, sem force push nem exclusão | Rulesets 27277722 e 27277724 |
| 2026-11-15 | Fase 07 integrada em `develop` (PR #13, merge `a6f87f9`) | [PR #13](https://github.com/harrisonleandro/Gestalyn/pull/13) |
| 2026-11-15 | Fase 08 integrada em `develop` (PR #15, merge `0e9dbdd`) | [PR #15](https://github.com/harrisonleandro/Gestalyn/pull/15) |
| 2026-11-18 | Fase 10, correções da revisão independente: política de agentes pela marca `[AgentApi]` conferida na inicialização; trava otimista no limite de credenciais no lugar da transação serializável; `CHECK` de escopos conhecidos; testes de isolamento e de recusas indistinguíveis | [ADR-016, revisão](decisions/ADR-016-AGENTES.md#revisão-de-2026-11-18-revisão-técnica-independente-das-etapas-1-a-4) |
| 2026-11-18 | Fase 09 registrada como Completed em `develop` (PR #18, merge `d6c34fc`) | [PR #18](https://github.com/harrisonleandro/Gestalyn/pull/18) |
| 2026-11-15 | Fase 09 integrada em `develop` (PR #17, merge `5960bde`) | [PR #17](https://github.com/harrisonleandro/Gestalyn/pull/17) |
| 2026-11-15 | Fase 10: agentes provisionados com `agents.manage`; credencial `gsk_` com hash; esquema `AgentToken` e políticas explícitas; `AgentAccessGrant`; agentes sem aprovação e sem entrar em Concluída (`TransitionRules`); idempotência transacional; `AgentOperation`; rate limiting no PostgreSQL; retenção; token exibido uma vez na Web (exceção à seção 7) | [ADR-016](decisions/ADR-016-AGENTES.md) |
| 2026-11-15 | Fase 09: Web SSR (BFF) com design system próprio sobre o Bootstrap, organização na URL, permissões da API refletidas na tela (`GET /organizations/{id}/me`), quadro por estado, Markdown com Markdig e CSP, testes com bUnit; agentes para a Fase 10 e sem tempo real (D1 a D8) | [ADR-015](decisions/ADR-015-INTERFACE-WEB.md) |
| 2026-11-15 | Fase 08: auditoria imutável com autor do token e origem declarada, eventos na mesma gravação com teste de cobertura por rota, documentos com versões, decisões no estilo de ADR, regra única de progresso e dashboards calculados na hora (D1 a D8) | [ADR-014](decisions/ADR-014-AUDITORIA-DOCUMENTOS-E-DASHBOARDS.md) |
| 2026-11-14 | Fase 07: workflow por projeto, categorias fixas com estados finais, transições explícitas, aprovação com `tasks.approve`, agentes nunca aprovam o próprio trabalho, histórico com os nomes na data e migração de dados (D1 a D7) | [ADR-013](decisions/ADR-013-WORKFLOW-CONFIGURAVEL.md) |
| 2026-11-12 | Fase 06: tarefas sem estado até o workflow, anexos em `IFileStorage` (disco local, tipos conferidos pelo conteúdo), fases e marcos do mesmo projeto, responsáveis da equipe, dependências sem ciclos, permissões de tarefa e número por projeto (D1 a D7) | [ADR-011](decisions/ADR-011-TAREFAS-E-ANEXOS.md) |

## Próxima atividade

1. Implementação da Fase 10 nas etapas do plano da [especificação](specifications/FASE-10-AGENTES.md#plano-de-implementação) (documentação aprovada em 2026-11-18), começando pela etapa 1: as políticas `User` e `Agent`.
