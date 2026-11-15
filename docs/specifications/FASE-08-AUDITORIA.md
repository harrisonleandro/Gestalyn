# Fase 08: Auditoria, documentação e dashboards

- **Estado:** In Review. Implementada e validada localmente (testes, mutação e execução real da API); faltam o CI no GitHub e a integração em `develop`. Decisões D1 a D8 aprovadas pelo responsável em 2026-11-15.
- **Referência:** [Especificação mestre](ESPECIFICACAO-MESTRE.md), seção 9 (Fase 08), seção 4.6 (auditoria e documentação), seções 5.6 e 5.7 (contratos) e seção 6.4 (relatório de trabalho)
- **Branch:** `feature/fase-08-auditoria` (a partir de `develop` `fe04718`, com as Fases 01 a 07 integradas)
- **Pré-requisito para implementar:** decisões aprovadas

## Objetivo

Dar visibilidade sobre o andamento dos projetos:
- um registro de atividades confiável, que identifica o autor e a origem de cada alteração;
- documentos e decisões técnicas por projeto;
- dashboards, progresso e estatísticas calculados a partir de dados reais, por regras documentadas.

A especificação mestre proíbe percentuais arbitrários de progresso.

## Escopo

1. **Eventos de atividade** (`ActivityEvent`), gerados pelo backend a cada alteração, na mesma transação da alteração (D1 e D2):
   - tarefas (F08-003);
   - projetos, planejamento e workflow (F08-004);
   - membros da organização e da equipe do projeto (F08-005).
2. **Registros manuais de atividade** (`ActivityLog`), por `POST /projects/{id}/activities` (D1).
3. **Autor e origem** de cada evento: pessoa ou agente, e por qual cliente (D3).
4. **Consulta e filtros** de atividades por projeto e por organização (D4, F08-012).
5. **Documentos do projeto** (`ProjectDocument`) em Markdown, com versões (D5, F08-006).
6. **Decisões** (`Decision`), com estados e substituição (D6, F08-007).
7. **Progresso** por projeto, fase e marco, por uma regra única e documentada (D7, F08-010).
8. **Dashboards** da organização e do projeto, e **estatísticas** do projeto (D8, F08-008, F08-009 e F08-011).
9. **Permissões novas** nos catálogos de projeto e de organização, com a migração dos papéis existentes.
10. **Testes:** eventos de cada área, imutabilidade, filtros, documentos, decisões, progresso, dashboards, permissões e isolamento cross-tenant.
11. **Documentação:** ADR-014, `docs/api/ACTIVITY.md`, `docs/api/DOCUMENTS.md`, `docs/api/DASHBOARDS.md` e atualização de ARCHITECTURE, README e AGENTS.md.

## Fora de escopo

- **Relatórios de trabalho de agentes** (seção 6.4: o que o agente declarou, o que a API aceitou, o que os testes e o CI confirmaram): Fase 10, com as credenciais de agente. O modelo de eventos já registra o tipo de autor e a origem, para que esses relatórios virem atividades depois.
- **Eventos de autenticação** (login, logout, renovação de sessão e redefinição de senha): ficam nos logs estruturados. A auditoria de segurança entra na revisão de logs e auditoria da Fase 13 (F13-018).
- **Tempo real** (SignalR para atividades e dashboards): só se a Fase 09 mostrar necessidade (seção 8.2).
- **Telas** de atividades, documentos, decisões e dashboards: Fase 09.
- **Arquivos como documentos** (PDF, DOCX): os documentos desta fase são Markdown. Os arquivos continuam como anexos das tarefas.
- **Política de retenção e exportação da auditoria:** Fase 14, com a operação.
- **Recriar eventos de alterações antigas:** os eventos começam nesta fase (D2). O histórico de estados da Fase 07 continua disponível e alimenta as estatísticas.

## Decisões (D1 a D8 aprovadas)

| ID | Proposta | Alternativas |
|---|---|---|
| **D1** Modelo de atividades | Duas entidades, como lista a especificação: <br>- **`ActivityEvent`**: o registro **automático e imutável** de uma alteração, gerado pelo backend; <br>- **`ActivityLog`**: o registro **manual** de uma atividade (por exemplo, uma reunião ou uma nota de acompanhamento), criado por `POST /projects/{id}/activities`. Também é imutável: uma correção é um novo registro. <br>`GET /projects/{id}/activities` mostra os dois numa só lista, em ordem cronológica, com o campo `source` (`event` ou `log`). <br>O evento guarda: organização, projeto e tarefa (opcionais), autor e tipo de autor, origem, ação (catálogo fixo, por exemplo `task.updated`), tipo e ID da entidade, um resumo legível e as alterações campo a campo (`changes`, JSON com `from` e `to`). | (a) Uma só tabela para os dois: mistura o que o sistema garante com o que alguém declarou, o que a seção 6.4 pede para distinguir. (b) `ActivityLog` como cabeçalho e `ActivityEvent` como itens: uma tabela a mais sem função. |
| **D2** Como os eventos são gerados | **Registro explícito nos serviços**, por um `IActivityRecorder` da Application, gravado no **mesmo `SaveChanges`** da alteração: ou os dois são gravados, ou nenhum. As alterações campo a campo saem dos valores originais e atuais do EF, pelos nomes do contrato da API. Campos longos (descrições e corpos de comentários) registram que mudaram, mas sem o conteúdo, para não copiar textos inteiros para a auditoria. <br>**Cobertura garantida por teste:** um teste de integração percorre todas as rotas de escrita (POST, PUT, PATCH e DELETE) e falha se alguma não gerar evento ou não estiver numa lista explícita de exceções (por exemplo, autenticação). <br>**Sem eventos retroativos:** a migration não inventa eventos para dados antigos. | (a) Interceptor genérico do EF, que registra toda linha alterada: captura ruído técnico (`xmin`, contadores, tabelas de junção) e perde a intenção ("aprovou", "removeu da equipe"). (b) Eventos assíncronos, por fila ou Worker: exige infraestrutura nova e abre janela de perda. |
| **D3** Autor e origem | **Autor:** sempre o `sub` do token (pessoa nesta fase; agente na Fase 10, pelo tipo da credencial), nunca um valor do corpo. <br>**Origem:** `Web`, `Cli`, `Mcp`, `Api` ou `System`. A Web (BFF) passa a se identificar pelo cabeçalho **`X-Gestalyn-Client`**, e a CLI fará o mesmo quando chamar a API (Fase 11); sem o cabeçalho, ou com um valor desconhecido, a origem é `Api`. `System` é reservado a migrations e rotinas internas. **A origem é declarada pelo cliente:** serve para entender o uso, não para autorizar, e a documentação diz isso. <br>Cada evento guarda também o **ID da requisição** (`TraceIdentifier`), para correlacionar com os logs. **Não guardamos IP nem User-Agent**, por minimização de dados pessoais; isso pode ser revisto na Fase 13. | (a) Origem por credencial de cliente (um `client_id` por aplicação): a Web e a CLI usam o mesmo login hoje, e um segredo dentro da CLI não seria segredo. Fica para avaliar com os agentes (Fase 10). (b) Guardar IP e User-Agent: dado pessoal sem uso definido nesta fase. |
| **D4** Leitura, filtros e imutabilidade | **Projeto:** `GET /projects/{id}/activities` exige `project.read`. **Organização:** `GET /organizations/{id}/activities` traz os eventos da organização (membros, papéis, convites e dados da organização) e os de todos os projetos, e exige a **permissão nova `activity.read_all`** (Proprietário e Administrador). <br>**Filtros:** autor, tipo de autor, origem, ação, tipo de entidade, tarefa, período (`from`, `to`) e `source`; para a organização, também o projeto. Mais novos primeiro, paginados como as demais listas. <br>**Imutabilidade:** a API não tem rota de alteração nem de exclusão. No banco, um **gatilho recusa `UPDATE` e `DELETE`** em `activity_events` e `activity_logs`. É SQL bruto na migration, justificado no ADR-014. <br>**Dados pessoais:** e-mails nunca vão para os eventos. Um convite aparece pelo ID e pelo papel. | (a) Leitura da auditoria da organização com `members.read`: o Membro comum veria a atividade de projetos que não enxerga. (b) Imutabilidade só pela API: uma correção manual no banco apagaria rastros sem deixar sinal. |
| **D5** Documentos | **`ProjectDocument`**: título (até 200 caracteres), corpo em **Markdown** (até 200 KB), autor e arquivamento. **Cada edição cria uma versão imutável** (`ProjectDocumentVersion`), com autor e data. A edição exige a versão esperada; uma versão diferente responde 409, para que duas pessoas não sobrescrevam o trabalho uma da outra. <br>**Permissões novas:** `documents.write` (Gerente e Colaborador) cria e edita; `documents.manage` (Gerente) arquiva e restaura. A leitura exige `project.read`. <br>**Protegidos** quer dizer: acesso só pelo projeto, 404 para quem não o enxerga, isolamento por organização e Markdown guardado como veio. A Web (Fase 09) renderiza com sanitização, sem HTML cru. | (a) Documentos como arquivos no `IFileStorage`: sem versão, busca ou edição pela API, e os anexos de tarefas já cobrem arquivos. (b) Sem versões, só a última: um erro de edição apagaria conteúdo sem volta. |
| **D6** Decisões | **`Decision`**, um registro no estilo de ADR por projeto: título, contexto, decisão, alternativas e consequências (Markdown), tarefa relacionada opcional, autor e datas. <br>**Estados fixos:** `Proposed` → `Accepted` ou `Rejected`; `Accepted` → `Superseded`, apontando a decisão que a substitui (do mesmo projeto e já aceita). <br>**Edição:** só enquanto proposta. Decisão aceita, rejeitada ou substituída é imutável. <br>**Permissões:** `documents.write` propõe e edita propostas; `documents.manage` aceita, rejeita e substitui. Cada mudança de estado gera um evento. | (a) Decisões como documentos com uma marcação: perde os estados e a substituição, que são o que torna uma decisão rastreável. (b) Workflow configurável também para decisões: complexidade sem necessidade apresentada. |
| **D7** Regra de progresso | **Progresso = tarefas concluídas ÷ (tarefas − tarefas canceladas)**, contando só tarefas não arquivadas: <br>- **concluída** é a tarefa num estado da categoria `Done`; <br>- tarefas em `Canceled` saem da conta, porque não serão feitas; <br>- `Blocked`, `InProgress` e `Todo` contam como não concluídas; <br>- **sem tarefas na conta, o progresso é `null`** ("sem dados"), nunca 0% nem 100%. <br>A resposta traz **sempre as contagens por categoria** junto com o percentual, e o nome da regra (`doneOverNotCanceled`). A mesma regra vale para o projeto, cada fase e cada marco. **Sem pesos:** não há estimativas nem pontos no modelo, e usar a prioridade como peso seria arbitrário. | (a) Ponderar pela prioridade: arbitrário, que é o que a especificação proíbe. (b) Contar `InProgress` como meio concluída: o mesmo problema. (c) Média de progresso das fases: projetos com fases de tamanhos diferentes dariam números enganosos. |
| **D8** Dashboards e estatísticas | **Calculados na hora**, por consultas agregadas sobre os dados reais, sem tabelas de resumo nem cache nesta fase. <br>- **Organização:** a especificação prevê `GET /dashboard/overview`. Pela regra da organização ativa pela rota (ADR-008), a rota fica **`GET /organizations/{id}/dashboard`**, um ajuste de contrato registrado no ADR-014. Traz só os projetos que o usuário enxerga: projetos ativos com o progresso de cada um, tarefas abertas, atrasadas e concluídas nos últimos 7 dias, e as tarefas abertas do próprio usuário. <br>- **Projeto:** `GET /projects/{id}/dashboard` traz o progresso, tarefas por categoria e por estado, atrasadas, com prazo nos próximos 7 dias, bloqueadas, os próximos marcos com o progresso e as 10 atividades mais recentes. <br>- `GET /projects/{id}/progress`: o projeto, cada fase e cada marco, pela regra D7. <br>- `GET /projects/{id}/statistics?weeks=8` (de 1 a 26 semanas): tarefas criadas e concluídas por semana e o **tempo de ciclo** (mediana e percentil 85, em dias, entre a criação e a primeira entrada num estado `Done`). As datas de conclusão vêm do histórico de estados da Fase 07. <br>Leitura com `project.read`. As semanas são em UTC, de segunda a domingo. | (a) Tabelas de resumo atualizadas a cada alteração: mais rápido de ler, mas cria um segundo lugar para errar, e o volume atual não justifica. Medir na Fase 13. (b) Manter `/dashboard/overview` com a organização num cabeçalho: foge da regra que os demais endpoints da organização seguem. |

### Catálogo de ações (D1 e D2)

| Área | Ações |
|---|---|
| Organização | `organization.updated`, `member.invited`, `invitation.revoked`, `invitation.accepted`, `member.role_changed`, `member.removed` |
| Projeto | `project.created`, `project.updated` (inclui etiquetas), `project.archived`, `project.member_added`, `project.member_role_changed`, `project.member_removed`, `project.repository_added`, `project.repository_removed` |
| Planejamento | `phase.created`, `phase.updated`, `phase.reordered`, `phase.deleted`, `milestone.created`, `milestone.updated`, `milestone.deleted` |
| Tarefa | `task.created`, `task.updated`, `task.archived`, `task.assignee_added`, `task.assignee_removed`, `task.dependency_added`, `task.dependency_removed`, `task.transitioned`, `task.comment_added`, `task.comment_edited`, `task.comment_deleted`, `task.attachment_added`, `task.attachment_deleted` |
| Workflow | `workflow.updated`, `workflow.state_added`, `workflow.state_updated`, `workflow.state_moved`, `workflow.state_deleted`, `workflow.transition_added`, `workflow.transition_removed` |
| Documentos e decisões | `document.created`, `document.updated`, `document.archived`, `document.restored`, `decision.proposed`, `decision.updated`, `decision.accepted`, `decision.rejected`, `decision.superseded` |

Os nomes finais seguem as rotas que já existem; a lista exata fica no ADR-014 e em `docs/api/ACTIVITY.md`.

### Permissões novas

| Permissão | Catálogo | Papéis | Migração |
|---|---|---|---|
| `activity.read_all` | Organização | Proprietário e Administrador | Concedida aos papéis de sistema das organizações existentes, com teste (como `ProjectMigrationTests`) |
| `documents.write` | Projeto | Gerente e Colaborador | Não precisa: o catálogo de projeto é calculado pelo papel fixo |
| `documents.manage` | Projeto | Gerente | Não precisa |

### Decisões de baixo risco (serão registradas no ADR-014)

- **Transições:** geram um evento `task.transitioned` além da entrada em `TaskStatusHistory`, para que a lista de atividades seja completa. O histórico de estados continua sendo a fonte das estatísticas.
- **Projeto arquivado:** documentos, decisões e registros manuais ficam somente leitura, como o resto do projeto.
- **Registro manual (`ActivityLog`):** `{ kind, summary, details?, taskId?, occurredAt? }`, com `kind` em `Note`, `Meeting` ou `Other`, resumo de até 500 caracteres, detalhes em Markdown de até 10 KB e `occurredAt` não futuro. Exige `documents.write`.
- **Tamanho dos eventos:** `changes` limitado aos campos do contrato. Listas, como etiquetas, registram o que entrou e o que saiu.
- **Índices:** `(organization_id, occurred_at)`, `(project_id, occurred_at)` e `(task_id, occurred_at)`, para os filtros mais comuns.

## Contratos previstos (`/api/v1`)

| Método e rota | Permissão |
|---|---|
| `GET /projects/{projectId}/activities` (eventos e registros manuais, filtros de D4) | `project.read` |
| `POST /projects/{projectId}/activities` (registro manual) | `documents.write` |
| `GET /organizations/{organizationId}/activities` | `activity.read_all` |
| `GET /projects/{projectId}/documents` · `POST /projects/{projectId}/documents` | `project.read` · `documents.write` |
| `GET /documents/{documentId}` · `PUT /documents/{documentId}` (`{ title, body, expectedVersion }`) | `project.read` · `documents.write` |
| `GET /documents/{documentId}/versions` · `GET /documents/{documentId}/versions/{number}` | `project.read` |
| `POST /documents/{documentId}/archive` · `POST /documents/{documentId}/restore` | `documents.manage` |
| `GET /projects/{projectId}/decisions` · `POST /projects/{projectId}/decisions` | `project.read` · `documents.write` |
| `GET /decisions/{decisionId}` · `PATCH /decisions/{decisionId}` (só proposta) | `project.read` · `documents.write` |
| `POST /decisions/{decisionId}/accept` · `/reject` · `/supersede` (`{ supersededById }`) | `documents.manage` |
| `GET /organizations/{organizationId}/dashboard` | membro da organização (só os projetos que ele enxerga) |
| `GET /projects/{projectId}/dashboard` · `/progress` · `/statistics?weeks=` | `project.read` |

As rotas `/documents/{id}` e `/decisions/{id}` são por ID do recurso. Elas recebem a organização pelo cabeçalho `X-Gestalyn-Organization` e calculam o acesso pelo projeto do recurso (`[DocumentPermission]`, `[DecisionPermission]`), como `[TaskPermission]` (ADR-011).

## Dependências

| Item | Estado |
|---|---|
| Fase 07 (workflow, categorias, histórico de estados) | Completed, integrada em `develop` |
| Docker (PostgreSQL e Mailpit) | Instalado; precisa estar em execução para os testes de integração |
| Pacotes novos | Nenhum previsto |

## Tarefas

| ID | Tarefa | Subtarefas |
|---|---|---|
| F08-001 | Implementar `ActivityLog` | Registro manual, `POST` e validações; imutável |
| F08-002 | Implementar `ActivityEvent` | Entidade, `IActivityRecorder`, alterações campo a campo, autor, origem (`X-Gestalyn-Client`) e ID da requisição; gatilho de imutabilidade |
| F08-003 | Registrar alterações de tarefas | Todas as ações de tarefa do catálogo, inclusive transições, comentários e anexos |
| F08-004 | Registrar alterações de projetos | Projeto, equipe, etiquetas, repositórios, planejamento e workflow |
| F08-005 | Registrar ações de membros | Convites, aceitação, papel e remoção na organização; dados da organização |
| F08-006 | Implementar documentos | `ProjectDocument`, versões, concorrência pela versão esperada, arquivamento e permissões |
| F08-007 | Implementar decisões | `Decision`, estados, substituição, imutabilidade depois de decidida e permissões |
| F08-008 | Implementar dashboard geral | `GET /organizations/{id}/dashboard`, só com os projetos visíveis |
| F08-009 | Implementar dashboard de projeto | `GET /projects/{id}/dashboard` |
| F08-010 | Implementar cálculo de progresso | Regra D7 no domínio; projeto, fases e marcos |
| F08-011 | Implementar estatísticas | Criadas e concluídas por semana; tempo de ciclo |
| F08-012 | Implementar filtros de atividades | Filtros de D4, nos dois níveis, com paginação |
| F08-013 | Criar testes | Domínio, cobertura de eventos, imutabilidade, filtros, documentos, decisões, progresso, dashboards, permissões, migração e isolamento |

## Arquivos previstos

```
src/ProjectManager.Domain/Activity/            ActivityEvent, ActivityLog, ActivityAction (catálogo), ActivityOrigin
src/ProjectManager.Domain/Documents/           ProjectDocument, ProjectDocumentVersion, Decision, DecisionStatus
src/ProjectManager.Domain/Projects/            ProjectPermissions (documents.write, documents.manage), ProgressRule
src/ProjectManager.Domain/Organizations/       OrganizationPermissions (activity.read_all)
src/ProjectManager.Application/Activity/       IActivityRecorder, IActivityService, contratos
src/ProjectManager.Application/Documents/      IDocumentService, IDecisionService, contratos
src/ProjectManager.Application/Dashboards/     IDashboardService, contratos
src/ProjectManager.Infrastructure/Activity/    ActivityRecorder, ActivityService, cálculo das alterações
src/ProjectManager.Infrastructure/Documents/   DocumentService, DecisionService
src/ProjectManager.Infrastructure/Dashboards/  DashboardService (consultas agregadas)
src/ProjectManager.Infrastructure/*Service.cs  Chamadas ao IActivityRecorder em cada alteração
src/ProjectManager.Infrastructure/Persistence/Migrations/  Tabelas, gatilhos de imutabilidade e permissão nova nos papéis existentes
src/ProjectManager.API/Controllers/            ActivityController, DocumentsController, DecisionsController, DashboardController
src/ProjectManager.API/                        Leitura de X-Gestalyn-Client; atributos de documento e de decisão
src/ProjectManager.Web/Api/                    Envio de X-Gestalyn-Client: web
tests/ProjectManager.UnitTests/Domain/         Progresso, decisões, documentos, alterações campo a campo
tests/ProjectManager.IntegrationTests/Activity/, Documents/, Dashboards/
docs/decisions/ADR-014-AUDITORIA-DOCUMENTOS-E-DASHBOARDS.md
docs/api/ACTIVITY.md, DOCUMENTS.md, DASHBOARDS.md
```

## Critérios de aceitação

| Critério (especificação mestre) | Como será verificado |
|---|---|
| Histórico confiável | Toda rota de escrita gera evento (teste de cobertura); evento e alteração gravados juntos (uma alteração recusada não deixa evento); `UPDATE` e `DELETE` recusados pelo banco |
| Identificação do autor | O autor do evento é o do token, mesmo com outro ID no corpo; o tipo de autor é registrado |
| Identificação da origem | Chamadas com `X-Gestalyn-Client: web` e `cli` e sem o cabeçalho geram `Web`, `Cli` e `Api`; valor desconhecido vira `Api` |
| Progresso calculado a partir de regras documentadas | Testes unitários da regra D7 (canceladas fora da conta, `null` sem tarefas, arquivadas fora); a resposta traz as contagens e o nome da regra; o documento da API descreve a regra |
| Dashboard com dados reais | Testes de integração montam projetos, tarefas, transições e prazos conhecidos e conferem cada número; o dashboard da organização não conta projetos que o usuário não enxerga |
| Documentos protegidos | Matriz de permissões; 404 para quem não enxerga o projeto e entre organizações; edição com versão desatualizada responde 409; versões antigas preservadas |
| Auditoria consistente | Filtros conferidos com eventos conhecidos; `activity.read_all` concedida aos papéis existentes pela migração; isolamento cross-tenant de todas as tabelas novas |

## Estratégia de testes

- **Unitários:**
  - regra de progresso (D7) com todas as categorias, arquivadas e sem tarefas;
  - estados das decisões e substituição;
  - versões dos documentos;
  - cálculo das alterações campo a campo (inclusive campos longos sem conteúdo);
  - leitura da origem a partir do cabeçalho.
- **Integração** (PostgreSQL via Testcontainers):
  - **cobertura:** todas as rotas de escrita geram evento, exceto as da lista de exceções;
  - eventos de cada área, com autor, origem, ação e alterações corretos;
  - alteração recusada (403, 404 ou 409) não gera evento;
  - imutabilidade no banco (`UPDATE` e `DELETE` recusados);
  - filtros e paginação nos dois níveis;
  - documentos: versões, conflito de versão, arquivamento e permissões;
  - decisões: estados, substituição e imutabilidade;
  - progresso, dashboards e estatísticas com dados conhecidos;
  - **migração:** banco da Fase 07 com organizações, depois até o fim; os papéis de sistema recebem `activity.read_all`;
  - isolamento cross-tenant e FKs compostas das tabelas novas.
- **Mutação:** retirar o registro de evento de uma rota de escrita deve fazer o teste de cobertura falhar; contar as canceladas no denominador deve fazer o teste de progresso falhar.
- **Execução real:** API em execução, alterações feitas pela API com e sem `X-Gestalyn-Client`, atividades filtradas, documento editado em duas versões, decisão aceita e substituída e dashboards conferidos contra os dados.

## Riscos

| Risco | Mitigação |
|---|---|
| Alguma alteração esquecer de gerar evento | Teste de cobertura por rota; mutação que confirma que ele falha |
| Volume da tabela de eventos e custo das consultas | Índices por organização, projeto e tarefa; paginação obrigatória; medir na Fase 13 |
| Dashboards lentos com muitos dados, por serem calculados na hora | Consultas agregadas no banco, sem carregar entidades; tabelas de resumo só se as medições da Fase 13 pedirem |
| Origem declarada pelo cliente pode ser falsa | Documentada como informativa; o autor e o tipo de autor vêm sempre da credencial |
| Gatilho de imutabilidade dificulta correções legítimas | Correções viram novos registros; uma remoção excepcional exige migration revisada e ADR |
| Copiar dados pessoais para a auditoria | E-mails e textos longos fora dos eventos; teste que confere que nenhum evento de convite traz e-mail |
| Volume da fase | Commits por área: atividades e cobertura; documentos e decisões; progresso, dashboards e estatísticas |

## Definição de conclusão

- Todos os critérios verificados com evidência, local e no CI.
- Mutações do teste de cobertura e da regra de progresso detectadas.
- ADR-014 registrado.
- Documentação atualizada: `docs/api/ACTIVITY.md`, `DOCUMENTS.md`, `DASHBOARDS.md`, README, AGENTS.md, ARCHITECTURE, `PROJECT_STATUS.md` e relatório da Fase 08.
- Alterações integradas por PR, com o check "Build e testes" aprovado.

## Execução e ajustes em relação ao plano

| ID | Estado | Evidência |
|---|---|---|
| F08-001 | Completed | `ActivityLog` e `POST /projects/{id}/activities`; `ManualLogs_AreValidated_AndFollowProjectPermissions` |
| F08-002 | Completed | `ActivityEvent`, `ActivityRecorder` e `HttpActivityContext`; `Events_IdentifyTheAuthorFromTheToken_TheDeclaredOrigin_AndTheChangedFields`; gatilhos testados em `ActivityRecords_CannotBeChangedOrDeleted_EvenDirectlyInTheDatabase` |
| F08-003 | Completed | 13 ações de tarefa, conferidas uma a uma em `EveryWriteRoute_RecordsExactlyOneEvent` |
| F08-004 | Completed | Projeto, equipe, repositórios, planejamento e workflow no mesmo teste de cobertura |
| F08-005 | Completed | Convites, aceitação, papel e remoção no teste de cobertura; `OrganizationActivities_RequireThePermission_AreIsolated_AndCarryNoEmails` |
| F08-006 | Completed | `Editing_CreatesVersions_AndAnOutdatedVersionIsAConflict` (inclui duas edições simultâneas), `Documents_FollowProjectPermissions_AndArchivingMakesThemReadOnly` |
| F08-007 | Completed | `Decisions_MoveFromProposedToDecided_AndAreImmutableAfterwards`; `DocumentRulesTests` |
| F08-008 | Completed | `OrganizationDashboard_CountsOnlyTheProjectsTheUserSees`; mutação detectada |
| F08-009 | Completed | `ProjectDashboard_ShowsRealNumbers` |
| F08-010 | Completed | `ProgressRule`; `ProgressRuleTests` e `Progress_FollowsTheDocumentedRule_ForTheProjectPhasesAndMilestones`; mutação detectada |
| F08-011 | Completed | `Statistics_CountCreatedAndCompletedTasksPerWeek_AndTheCycleTime` (datas de criação ajustadas para tempos conhecidos) |
| F08-012 | Completed | `List_CombinesEventsAndManualLogs_WithFiltersAndPagination` |
| F08-013 | Completed | 30 casos unitários e 18 testes de integração novos; 4 mutações detectadas |
| CI | **In Review** | Depende do push de `feature/fase-08-auditoria`, autorizado pelo responsável |

Ajustes feitos durante a implementação, dentro das decisões aprovadas:

1. **Categoria no histórico de estados:** as estatísticas precisam saber se uma mudança antiga levou a um estado concluído, e a categoria atual de um estado pode ter mudado, ou o estado pode ter sido excluído. `TaskStatusHistory` ganhou `ToCategory`, gravado a cada transição. A migration `AddStatusHistoryCategory` preenche os registros existentes com a categoria atual do estado, a melhor informação disponível, e o teste de migração da Fase 07 confere o resultado.
2. **Paginação da lista combinada:** a união é feita só com o ID e a data de cada item (`UNION ALL`), e os detalhes da página são lidos depois, nas duas tabelas. Assim, as colunas que só um dos lados tem (`changes`, `details`) não entram na união.
3. **Ações do catálogo:** `project.member_updated` cobre a mudança de papel e de responsável na equipe; as etiquetas do projeto entram em `project.updated`; não há `project.restored`, porque a API não tem rota de restauração de projeto.
4. **Operações sem efeito não geram evento:** arquivar o que já está arquivado e um `PATCH` que não muda nenhum campo respondem com sucesso, sem evento. O teste de cobertura usa sempre operações com efeito.
5. **Dashboard da organização:** exige `organization.read`, concedida a todos os papéis de sistema.
6. **Registro manual:** o autor também vem do token e a origem do cabeçalho, como nos eventos.
