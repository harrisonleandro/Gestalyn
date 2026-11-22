# Fase 10: API e autenticação de agentes

- **Estado:** Completed em 2026-11-22. Validada localmente e no CI e integrada em `develop` pelo [PR #19](https://github.com/harrisonleandro/Gestalyn/pull/19) (merge `b9e8daa`). Em andamento de 2026-11-18 a 2026-11-22.
  - as 24 decisões da revisão;
  - as 4 decisões complementares (exibição do token, rate limiting, retenção e transição para Cancelada).

  A documentação corrigida depois da revisão técnica (esta especificação e o ADR-016, inclusive as escolhas técnicas que detalham as decisões) foi **aprovada pelo responsável em 2026-11-18**.
- **ADR:** [ADR-016](../decisions/ADR-016-AGENTES.md)
- **Referência:** [Especificação mestre](ESPECIFICACAO-MESTRE.md):
  - seção 9 (Fase 10);
  - seções 4.5, 4.6 e 4.7 (workflow, auditoria e inteligência artificial);
  - seção 5.8 (contratos de agentes);
  - seções 6.1, 6.4 e 6.5 (REST, relatório de trabalho, idempotência e concorrência);
  - seção 7 (segurança).
- **Branch:** `feature/fase-10-agentes`, rebaseada em `develop` `d6c34fc` (merge do [PR #18](https://github.com/harrisonleandro/Gestalyn/pull/18)) em 2026-11-18, antes de qualquer envio ao remoto.
- **Pré-requisito para implementar:** cumprido. A documentação foi aprovada em 2026-11-18, e a etapa 0 foi concluída no mesmo dia (merge do [PR #18](https://github.com/harrisonleandro/Gestalyn/pull/18), autorizado pelo responsável, e rebase).

## Objetivo

Permitir que agentes de IA operem no Gestalyn com segurança:
- com identidade e credenciais próprias, independentes dos usuários;
- com acesso limitado a projetos e escopos explícitos;
- com toda operação registrada e protegida contra duplicação.

Os agentes trabalham sobre as mesmas tarefas, atividades e histórico que as pessoas, pelas mesmas regras do backend. **Nunca aprovam tarefas** e **nunca entram em estados da categoria Concluída**: a conclusão é sempre de uma pessoa.

## Situação atual (verificada no código em 2026-11-15)

| Tema | Situação | Onde |
|---|---|---|
| Autorização global | `new AuthorizeFilter()` sem política nem esquema: vale o esquema padrão (JWT) | `src/ProjectManager.API/Program.cs:24`, `Authentication/ApiAuthentication.cs:18` |
| Sessão | O `OnTokenValidated` confere a sessão `sub`/`sid` a cada requisição | `ApiAuthentication.cs` |
| Organização ativa | Pela rota ou pelo cabeçalho; só para usuário com sessão (`TryGetSession`) e associação (`MembershipDirectory`) | `Organizations/ActiveOrganizationMiddleware.cs` |
| Filtro de organização | Filtro global por `CurrentOrganizationId`; `TenantIsolationRules.Enforce` na gravação | `Persistence/ProjectManagerDbContext.cs:111-131` |
| `IgnoreQueryFilters` | Só no `MembershipDirectory`, como o `AGENTS.md` e o ADR-008 exigem | `Organizations/MembershipDirectory.cs` |
| Acesso a projetos | `ProjectAccessGrant.Compute(ActiveMembership, ...)`, com `UserId`; os atributos exigem associação e cabeçalho | `Application/Projects/ProjectAccess.cs`, `API/Organizations/ProjectPermissionAttribute.cs:52-63` |
| Transições | `TransitionRules` recusa a aprovação só pela marcação `RequiresApproval` e pela regra do responsável. Um workflow pode ter uma transição direta para Concluída sem aprovação | `Domain/Workflows/TransitionRules.cs:39-50`, `Workflows/WorkflowService.cs:215` |
| Transições disponíveis | Calculadas com uma cópia da regra, fora de `TransitionRules` | `Tasks/TaskService.cs:838` |
| Ator nos registros | `ActorKind.User` fixo no histórico; `HttpActivityContext` sempre `User` | `TaskService.cs:88, 747, 776`, `API/Activity/HttpActivityContext.cs:30` |
| Autoria das tarefas | `created_by_user_id` obrigatório, com FK para `users`; o setter é privado | `Domain/Tasks/ProjectTask.cs:36, 63`, `Configurations/TaskConfigurations.cs:52-55` |
| Contratos de tarefa | `TaskDetails.CreatedByUserId` obrigatório na API e na Web | `Application/Tasks/TaskContracts.cs:215`, `Web/Api/ApiModels.cs:127` |
| Nomes dos autores | As atividades resolvem nomes só em `users` | `Infrastructure/Activity/ActivityService.cs:207` |
| Concorrência das tarefas | Sem token de concorrência. A transição confere `ExpectedStateId` lendo e depois gravando | `TaskService.cs:726` |
| Registro manual de atividade | Exige `documents.write` | `API/Controllers/ActivityController.cs:29` |
| Transações nos serviços | `AddDependencyAsync`, `ReorderPhasesAsync` e `SaveMemberAsync` abrem transação própria; criar, editar e mover tarefas não abrem | `TaskService.cs:600`, `PlanningService.cs:142`, `ProjectService.cs:444` |
| Imutabilidade | Gatilhos recusam `UPDATE`, `DELETE` e `TRUNCATE` nas tabelas de atividade | Migração `20261115023615_AddActivity.cs:133-149` |
| Cobertura de auditoria | O teste percorre **todas** as rotas de escrita da API | `IntegrationTests/Activity/ActivityCoverageTests.cs:196` |
| Tokens opacos | 32 bytes de `RandomNumberGenerator` (256 bits), SHA-256 em hexadecimal | `Infrastructure/Common/OpaqueTokens.cs:15-21` |
| Rate limiting | Só para a autenticação, por IP, em memória | `API/Authentication/AuthRateLimiting.cs` |
| Papéis personalizados | Modelo existe, sem endpoints | ADR-009, linha 35 |
| Health checks | Já existem na API | `API/Health/HealthEndpoints.cs` |

## Decisões aprovadas (2026-11-15)

| Nº | Decisão | Seção |
|---|---|---|
| 1 | Agentes pertencem a uma organização, não são membros e não acessam a Web | [D1](#d1-agente-e-provisionamento) |
| 2 | Só Proprietários e Administradores, com `agents.manage`, criam e administram agentes | [D1](#d1-agente-e-provisionamento) |
| 3 | Tokens opacos `gsk_`, exibidos uma única vez, guardados só como hash SHA-256 e prefixo | [D2](#d2-credenciais) |
| 4 | Expiração padrão de 90 dias, máxima de 365; até 3 credenciais ativas por agente | [D2](#d2-credenciais) |
| 5 | Esquema próprio `AgentToken`, separado da autenticação de usuários | [D3](#d3-autenticação-e-separação-das-políticas) |
| 6 | Tokens de agentes só em `/api/v1/agents/*`; a organização vem da credencial, nunca do cabeçalho | [D3](#d3-autenticação-e-separação-das-políticas) |
| 7 | Escopos `context:read`, `tasks:read`, `tasks:write`, `activities:write` e `reports:write` | [D4](#d4-escopos-acesso-a-projetos-e-agentaccessgrant) |
| 8 | `AgentProjectAccess` com papel Colaborador ou Observador; nunca Gerente | [D4](#d4-escopos-acesso-a-projetos-e-agentaccessgrant) |
| 9 | `AgentAccessGrant.Compute` centraliza a autorização | [D4](#d4-escopos-acesso-a-projetos-e-agentaccessgrant) |
| 10 | Agentes nunca aprovam, inclusive em workflows personalizados | [D5](#d5-regras-de-transição-para-agentes) |
| 11 | `created_by_agent_id`, com exclusividade entre autoria humana e de agente | [D6](#d6-autoria-e-versão-das-tarefas) |
| 12 | Histórico e eventos com `ActorKind.Agent` | [D6](#d6-autoria-e-versão-das-tarefas) |
| 13 | Rotas adicionais `GET /agents/tasks/{id}` e `POST /agents/tasks/{id}/transitions` | [D7](#d7-endpoints-de-agentes) |
| 14 | Transições pelas mesmas regras dos usuários, em `TransitionRules` | [D5](#d5-regras-de-transição-para-agentes) |
| 15 | `WorkReport` imutável, com os níveis separados | [D8](#d8-relatório-de-trabalho) |
| 16 | Testes e CI como não verificados nesta fase | [D8](#d8-relatório-de-trabalho) |
| 17 | `Idempotency-Key` em todos os `POST` de agentes | [D9](#d9-idempotência) |
| 18 | Mesma chave e mesmo corpo: a resposta original; outro corpo: 422; em processamento: 409 | [D9](#d9-idempotência) |
| 19 | Alterações de tarefa por agentes exigem `version` | [D6](#d6-autoria-e-versão-das-tarefas) |
| 20 | `AgentOperation` para cada requisição de agente, inclusive leituras e negadas | [D10](#d10-auditoria-de-operações-agentoperation) |
| 21 | Limite inicial de 120 requisições por minuto por credencial | [D11](#d11-rate-limiting-no-postgresql) |
| 22 | Telas Web de agentes, credenciais, acesso a projetos, operações e relatórios | [D13](#d13-interface-web-e-exibição-do-token) |
| 23 | SignalR fora desta fase | [D14](#d14-tempo-real) |
| 24 | Agentes não são responsáveis por tarefas nem escrevem comentários ou documentos | [Fora de escopo](#fora-de-escopo) |
| C4 | O token completo aparece na Web só na criação, uma vez, sob as condições da D13 | [D13](#d13-interface-web-e-exibição-do-token) |
| A6 | Rate limiting com contadores atômicos no PostgreSQL | [D11](#d11-rate-limiting-no-postgresql) |
| A6 | Retenção das operações e dos registros de idempotência, com limpeza por serviço em segundo plano na API | [D12](#d12-retenção-e-limpeza) |
| C2 | Agentes podem pedir a transição para Cancelada (sem aprovação, configurada, com acesso); nunca para Concluída | [D5](#d5-regras-de-transição-para-agentes) |

## Escopo

1. Agentes provisionados por pessoas com `agents.manage`, sem autorregistro (D1).
2. Credenciais com hash, prefixo, expiração, revogação imediata e rotação (D2).
3. Esquema `AgentToken` e políticas explícitas por esquema, com teste de todos os endpoints (D3).
4. Escopos, `AgentProjectAccess` e a regra única `AgentAccessGrant.Compute` (D4).
5. Regras de transição para agentes em `TransitionRules`, que passa a calcular também as transições disponíveis (D5).
6. Autoria de agentes, ator nos serviços e controle de versão das tarefas (D6).
7. Endpoints de agentes (D7) e de gestão por pessoas (ADR-016).
8. Relatórios de trabalho imutáveis, com os cinco níveis (D8).
9. Idempotência transacional (D9).
10. Auditoria de operações (D10).
11. Rate limiting no PostgreSQL (D11).
12. Retenção e limpeza controlada (D12).
13. Interface Web de agentes (D13), transferida da Fase 09 (F09-018).
14. Testes de segurança, concorrência, migração e ponta a ponta ([estratégia de testes](#estratégia-de-testes)).

## Fora de escopo

- **CLI** (Fase 11) e **servidor MCP** (Fase 12). Eles usarão estas credenciais e rotas.
- **Integração com GitHub e CI** (Fase 12), inclusive a tabela de verificações dos relatórios (D8). Nesta fase, os níveis "testes" e "CI" são sempre "não verificado".
- **Agente como responsável de tarefa**, comentários, anexos, dependências, documentos e decisões escritos por agentes (decisão 24). Os serviços dessas operações recusam um ator agente, como defesa em profundidade (D4).
- **Aprovação por agentes** e **entrada de agentes em estados Concluída** (D5).
- **OAuth, client credentials ou tokens assinados para agentes** (ADR-016).
- **Tempo real (SignalR)** (D14).
- **Projeto `ProjectManager.Worker`:** a limpeza roda num serviço em segundo plano da própria API (D12).
- **Exclusão de agentes:** agentes são desativados, nunca excluídos, para preservar a autoria.

## Decisões técnicas

### D1. Agente e provisionamento

- **`Agent`** (`IOrganizationOwned`, tabela `agents`):
  - nome único na organização, até 100 caracteres;
  - descrição, até 500;
  - estado `Active` ou `Disabled`;
  - quem criou e quando.
  - Chave alternativa `(organization_id, id)`, alvo das FKs compostas.
- **Quem cria e administra:** só pessoas com a permissão nova `agents.manage`:
  - ela entra em `OrganizationPermissions.All`; `Role.CreateSystemRoles` dá a permissão a Proprietário e Administrador nas organizações novas;
  - uma migração de dados a concede aos papéis de sistema Proprietário e Administrador das organizações existentes, com teste no modelo de `ProjectMigrationTests`;
  - a autorização é **pela permissão, nunca pelo nome do papel** (ADR-009). Hoje, só Proprietário e Administrador a têm, porque papéis personalizados não têm endpoints. Se ganharem, poderão receber `agents.manage` explicitamente.
- **Sem autorregistro:** nenhuma rota anônima cria agente ou credencial.
- **O agente não é membro da organização:**
  - não ocupa `organization_members` e não tem papel da organização;
  - não entra na Web: a política de usuário exige `sid` (D3), que o agente não tem;
  - só enxerga os projetos concedidos (D4).
- **Desativar** bloqueia todas as credenciais do agente na requisição seguinte. **Reativar** não ressuscita credenciais revogadas nem expiradas.

### D2. Credenciais

- **Formato:**
  - `gsk_` seguido de 43 caracteres base64url, que codificam 32 bytes gerados por `RandomNumberGenerator`, o gerador criptográfico do .NET: **256 bits de entropia**;
  - usa `OpaqueTokens.Generate`.
- **Armazenamento** (`agent_credentials`):
  - só o **SHA-256 do token completo** (hexadecimal, índice único) e o **prefixo**: os 8 primeiros caracteres depois de `gsk_`, que identificam a credencial nas listas e não bastam para usá-la;
  - também o nome (até 100 caracteres), os escopos (pelo menos um), a criação e o autor, a expiração, a revogação e o autor dela, e o último uso.
  - O token completo **nunca** é gravado.
- **Hash rápido:** com 256 bits aleatórios, um hash lento (PBKDF2 ou Argon2) só acrescenta custo. É o mesmo critério dos refresh tokens (ADR-006).
- **Expiração obrigatória:**
  - padrão de 90 dias, mínimo de 1 e máximo de 365;
  - em `Agents:Credentials` (`DefaultLifetimeDays`, `MaxLifetimeDays`), validados na inicialização;
  - a API recusa pedidos acima do máximo (400).
- **Até 3 credenciais ativas por agente** (não revogadas e não expiradas):
  - a contagem e a criação são protegidas por uma trava otimista na linha do próprio agente (`xmin`): a gravação atualiza também o agente, conferindo a versão lida, e uma criação simultânea para o mesmo agente responde 409;
  - criações para agentes diferentes não disputam nada entre si. Uma transação `Serializable`, prevista na primeira versão, fazia agentes de organizações diferentes receberem 409 sob concorrência (revisão independente de 2026-11-18).
- **Revogação imediata:** grava a data e o autor; a credencial deixa de valer na requisição seguinte, porque toda requisição a confere (D3).
- **Último uso:** gravado no máximo uma vez por minuto por credencial, aproveitando o contador do rate limiting (D11), sem uma gravação extra por requisição.
- **Perda do token:** não há recuperação. A pessoa cria uma credencial nova e revoga a antiga.

### D3. Autenticação e separação das políticas

- **Esquemas:**
  - o JWT **continua sendo o esquema padrão**;
  - o `AgentToken` é registrado como **esquema adicional**;
  - **não há seletor de esquema como padrão**. Um token `gsk_` numa rota de usuário falha na validação do JWT (401); um JWT numa rota de agente é ignorado pelo handler de agentes (401).
- **Políticas explícitas**, no lugar do `new AuthorizeFilter()` sem política:

  | Política | Esquema | Exige |
  |---|---|---|
  | `User` | JWT | Usuário autenticado e claim `sid` (sessão conferida pelo `OnTokenValidated`) |
  | `Agent` | `AgentToken` | Claim `actor_kind=agent` |

  - A política `Agent` vale para os controllers marcados com `[AgentApi]`; os demais recebem `User`. A marca é explícita, e não depende do namespace (revisão independente de 2026-11-18).
  - A marca é conferida contra a rota combinada de cada ação. Um controller marcado com rota fora de `/api/v1/agents`, ou uma rota sob esse prefixo sem a marca, impede a API de iniciar: os endpoints são montados na inicialização.
  - `[AllowAnonymous]` continua explícito e justificado.
  - **Teste de políticas:** percorre o `EndpointDataSource` e exige que todo endpoint `/api/...` tenha exatamente uma das três situações (`User`, `Agent` ou anônimo), e que `Agent` ocorra se e somente se a rota começar com `/api/v1/agents/`.
- **Handler `AgentToken`, a cada requisição:**
  1. Lê `Authorization: Bearer gsk_...`. Sem o prefixo, não se manifesta.
  2. Aplica o limite de falhas por IP (D11), antes de qualquer consulta.
  3. Calcula o SHA-256 e busca a credencial pelo `AgentCredentialDirectory`. É a segunda exceção autorizada ao uso de `IgnoreQueryFilters` (ADR-016): restrita à igualdade do hash e devolve só a credencial, o agente e a organização.
  4. **Token inexistente:** 401 genérico. Nada vai para `AgentOperation`, porque não há agente nem organização. Fica um log estruturado sem o token e sem o prefixo, e a falha conta para o limite por IP.
  5. **Credencial revogada, expirada ou de agente desativado:** o mesmo 401 genérico. A tentativa é registrada em `AgentOperation` na organização da credencial, com o resultado `CredentialRevoked`, `CredentialExpired` ou `AgentDisabled`.
  6. **Válida:** monta o principal com `sub` (agente), `org`, `cid` (credencial), `actor_kind=agent` e os escopos, e define a organização ativa do agente (`TenantContext.SetActiveAgent`). Toda leitura e gravação seguinte passa pelo filtro de organização.
- **O 401 é sempre igual:** Problem Details em pt-BR e `WWW-Authenticate: Bearer`, sem distinguir token inexistente, expirado, revogado ou de agente desativado.
- **Organização vinda só da credencial:**
  - `X-Gestalyn-Organization` e `{organizationId}` nunca definem a organização de um agente;
  - se vierem diferentes da organização da credencial, a resposta é 404, com o resultado `OrganizationMismatch` em `AgentOperation`;
  - o `ActiveOrganizationMiddleware` ganha o caminho do agente, sem `MembershipDirectory`.
- **Ordem do pipeline:**
  1. autenticação;
  2. rate limiting por credencial (D11);
  3. organização ativa;
  4. autorização por escopo e projeto (D4);
  5. idempotência (D9);
  6. ação.

  O registro em `AgentOperation` envolve tudo a partir da autenticação (D10).

### D4. Escopos, acesso a projetos e AgentAccessGrant

- **`AgentProjectAccess`** (tabela `agent_project_access`): organização, agente, projeto e papel.
  - O papel é `Contributor` ou `Viewer`; `Manager` é recusado (400).
  - FKs compostas `(organization_id, agent_id)` → `agents` e `(organization_id, project_id)` → `projects`; índice único `(agent_id, project_id)`.
  - **Quem concede ou remove:** a pessoa precisa de `agents.manage` na organização **e** de `project.members.manage` naquele projeto, pelo `ProjectAccessGrant` dela. Ninguém concede acesso a um projeto que não gerencia.
  - O acesso pertence ao agente: continua válido se quem concedeu perder a permissão ou sair da organização.
- **Escopos e permissões** (tabela única, sem outra fonte):

  | Escopo | Rotas de agente | Permissão de projeto exigida |
  |---|---|---|
  | (nenhum) | `GET /agents/me` | — |
  | `context:read` | `GET /agents/projects`, `GET /agents/projects/{id}/context` | `project.read` |
  | `tasks:read` | `GET /agents/projects/{id}/tasks`, `GET /agents/tasks/{id}` | `project.read` |
  | `tasks:write` | `POST /agents/tasks`, `PATCH /agents/tasks/{id}`, `POST /agents/tasks/{id}/transitions` | `tasks.write` |
  | `activities:write` | `POST /agents/activities` | `documents.write` (a mesma do registro manual por pessoas) |
  | `reports:write` | `POST /agents/reports` | `tasks.write` (Observador não envia relatórios) |

- **Permissões que um agente pode ter:** só `project.read`, `tasks.write` e `documents.write`, intersectadas com as do papel (`ProjectPermissions.For`). Colaborador recebe as três; Observador, só `project.read`. **`tasks.approve`, `project.update`, `tasks.comment`, `tasks.archive`, `workflow.manage` e `documents.manage` nunca entram.**
- **`AgentAccessGrant.Compute`** (em `Application/Projects`, ao lado de `ProjectAccessGrant`):
  - **recebe:**
    - o ator (agente, com `ActorKind.Agent`) e o estado dele;
    - os escopos da credencial;
    - o papel no projeto (ou nenhum);
    - a exigência da operação: escopo e permissão da tabela acima, e se é leitura ou escrita;
  - **devolve uma decisão:** `Allowed` com o `ProjectAccessGrant`, ou `ProjectNotVisible` (404), `ScopeMissing` (403) ou `PermissionMissing` (403). O motivo vai para `AgentOperation`.
  - **É a única regra de acesso de agentes.** O projeto arquivado continua tratado pelos serviços, como para pessoas: leitura permitida e escrita com 409. Não há cópia dessa regra.
- **`ProjectAccessGrant` passa a carregar o ator** (ID e tipo):
  - `UserId` continua para pessoas;
  - os serviços usam o ator como autor;
  - os serviços que gravam autoria com FK para `users` (comentários, dependências, anexos, documentos, decisões, responsáveis) recusam um ator agente (403), como defesa em profundidade.
- **Resolução:**
  - `IAgentProjectAccess` (`FindAsync`, `FindByTaskAsync`) carrega `AgentProjectAccess` e chama `Compute`;
  - o atributo `[AgentPermission(escopo, permissão)]` cobre as rotas com o recurso na URL;
  - um método estático equivalente cobre as rotas com o projeto no corpo (`POST /agents/tasks`, `/activities`, `/reports`), no padrão de `ProjectResourcePermissionAttribute.AuthorizeAsync`;
  - os atributos de pessoas continuam como estão.

### D5. Regras de transição para agentes

A regra fica em `TransitionRules.Evaluate`, a regra única da Fase 07, e não só nas rotas da API. Ordem da avaliação:

1. Transição inexistente no workflow: 409 (como hoje).
2. **Ator agente** (`actor.Kind == ActorKind.Agent`):
   - transição com `RequiresApproval`: **`AgentCannotApprove`** (403), qualquer que seja o responsável;
   - estado de destino da categoria `Done`: **`AgentCannotEnterDone`** (403), mesmo que a transição não exija aprovação, como num workflow personalizado com "Em andamento → Concluída".
3. Regras de pessoas: `tasks.approve` e o próprio trabalho (`IsAssignee` e `RequireIndependentApproval`), sem mudança.
4. Dependências abertas ao entrar em `Done`: 409 (como hoje).

Consequências:
- **Cancelada é permitida a agentes** quando a transição existe no workflow, não exige aprovação e o agente tem `tasks:write` e `tasks.write`. As demais regras de negócio continuam valendo.
- **Agentes podem levar a tarefa até um estado de revisão.** Uma pessoa faz a aprovação e a conclusão.
- **As transições disponíveis passam a ser calculadas por `TransitionRules`**, para pessoas e agentes. Isso remove a cópia da regra em `TaskService.cs:838`, com testes que preservam o comportamento das pessoas.
- **A rota de transição das pessoas** (`POST /tasks/{id}/transitions`) não muda.
- **Toda recusa a um agente** fica em `AgentOperation`, com o resultado `BusinessRuleDenied` e o código (`agent_cannot_approve`, `agent_cannot_enter_done`). Uma recusa não muda o estado nem grava histórico.

### D6. Autoria e versão das tarefas

- **Ator nos serviços:** os serviços recebem o ator (ID e `ActorKind`) pelo `ProjectAccessGrant` (D4).
  - `TaskService` deixa de fixar `ActorKind.User` (linhas 88, 747 e 776).
  - `HttpActivityContext` usa `ActorKind.Agent` quando o principal tem `actor_kind=agent`.
  - A origem (`X-Gestalyn-Client`) continua só informativa.
- **Migração de `tasks`, que preserva todas as tarefas existentes:**
  1. Adiciona `created_by_agent_id uuid NULL`.
  2. Torna `created_by_user_id` anulável. As linhas existentes continuam com o usuário e a FK para `users`.
  3. Cria a FK composta `(organization_id, created_by_agent_id)` → `agents (organization_id, id)`, `ON DELETE RESTRICT`.
  4. Cria `CHECK ck_tasks_single_author (num_nonnulls(created_by_user_id, created_by_agent_id) = 1)`. Todas as linhas existentes já atendem.
  5. Cria o gatilho `reject_task_author_change`: `BEFORE UPDATE OF created_by_user_id, created_by_agent_id` recusa qualquer mudança da autoria. É SQL escrito à mão na migração, como os gatilhos da Fase 08.
  6. Adiciona `version integer NOT NULL DEFAULT 1`. As tarefas existentes começam na versão 1.
- **Domínio:** `ProjectTask` ganha um construtor para cada tipo de autor; os dois campos têm setter privado e nenhum método os altera.
- **Histórico, eventos e atividades:** `task_status_history`, `activity_events` e `activity_logs` guardam o ID do agente com `actor_kind = 'Agent'`. Essas tabelas já não têm FK de autor (ADR-014).
- **Leitura:**
  - as respostas de tarefa, histórico e atividades ganham `createdBy` ou `actor`, com `{ id, kind, name }`; os nomes de agentes vêm de `agents`, da mesma organização;
  - `TaskDetails.CreatedByUserId` passa a anulável: é mudança de contrato, registrada no ADR-016. O único consumidor atual é a Web, ajustada na mesma etapa.
- **Versão:**
  - `version` é o token de concorrência da tarefa: o `UPDATE` inclui `WHERE version = @esperada`;
  - ela incrementa em toda alteração bem-sucedida da linha da tarefa (edição, inclusive só de etiquetas; transição; arquivamento; restauração), num ponto único do `SaveChanges`;
  - responsáveis, dependências, comentários e anexos são recursos próprios e não mudam a versão;
  - a versão divergente responde **409**, sem sobrescrever. O `DbUpdateConcurrencyException` vira 409 com a mensagem para recarregar.
- **Uso da versão:**
  - `PATCH /agents/tasks/{id}` e `POST /agents/tasks/{id}/transitions` **exigem** `expectedVersion` (400 sem ele);
  - `PATCH /tasks/{id}` de pessoas aceita `expectedVersion` opcional, e a Web passa a enviá-lo;
  - a transição de pessoas mantém `expectedStateId`, e o token de concorrência elimina a corrida entre a leitura e a gravação (`TaskService.cs:726`).

### D7. Endpoints de agentes

Todos ficam sob `/api/v1/agents`, com a política `Agent`. Os oito da seção 5.8 e dois ajustes documentados (decisão 13):

| Rota | Escopo e permissão | Comportamento |
|---|---|---|
| `GET /me` | — | `{ agentId, name, organizationId, credential { id, prefix, scopes, expiresAt } }` |
| `GET /projects` | `context:read` | Projetos concedidos, com o papel |
| `GET /projects/{id}/context` | `context:read` + `project.read` | Ver abaixo |
| `GET /projects/{id}/tasks` | `tasks:read` + `project.read` | Filtros e paginação da lista de pessoas; até 100 por página |
| `GET /tasks/{id}` (novo) | `tasks:read` + `project.read` | Detalhes, `version` e transições disponíveis para o agente (D5) |
| `POST /tasks` | `tasks:write` + `tasks.write` + `Idempotency-Key` | `projectId` no corpo; 201; autoria de agente |
| `PATCH /tasks/{id}` | `tasks:write` + `tasks.write` + `expectedVersion` | 200; 409 com versão divergente. Sem `Idempotency-Key`: a versão já protege |
| `POST /tasks/{id}/transitions` (novo) | `tasks:write` + `tasks.write` + `Idempotency-Key` + `expectedVersion` | Mesmo serviço e mesma `TransitionRules`. 200; 403 (`AgentCannotApprove`, `AgentCannotEnterDone`); 409 (sem transição, dependências ou versão) |
| `POST /activities` | `activities:write` + `documents.write` + `Idempotency-Key` | 201; registro manual com o ator agente |
| `POST /reports` | `reports:write` + `tasks.write` + `Idempotency-Key` | 201; D8 |

- **Contexto do projeto** (tamanho limitado, sem dados pessoais):
  - projeto: ID, nome, descrição, arquivado;
  - workflow: estados, com a categoria, e transições, com `requiresApproval` e se o agente pode executá-las;
  - fases e marcos, com o progresso (`ProgressRule`);
  - decisões aceitas: título e resumo, até 50;
  - documentos: ID, título e versão, sem o conteúdo, até 100;
  - as 20 atividades mais recentes: resumo e autor (tipo e nome).
  - **Ficam de fora** e-mails, a lista de membros e qualquer dado pessoal além do nome de quem aparece como autor.
- **Reuso:** todas as rotas usam os serviços existentes. Nenhuma regra de negócio é copiada para os controllers de agentes.
- **Rotas de gestão por pessoas e de leitura de relatórios:** estão no ADR-016.

### D8. Relatório de trabalho

- **`work_reports`** (imutável e permanente):
  - organização, projeto, tarefa (opcional; se vier, precisa ser do projeto), agente e credencial;
  - **declarado:**
    - `summary` (até 2.000 caracteres) e `changes` (Markdown, até 20.000);
    - `files` (até 200 caminhos de até 500 caracteres);
    - `tests` (comando de até 500 caracteres; aprovados, falhas e ignorados, inteiros não negativos);
    - `commit` (SHA hexadecimal de 40 ou 64 caracteres e repositório opcional de até 200);
    - `suggestedStatus` (ID de um estado do workflow do projeto);
  - **retrato do recebimento:** o estado da tarefa naquele momento (ID e nome) e o estado sugerido (ID e nome), além da data e do ID da requisição.
  - **Gatilho:** recusa `UPDATE`, `DELETE` e `TRUNCATE`, sem exceção de retenção.
- **Os cinco níveis da seção 6.4**, sempre separados:

  | Nível | Origem | Nesta fase |
  |---|---|---|
  | 1. Declarado | O conteúdo do relatório, sempre rotulado como declaração do agente | Gravado |
  | 2. Aceito | **Recebido e validado pela API**: projeto, tarefa, estado sugerido e tamanhos. Não significa aprovado por uma pessoa | Sempre `true` num 201; o que não passa na validação não é gravado (400 ou 422) |
  | 3. Testes executados | Registro de verificação separado e só de inclusão (`work_report_verifications`, Fase 12) | `notVerified`: a contagem do agente é declaração |
  | 4. Confirmado pelo CI | Idem | `notVerified` |
  | 5. Aprovado por uma pessoa | Calculado na leitura pelo histórico: a primeira entrada de `task_status_history` posterior ao relatório, com `actor_kind = 'User'` e `to_category = 'Done'`. Como agentes nunca entram em `Done` (D5), toda entrada assim é de uma pessoa | `approved` (com quem e quando), `pending` ou `notApplicable` (sem tarefa) |

  - As verificações posteriores **nunca alteram** o relatório: ficam numa tabela própria, só de inclusão.
  - A tabela `work_report_verifications` será criada na Fase 12, quando existir quem grave. Sem verificação, o nível é `notVerified`.
- **O relatório não muda o estado da tarefa.** Para isso, o agente usa a rota de transição (D5).
- **Resposta (201):**

  ```json
  {
    "reportId": "…",
    "accepted": true,
    "activityCreated": "<id do ActivityEvent>",
    "taskStatus": { "stateId": "…", "name": "…", "category": "…" },
    "approvalRequired": true,
    "evidence": {
      "declared": true,
      "accepted": true,
      "testsExecuted": "notVerified",
      "ciConfirmed": "notVerified",
      "humanApproval": "pending"
    }
  }
  ```

  - `approvalRequired` vale `true` ou `false` se existe uma transição direta do estado atual até o sugerido, conforme ela exija aprovação.
  - Vale `null` sem estado sugerido ou sem transição direta, e então `suggestedTransition` é `"none"`. Com transição direta, `suggestedTransition` é `"direct"` (valor fixado na etapa 9).
  - Tarefa de outro projeto, tarefa inexistente e estado sugerido de outro workflow são erros de validação (400); projeto arquivado, 409 (etapa 9).
  - Se o estado sugerido for da categoria `Done`, a conclusão será sempre de uma pessoa (D5).
- **Leitura por pessoas** (`project.read`):
  - `GET /api/v1/projects/{id}/reports`;
  - `GET /api/v1/tasks/{id}/reports`;
  - `GET /api/v1/reports/{id}`, com o cabeçalho de organização e `[ReportPermission]` no modelo de `[TaskPermission]`.
- **Auditoria:** o recebimento gera o evento `work_report.received`, com o ator agente, dentro da mesma transação.

### D9. Idempotência

- **Onde vale:** em todos os `POST` de agentes (tarefas, transições, atividades e relatórios).
- **Chave:** `Idempotency-Key` de 1 a 100 caracteres ASCII visíveis (0x21 a 0x7E); ausente ou inválida, 400.
- **`idempotency_records`** (`IOrganizationOwned`):
  - organização, agente e **credencial**;
  - chave, **método HTTP** e **modelo da rota**;
  - **SHA-256 do caminho concreto, da query e dos bytes do corpo** (etapa 8: o caminho entra no hash para que a mesma chave e o mesmo corpo em outra tarefa sejam outra requisição, com 422);
  - projeto afetado (etapa 8: a tarefa não é guardada; ela nunca muda de projeto, e as rotas por tarefa já reavaliam o acesso pela tarefa antes da idempotência);
  - código e corpo da resposta (até 256 KB, ajustado na etapa 8: o detalhe de uma tarefa com a descrição máxima de 20.000 caracteres pode passar de 64 KB depois do escape JSON);
  - criação e expiração lógica (24 horas).
  - Índice único `(credential_id, key)`.
- **Fluxo**, depois da autenticação, do rate limiting e da autorização da rota:
  1. Abre **uma transação** e tenta `pg_try_advisory_xact_lock(hash(credential_id, key))`. Se a trava não vier, há outra requisição com a mesma chave em processamento: **409**, sem esperar.
  2. Procura o registro não expirado da chave. Um registro expirado é tratado como inexistente e substituído.
  3. **Registro encontrado:**
     - método, rota ou hash diferentes: **422**;
     - iguais: **reavalia a autorização**: `AgentAccessGrant.Compute` com os escopos e o acesso atuais, para o projeto e a tarefa guardados. Negada: responde a negação atual (403 ou 404) e **não devolve** a resposta guardada. Permitida: devolve o código e o corpo guardados, com `Idempotency-Replayed: true`.
  4. **Sem registro:** executa a operação **na mesma transação**, grava o registro com a resposta e faz o commit. A operação e o registro entram juntos ou não entram.
- **Atomicidade:**
  - uma queda antes do commit desfaz os dois, e a nova tentativa executa de novo, com segurança;
  - serviços que abrem transação própria verificam se já existe uma (`Database.CurrentTransaction`) e a reutilizam. Hoje, só operações fora das rotas de agentes abrem transação própria (veja a situação atual), e um teste garante que as rotas de agentes não abrem uma segunda.
- **O que não fica guardado:** 401, 403, 404, 429 e 5xx, que dependem de autorização, limite ou falha transitória. A transação é desfeita e a chave fica livre.
- **O que fica guardado:** 2xx e as respostas de regra determinística (400 de validação, 409 de conflito e 422).
- **Credencial revogada ou expirada:** nunca chega ao passo 1, porque recebe 401 na autenticação. **Agente sem acesso:** é barrado no passo 3. Nenhum dos dois recupera respostas anteriores.
- **Comportamentos documentados:**
  - **Rotação de credencial:** a chave vale por credencial. Uma nova tentativa com outra credencial executa de novo. O cliente deve manter a credencial durante as novas tentativas.
  - **Corpo:** o hash é dos bytes. JSON equivalente com outra serialização responde 422.
- **Expiração e exclusão:** a expiração lógica é de 24 horas, e a exclusão física acontece depois de 7 dias (D12).

### D10. Auditoria de operações (AgentOperation)

- **`agent_operations`** (`IOrganizationOwned`): uma linha por requisição de agente com credencial identificada, inclusive leituras, negadas, credenciais revogadas ou expiradas e a primeira requisição bloqueada por limite em cada janela.
- **Campos:**
  - `agent_id` e `credential_id`;
  - `occurred_at` (UTC) e `duration_ms`;
  - `http_method` e `route_template` (o modelo, por exemplo `/api/v1/agents/tasks/{taskId}`, nunca a URL com valores ou a query string);
  - `project_id` e `task_id`, quando houver;
  - `status_code`;
  - **`authorization_result`**: `Allowed`, `CredentialRevoked`, `CredentialExpired`, `AgentDisabled`, `OrganizationMismatch`, `ProjectNotVisible`, `ScopeMissing`, `PermissionMissing`, `BusinessRuleDenied` ou `RateLimited`;
  - `denial_code` (até 64 caracteres, por exemplo `agent_cannot_approve`);
  - **`correlation_id`** (o `TraceIdentifier`, o mesmo dos logs e do `RequestId` dos eventos);
  - `idempotent_replay`;
  - `client_origin` (`X-Gestalyn-Client`, informativo).
- **Nunca gravado:** token, prefixo, hash, cabeçalhos, corpos de requisição ou resposta, e-mails.
- **Gravação:**
  - num middleware que envolve a requisição a partir da autenticação, com um escopo de DI e um `DbContext` próprios, **fora da transação da operação**: a linha sobrevive a um rollback;
  - se a gravação falhar, a requisição não falha. Fica um log de erro e o health check `agents-audit` fica `Degraded`.
  - As escritas bem-sucedidas continuam auditadas também pelo `ActivityEvent`, gravado na mesma transação (ADR-014).
- **Tokens inexistentes** não têm agente nem organização: ficam fora da tabela, com um log estruturado (sem o token e sem o prefixo) e o limite por IP (D11).
- **Gatilho:** recusa `UPDATE` e `TRUNCATE` sempre. O `DELETE` só é aceito quando `OLD.occurred_at < now() - interval '180 days'` (D12).
- **Índices:** `(agent_id, occurred_at)`, `(organization_id, occurred_at)` e `(occurred_at)`, este para a retenção.
- **Leitura por pessoas:** `GET /api/v1/organizations/{id}/agents/{agentId}/operations` (`agents.manage`), paginada, com filtros por resultado e período.

### D11. Rate limiting no PostgreSQL

- **Contador por credencial e janela** (`agent_rate_limit_counters`): `credential_id`, `organization_id`, `window_start` e `request_count`, com chave primária `(credential_id, window_start)`.
- **Uma instrução por requisição**, depois da autenticação:

  ```sql
  INSERT INTO agent_rate_limit_counters (credential_id, organization_id, window_start, request_count)
  VALUES (@credential, @organization, to_timestamp(floor(extract(epoch FROM now()) / @window) * @window), 1)
  ON CONFLICT (credential_id, window_start)
  DO UPDATE SET request_count = agent_rate_limit_counters.request_count + 1
  RETURNING request_count, window_start;
  ```

- **Por que funciona:**
  - **atômica sob concorrência:** o `ON CONFLICT` trava a linha;
  - **correta com várias instâncias:** o estado está no banco, e a janela usa o relógio do banco, sem depender do relógio de cada instância;
  - **sem leitura extra:** um único comando por requisição.
- **Resultado:**
  - `request_count > limite`: **429** em Problem Details, com `Retry-After` até o fim da janela;
  - `request_count = limite + 1`: a primeira rejeição da janela vai para `AgentOperation` (`RateLimited`); as seguintes, não, para não multiplicar gravações durante uma enxurrada;
  - `request_count = 1`: atualiza o último uso da credencial (D2), no máximo uma vez por janela.
- **Configuração:** `RateLimiting:Agents` (`PermitLimit` 120 e `WindowSeconds` 60), validada na inicialização.
- **Janela fixa:** admite até o dobro do limite na virada da janela. A limitação fica documentada no ADR-016.
- **Banco indisponível:** a requisição falha com 503. Ela falharia de qualquer forma, porque a autenticação depende do banco.
- **SQL bruto:** o EF Core não expressa o upsert com `RETURNING` de forma atômica. A justificativa exigida pelo `AGENTS.md` está no ADR-016.
- **Limite complementar de falhas por IP:**
  - tokens `gsk_` inexistentes contam por IP, em memória (`RateLimiting:AgentAuthFailures`, padrão 30 por minuto);
  - acima disso, novas tentativas daquele IP recebem 429 antes da consulta ao banco;
  - **não é a cota de agentes**, só uma proteção barata da consulta. Atrás de um proxy, depende dos forwarded headers (Fase 14).

### D12. Retenção e limpeza

| Registro | Retenção |
|---|---|
| `agent_operations` | 180 dias |
| `idempotency_records` | Expiração lógica em 24 horas; exclusão depois de 7 dias |
| `work_reports` | Permanente |
| `agent_rate_limit_counters` | 1 dia (dado operacional, não de auditoria) |
| Agentes, credenciais e acessos | Permanentes: desativação e revogação, nunca exclusão |

- **Serviço:** `AgentRetentionService`, um `BackgroundService` hospedado na própria API, sem infraestrutura nova.
- **Agenda:** `PeriodicTimer` com `Agents:Retention:Interval` (padrão de 1 hora) e um atraso inicial.
- **Uma instância por vez:** `pg_try_advisory_lock` com uma chave fixa. Se outra instância estiver limpando, o ciclo é pulado.
- **Exclusão em lotes** de até 1.000 linhas, cada lote com a própria transação e `statement_timeout` de 30 segundos:
  - `agent_operations` com `occurred_at < now() - interval '180 days'`;
  - `idempotency_records` com `created_at < now() - interval '7 days'`;
  - `agent_rate_limit_counters` com `window_start < now() - interval '1 day'`.
- **Exceção de isolamento:** a limpeza atravessa organizações. É a terceira exceção autorizada ao uso de `IgnoreQueryFilters` ou SQL bruto (ADR-016), restrita a este serviço e a esses predicados.
- **Proteção no banco:**
  - o gatilho de `agent_operations` só aceita o `DELETE` vencido. O prazo de 180 dias fica **no gatilho**: mudá-lo exige uma migração;
  - a API recusa iniciar com uma configuração menor que a do gatilho;
  - `work_reports` não tem nenhuma exceção.
- **Idempotente:** repetir um ciclo não apaga nada além do vencido.
- **Monitorável:**
  - logs `[LoggerMessage]` com as contagens por tabela e a duração;
  - health check `agents-retention`, que fica `Degraded` sem um ciclo bem-sucedido (ou pulado por trava) há mais de 3 intervalos.
- **Falhas:** exceções são registradas e o próximo ciclo tenta de novo. O serviço usa um escopo próprio e nunca derruba a API nem bloqueia requisições.

### D13. Interface Web e exibição do token

- **Telas:**
  - **Configurações da organização → Agentes** (`agents.manage`): lista, criação, desativação e reativação;
  - **detalhes do agente:**
    - credenciais: nome, prefixo, escopos, expiração e último uso, com criação e revogação;
    - projetos concedidos, com concessão e remoção (o projeto precisa ser gerenciado por quem concede);
    - operações recentes, com o resultado da autorização;
  - **tarefa e atividades do projeto:** a autoria "Agente: nome" e os relatórios, com os níveis da D8 rotulados: "Declarado pelo agente", "Recebido e validado pela API", "Testes não verificados", "CI não verificado", "Aprovação humana pendente" ou "Aprovado por…".
  - O que aparece depende só das permissões que a API devolve (ADR-015).
- **Exibição do token** (decisão C4, exceção formal no ADR-016 à seção 7 da especificação mestre e ao ADR-006):
  1. o token completo aparece **uma única vez**, na resposta do próprio formulário de criação, **sem redirecionamento** (sem post-redirect-get);
  2. a API e a Web respondem com `Cache-Control: no-store`;
  3. o token não é guardado no banco (só o hash), na Web, no navegador (sem `localStorage`, `sessionStorage`, cookie ou URL), na sessão nem em aviso;
  4. o token não aparece em logs, telemetria, monitoramento nem em nenhuma resposta posterior. A Web não registra corpos de resposta da API, e um teste procura o token nos logs da execução;
  5. o botão de copiar usa a Clipboard API num módulo de `wwwroot/js`, sem script inline e sem guardar nada;
  6. ao sair da tela, o token não pode ser recuperado. A tela diz isso e orienta criar uma credencial nova e revogar a antiga.
- **Fora dessa exceção:** os tokens de sessão dos usuários e qualquer outra credencial continuam sem chegar ao navegador.
- **Testes e documentação:** as telas ganham testes bUnit e entram em `docs/web/TELAS.md`.

### D14. Tempo real

Reavaliado: os relatórios e as operações de agentes são lidos ao abrir a tela. Sem necessidade comprovada, o SignalR continua fora (decisão 23).

## Modelo de dados e migrações

| Tabela | Tipo | Proteções |
|---|---|---|
| `agents` | Organização | Nome único por organização; chave alternativa `(organization_id, id)` |
| `agent_credentials` | Organização | `token_hash` único; FK composta para `agents` |
| `agent_project_access` | Organização | FKs compostas para `agents` e `projects`; papel `Contributor` ou `Viewer` (`CHECK`) |
| `agent_operations` | Organização | Gatilho: sem `UPDATE` e `TRUNCATE`; `DELETE` só depois de 180 dias |
| `agent_rate_limit_counters` | Organização | Chave primária `(credential_id, window_start)` |
| `idempotency_records` | Organização | Índice único `(credential_id, key)` |
| `work_reports` | Organização | Gatilho: sem `UPDATE`, `DELETE` e `TRUNCATE`; FKs compostas para projeto, tarefa e agente |
| `tasks` (alteração) | Organização | `created_by_agent_id`, `CHECK` de autor único, gatilho de autoria imutável, `version` |
| `role_permissions` (dados) | Organização | `agents.manage` para Proprietário e Administrador existentes |

- **Migrações:**
  - geradas com `dotnet ef migrations add` e nunca aplicadas pela aplicação;
  - gatilhos e `CHECK` com `num_nonnulls` são SQL escrito à mão na migração, por necessidade, como na Fase 08;
  - cada migração tem um teste: aplica até a anterior, insere dados, aplica a nova e confere a preservação.

## Impacto

| Área | Impacto |
|---|---|
| Banco | 7 tabelas novas, alteração de `tasks`, dados de permissão, 4 gatilhos |
| API | Políticas explícitas (todas as rotas); esquema `AgentToken`; organização ativa de agentes; `AgentAccessGrant`; `TransitionRules`; ator nos serviços; versão das tarefas; 10 rotas de agentes; rotas de gestão e de relatórios; idempotência; auditoria; rate limiting; retenção; 2 health checks |
| Contratos existentes | `TaskDetails.CreatedByUserId` anulável e `createdBy`; `expectedVersion` opcional no `PATCH` de pessoas; `actor` nas atividades e no histórico. Mudanças registradas no ADR-016 |
| Web | Telas de agentes; autoria e relatórios na tarefa e nas atividades; envio de `expectedVersion`; ajuste do modelo `TaskDetails` |
| Testes | `ActivityCoverageTests` passa a cobrir as rotas novas de escrita, de agentes e de gestão; testes novos da estratégia de testes |

## Dependências

| Item | Estado |
|---|---|
| Fases 03 a 09 | Completed e integradas em `develop`; o registro da Fase 09 entrou pelo [PR #18](https://github.com/harrisonleandro/Gestalyn/pull/18) (merge `d6c34fc`) |
| Pacotes novos | **Nenhum.** ASP.NET Core, EF Core, Npgsql e .NET |
| Docker (PostgreSQL e Mailpit) | Necessário para os testes de integração e a execução real |

## Tarefas

| ID | Tarefa | Subtarefas |
|---|---|---|
| F10-001 | Implementar entidade Agent | Entidade, configuração, migração, `agents.manage` com migração de dados (D1) |
| F10-002 | Implementar provisionamento | Rotas de gestão, eventos de auditoria, permissão e isolamento (D1, ADR-016) |
| F10-003 | Implementar credenciais | Criação com o token uma vez só, prefixo, até 3 ativas (D2) |
| F10-004 | Implementar hash e armazenamento seguro de tokens | SHA-256 com índice único; nada do token em logs ou respostas posteriores (D2, D13) |
| F10-005 | Implementar expiração | Padrão, mínimo e máximo, checagem a cada requisição (D2, D3) |
| F10-006 | Implementar revogação | Credencial e desativação do agente, com efeito na requisição seguinte (D2, D3) |
| F10-007 | Implementar escopos de acesso | Tabela de escopos e `AgentAccessGrant.Compute` (D4) |
| F10-008 | Implementar AgentProjectAccess | Concessão e remoção com as duas permissões; Colaborador ou Observador (D4) |
| F10-009 | Implementar autenticação de agentes | Políticas por esquema, handler, `AgentCredentialDirectory`, organização da credencial (D3) |
| F10-010 | Implementar endpoints de contexto | `me`, `projects`, `projects/{id}/context` (D7) |
| F10-011 | Implementar listagem de tarefas | `projects/{id}/tasks` e `tasks/{id}` (D7) |
| F10-012 | Implementar criação e atualização de tarefas | `POST`, `PATCH` e transições; autoria e versão; `TransitionRules` (D5, D6, D7) |
| F10-013 | Implementar registro de atividades | `POST /agents/activities` com `ActorKind.Agent` (D6, D7) |
| F10-014 | Implementar relatórios de trabalho | `WorkReport`, os cinco níveis, leitura por pessoas (D8) |
| F10-015 | Implementar idempotência | Transação, trava, replay com reautorização, 409 e 422, expiração (D9) |
| F10-016 | Implementar auditoria de operações | `AgentOperation`, rate limiting no PostgreSQL, retenção (D10, D11, D12) |
| F10-017 | Criar testes de segurança | [Estratégia de testes](#estratégia-de-testes) |
| F09-018 | Implementar interface de agentes (transferida) | Telas e exibição única do token (D13) |

## Plano de implementação

Cada etapa termina com `dotnet build` sem avisos e todos os testes passando, num commit próprio.

| Etapa | Conteúdo | Verificação |
|---|---|---|
| 0 | Depois do merge do PR #18, autorizado pelo responsável: rebase em `develop` e commit da documentação aprovada | Concluída em 2026-11-18: merge `d6c34fc`; a branch tem só os commits da Fase 10 sobre `develop` |
| 1 | Políticas `User` e `Agent` no lugar do `AuthorizeFilter` sem política (ainda sem agentes) | Teste de políticas de todos os endpoints; os 372 testes atuais passam |
| 2 | `Agent`, `AgentCredential`, `AgentProjectAccess`; `agents.manage` com migração de dados | Testes de migração, de permissão e de isolamento |
| 3 | Rotas de gestão por pessoas e eventos | `ActivityCoverageTests`; 403 e 404 por permissão; token só na criação |
| 4 | Handler `AgentToken`, `AgentCredentialDirectory`, organização do agente, `GET /agents/me` | Revogação, expiração, agente desativado, organização divergente, separação das rotas |
| 5 | `AgentAccessGrant` e as regras de agentes em `TransitionRules`; transições disponíveis por `TransitionRules` | Unitários de escopo × papel; workflows personalizados; testes de pessoas sem mudança |
| 6 | Ator nos serviços; autoria, `CHECK`, gatilho e `version` nas tarefas; nomes de agentes; contratos e Web | Teste da migração com tarefas existentes; 409 de versão; autoria imutável |
| 7 | Rotas de leitura de agentes (projetos, contexto, tarefas) | Escopos, isolamento, contexto sem dados pessoais |
| 8 | Idempotência e rotas de escrita de agentes (tarefas, transições, atividades) | Replay, 422, 409 concorrente, reautorização, atomicidade |
| 9 | `WorkReport` e leitura por pessoas | Imutabilidade no banco; níveis; estado da tarefa inalterado |
| 10 | **Obrigatória; bloqueia a conclusão e qualquer uso em produção:** `AgentOperation` (inclusive as recusas de credenciais conhecidas e a primeira rejeição por limite), gravação de `last_used_at`, rate limiting por credencial no PostgreSQL, limite de falhas por IP para tokens desconhecidos e malformados (antes da consulta ao banco), `GET .../agents/{agentId}/operations` e retenção | Concorrência do contador; 429; auditoria das negadas e das credenciais recusadas; último uso no máximo uma vez por janela; falhas por IP barradas sem consultar o banco; ciclo de limpeza |
| 11 | Telas Web | bUnit; token uma vez só; `no-store` |
| 12 | Suíte de segurança, ponta a ponta, mutações, execução real, documentação de encerramento | Critérios de aceitação |

## Arquivos previstos

```
src/ProjectManager.Domain/Agents/                     Agent, AgentCredential, AgentProjectAccess, AgentScopes,
                                                      AgentOperation, WorkReport
src/ProjectManager.Domain/Workflows/TransitionRules.cs     Regras de agentes (D5)
src/ProjectManager.Domain/Tasks/ProjectTask.cs        Autoria por tipo de ator e versão (D6)
src/ProjectManager.Application/Agents/                Contratos, serviços, IAgentProjectAccess, IIdempotencyStore
src/ProjectManager.Application/Projects/AgentAccessGrant.cs     Regra única de acesso de agentes (D4)
src/ProjectManager.Infrastructure/Agents/             Serviços, AgentCredentialDirectory, idempotência,
                                                      rate limiting, AgentRetentionService
src/ProjectManager.Infrastructure/Persistence/Configurations/AgentConfigurations.cs
src/ProjectManager.Infrastructure/Persistence/Migrations/        Migrações da D6 e do modelo de dados
src/ProjectManager.API/Authentication/                Políticas, AgentTokenAuthenticationHandler
src/ProjectManager.API/Agents/                        Controllers de agentes, atributos, idempotência,
                                                      auditoria de operações
src/ProjectManager.API/Controllers/                   Gestão de agentes e leitura de relatórios
src/ProjectManager.Web/Components/Pages/Agents/       Telas de agentes (D13)
tests/ProjectManager.UnitTests/Agents/                Escopos, AgentAccessGrant, TransitionRules, credenciais, níveis
tests/ProjectManager.IntegrationTests/Agents/         Segurança, isolamento, idempotência, concorrência, retenção,
                                                      ponta a ponta
tests/ProjectManager.WebTests/Agents/                 Telas de agentes
docs/decisions/ADR-016-AGENTES.md
docs/security/AGENTES.md                              Modelo de ameaças e uso das credenciais
```

## Critérios de aceitação

| Critério | Como será verificado |
|---|---|
| Agentes autenticam independentemente de usuários | Credencial de agente sem nenhuma sessão de usuário; token de agente em rota de usuário e JWT em rota de agente recebem 401; teste de políticas de todos os endpoints |
| Credenciais podem ser revogadas | Revogada, expirada e de agente desativado recebem 401 na requisição seguinte e ficam em `AgentOperation`; outra credencial ativa continua funcionando; a quarta credencial ativa é recusada |
| Token inválido não acessa recursos | Token inexistente, adulterado, truncado, de outro agente ou sem `gsk_`: 401 igual, sem dados |
| Escopos são respeitados | Cada rota recusa (403) a credencial sem o escopo; `tasks.approve` nunca é concedido (unitário e mutação) |
| Acesso limitado aos projetos autorizados | Projeto da mesma organização sem concessão (404), de outra organização (404), organização divergente no cabeçalho ou na rota (404), Observador escrevendo (403) |
| Isolamento entre organizações | Toda rota de agente, de gestão e de relatório testada com recursos de outra organização |
| Agentes não aprovam nem concluem | Workflow padrão e personalizado com "Em andamento → Concluída" sem aprovação: 403, estado inalterado, sem histórico e com a recusa em `AgentOperation`; Cancelada sem aprovação permitida |
| Autoria íntegra | Tarefa criada por agente com `created_by_agent_id`; `CHECK` recusa os dois autores e nenhum; o gatilho recusa a mudança de autoria; a migração preserva as tarefas existentes |
| Concorrência | `expectedVersion` desatualizado responde 409 sem sobrescrever; duas edições simultâneas: uma vence e a outra recebe 409 |
| Operações registradas | Cada requisição com credencial identificada gera uma `AgentOperation` com resultado e correlation ID; escritas geram `ActivityEvent` com `ActorKind.Agent`; `UPDATE` recusado |
| Idempotência funcional | Replay idêntico sem duplicar; outro corpo, método ou rota: 422; chamadas simultâneas: uma executa e a outra recebe 409; credencial revogada ou acesso removido não recuperam a resposta; queda simulada antes do commit não deixa operação sem registro |
| Relatórios íntegros | `UPDATE` e `DELETE` recusados pelo banco; o estado da tarefa não muda; os níveis aparecem separados |
| Limite de requisições | A 121ª requisição na janela responde 429 com `Retry-After`; contagem correta com requisições concorrentes; só a primeira rejeição é registrada |
| Retenção | O ciclo apaga só o vencido; o `DELETE` direto de operação recente é recusado; relatórios intactos; ciclo repetido sem efeito; dois ciclos simultâneos: um é pulado |
| Token na Web | Aparece uma vez, com `no-store`; ausente de logs, banco, cookies e respostas posteriores |
| Abuso de autenticação | Tokens desconhecidos ou malformados acima do limite por IP recebem 429 antes da consulta ao banco; recusas de credenciais conhecidas ficam em `AgentOperation`; as respostas continuam indistinguíveis entre si |
| Último uso da credencial | `last_used_at` atualizado no máximo uma vez por janela, visível na gestão de credenciais |
| Ponta a ponta | Teste de integração automatizado (estratégia de testes) |

## Estratégia de testes

- **Unitários:**
  - `AgentAccessGrant` (escopo × papel × exigência; nunca `tasks.approve`);
  - `TransitionRules` com agentes (aprovação, Concluída, Cancelada, dependências) e pessoas (sem mudança);
  - formato e prefixo do token, expiração;
  - níveis do relatório e `approvalRequired`;
  - comparação do hash e do método e da rota na idempotência.
- **Integração (PostgreSQL real via Testcontainers):**
  1. **Isolamento entre organizações:** rotas de agentes, de gestão e de relatórios com IDs de outra organização; cabeçalho e rota divergentes; credencial de uma organização contra recursos de outra.
  2. **Escopos e permissões:** cada rota sem o escopo; Observador e Colaborador; concessão sem `project.members.manage`; papel Gerente recusado; migração de `agents.manage`.
  3. **Acesso por projeto:** sem concessão, concessão removida e projeto arquivado (leitura sim, escrita 409).
  4. **Revogação e expiração:** revogada, expirada, agente desativado e reativado; efeito na requisição seguinte; limite de 3 ativas com criações concorrentes.
  5. **Idempotência:** replay, 422 por corpo, método e rota, 409 em processamento, reautorização, não guardar 401, 403, 404 e 429, atomicidade com falha injetada antes do commit.
  6. **Concorrência e versão:** `expectedVersion` divergente; edições e transições simultâneas; versão incrementada em todos os caminhos de alteração da tarefa.
  7. **Workflows personalizados e bloqueio de aprovação:** transição direta para Concluída sem aprovação, transição com aprovação, Cancelada permitida, transições disponíveis coerentes com a regra.
  8. **Autoria:** criação por agente e por pessoa; `CHECK` e gatilho; migração com tarefas existentes; nomes de agentes nas leituras.
  9. **Integridade e imutabilidade de `WorkReport`:** `UPDATE`, `DELETE` e `TRUNCATE` recusados; tarefa de outro projeto recusada; estado inalterado; aprovação humana calculada pelo histórico.
  10. **Auditoria de operações negadas:** cada resultado de autorização registrado; operações gravadas mesmo com a operação desfeita; tokens inexistentes fora da tabela e no log sem o token.
- **Rate limiting e retenção:** contador sob concorrência (N requisições simultâneas, contagem exata), 429 e `Retry-After`, ciclo de limpeza e gatilhos.
- **Teste de ponta a ponta (integração, automatizado), com um token real emitido pelo sistema:**
  1. Uma pessoa com `agents.manage` cria o agente, cria a credencial (o token vem só nessa resposta) e concede um projeto como Colaborador.
  2. Com o token, o agente consulta `me` e o contexto do projeto.
  3. Cria uma tarefa (com `Idempotency-Key`; o replay não duplica) e a altera com `expectedVersion`.
  4. Move a tarefa até um estado de revisão.
  5. Registra uma atividade e envia um relatório, conferindo os níveis e o estado da tarefa inalterado.
  6. Tenta a transição com aprovação e a entrada em Concluída: as duas são recusadas, sem mudança de estado e com o registro em `AgentOperation`.
  7. Uma pessoa aprova e o nível de aprovação humana passa a `approved`.
  8. A credencial é revogada e a chamada seguinte recebe 401, também registrada.
- **Componentes (bUnit):** telas de agentes, credenciais (token uma vez, permissões), acessos, operações e relatórios com os níveis rotulados.
- **Mutação:** cada uma destas deve fazer os testes falharem:
  - remover a checagem de revogação ou de expiração;
  - incluir `tasks.approve` nas permissões de agentes;
  - remover a regra de `Done` para agentes;
  - aceitar a organização do cabeçalho;
  - devolver o replay sem reautorizar;
  - pular o hash do corpo;
  - remover o `CHECK` de autoria.
- **Execução real:**
  - API e Web sobre o banco local, com o fluxo do teste de ponta a ponta pela Web e por `curl`;
  - busca do token nos logs da execução e no banco: só o hash e o prefixo.

## Riscos

| Risco | Mitigação |
|---|---|
| Vazamento de credencial | Token uma vez só, hash no banco, expiração obrigatória, revogação imediata, escopos mínimos, rate limiting, operações registradas |
| Agente com poderes de pessoa | Políticas por esquema com teste de todos os endpoints; sem Gerente; regras de agentes em `TransitionRules`; serviços que recusam um ator agente nas autorias de pessoas |
| Regra de acesso ou de transição duplicada | `AgentAccessGrant.Compute` e `TransitionRules` são as únicas regras; as transições disponíveis passam a usar `TransitionRules` |
| Migração de `tasks` | `CHECK` válido para as linhas existentes, gatilho de autoria, teste com dados reais da migração anterior |
| Mudança de contrato em `TaskDetails` | Campo anulável mais `createdBy`; Web ajustada na mesma etapa; CLI ainda não existe |
| Volume de `AgentOperation` | Linha pequena, índices por agente e data, retenção de 180 dias |
| Falha ao gravar a auditoria de operações | Log de erro e health check `Degraded`; as escritas continuam auditadas pelo `ActivityEvent` na mesma transação |
| Latência do rate limiting no banco | Uma instrução por requisição sobre a chave primária; medição na Fase 13 |
| Corrida na idempotência | Trava consultiva por chave e índice único, com teste de concorrência |
| Limpeza concorrente ou longa | Trava consultiva global, lotes pequenos, `statement_timeout` |
| Volume da fase | 13 etapas pequenas, cada uma com verificação própria |

## Definição de conclusão

- Todos os critérios de aceitação verificados com evidência, local e no CI.
- **A autenticação de agentes não está pronta para produção sem a etapa 10** (auditoria persistente de operações, último uso e proteção contra abuso de autenticação). Concluir as etapas 1 a 9 não basta para liberar o uso de agentes.
- Mutações de segurança detectadas.
- ADR-016 aceito e coerente com o código entregue.
- `docs/security/AGENTES.md` escrito.
- Atualizados: README, AGENTS.md, `docs/security/AUTENTICACAO.md`, ARCHITECTURE, `docs/web/TELAS.md`, `PROJECT_STATUS.md` e o relatório da Fase 10.
- Alterações integradas por PR, com o check "Build e testes" aprovado.

## Rastreabilidade da revisão técnica (2026-11-15)

| Item | Correção |
|---|---|
| C1 Política global sem esquema | D3: políticas explícitas, sem seletor como padrão, teste de todos os endpoints |
| C2 Conclusão por workflow personalizado | D5: `AgentCannotApprove` e `AgentCannotEnterDone` em `TransitionRules`; Cancelada permitida |
| C3 Busca da credencial sem organização | D3: `AgentCredentialDirectory` (exceção no ADR-016) e organização ativa de agentes |
| C4 Token na Web | D13 e ADR-016: exceção formal, com as condições aprovadas |
| C5 Idempotência não atômica | D9: transação única com trava consultiva |
| C6 Replay sem reautorização | D9: reautorização, chave por credencial, método e rota, códigos não guardados |
| A1 Permissão das atividades | D4: `documents.write` |
| A2 Observador enviando relatórios | D4: `reports:write` exige `tasks.write` |
| A3 Contexto do `AgentAccessGrant` | D4: ator, estado, escopos, papel e exigência da operação; ator no `ProjectAccessGrant` |
| A4 Imutabilidade do relatório | D8: verificações em tabela separada e só de inclusão; aprovação calculada pelo histórico; "aceito" definido |
| A5 Versão das tarefas | D6: token de concorrência no `UPDATE`, quando incrementa, 409 |
| A6 Limite e retenção | D11 e D12 |
| A7 Campos da `AgentOperation` | D10: resultado da autorização, correlation ID, credenciais conhecidas inválidas, primeira rejeição por limite, gravação fora da transação |
| M1 Contratos e nomes de autores | D6 |
| M2 `ActorKind` fixo | D6 |
| M3 Cobertura de auditoria | Impacto e estratégia de testes |
| M4 Contexto sem dados pessoais | D7 |
| M5 Hash dos bytes | D9 |
| M6 Rotação e idempotência | D9 |
| M7 Limite de falhas por IP | D3 e D11 |
| M8 Acesso depois da perda de permissão | D4 |
| M9 `PATCH` sem `Idempotency-Key` | D7 |
| B1 ADR-016 | Criado |
| B2 Decisões aprovadas | Tabela de decisões aprovadas |
| B3 Testes ausentes | Estratégia de testes |
| B4 Ordem no `PROJECT_STATUS.md` | Corrigida |
| B5 `AUTENTICACAO.md` | Atualizado |

## Execução e ajustes em relação ao plano

| Etapa | Estado | Evidência |
|---|---|---|
| 0 | Concluída em 2026-11-18 | Merge do PR #18 (`d6c34fc`), CI de `develop` aprovado (run 44385682944); branch rebaseada antes de qualquer envio |
| 1 | Concluída em 2026-11-18 | Ver abaixo |
| 2 | Concluída em 2026-11-18 | Ver abaixo |
| 3 | Concluída em 2026-11-18 | Ver abaixo |
| 4 | Concluída em 2026-11-18 | Ver abaixo |
| Revisão | Correções concluídas em 2026-11-18 | Ver "Correções da revisão independente" |
| 5 | Concluída em 2026-11-18 | Ver abaixo |
| 6 | Concluída em 2026-11-18 | Ver abaixo |
| 7 | Concluída em 2026-11-19 | Ver abaixo |
| 8 | Concluída em 2026-11-19 | Ver abaixo |
| 9 | Concluída em 2026-11-22 | Ver abaixo |
| 10 | Concluída em 2026-11-22 | Ver abaixo |
| 11 | Concluída em 2026-11-22 | Ver abaixo |
| 12 | Concluída em 2026-11-22 | Ver abaixo |
| Integração | Concluída em 2026-11-22 | [PR #19](https://github.com/harrisonleandro/Gestalyn/pull/19), merge `b9e8daa`; [run 45039861969](https://github.com/harrisonleandro/Gestalyn/actions/runs/45039861969) e [run 45040388503](https://github.com/harrisonleandro/Gestalyn/actions/runs/45040388503) |

### Etapa 1: políticas explícitas

- **O que mudou:**
  - o `new AuthorizeFilter()` global deu lugar à política `User` (esquema JWT, usuário autenticado e claim `sid`);
  - uma convenção (`ApiPolicyConvention`) aplica essa política à metadata de todos os endpoints dos controllers;
  - `[AllowAnonymous]` continua valendo.
- **Ajuste em relação ao plano:** a política `Agent` não foi registrada nesta etapa. Ela depende do esquema `AgentToken`, que só existe na etapa 4, e registrá-la antes criaria uma política sem uso. O teste já exige a política `Agent` em qualquer rota `/api/v1/agents/*`.
- **Teste novo (`EndpointPolicyTests`):**
  - todo endpoint `/api` tem exatamente a política esperada;
  - as rotas anônimas são só as 7 de conta (cadastro, confirmação, reenvio, entrada, renovação, recuperação e redefinição);
  - um token `gsk_` numa rota de usuário recebe 401.
- **Validação:**
  - build em Release: 0 avisos e 0 erros;
  - `dotnet test`: 374/374 (178 unitários, 43 de componentes, 153 de integração).
- **Mutação:** sem a convenção, o teste estrutural falha. Os testes de sessão continuam passando nesse caso, porque o controller também recusa a requisição sem usuário; por isso o teste estrutural é necessário.
- **Execução real** (API local com o banco de desenvolvimento):
  - sem token, `GET /auth/me` responde 401;
  - com um token `gsk_`, `GET /auth/me` e `GET /organizations` respondem 401;
  - a entrada anônima responde 200, e com o JWT `/auth/me` e `/organizations` respondem 200;
  - `/health` continua anônimo (200);
  - nenhum erro no log.

### Etapa 2: agentes, credenciais, acesso e `agents.manage`

- **Domínio** (`src/ProjectManager.Domain/Agents`):
  - `Agent`: nome único na organização sem diferenciar maiúsculas (`normalized_name`), descrição, estado, desativação e reativação;
  - `AgentCredential`: só o hash e o prefixo, escopos validados e ordenados, validade de 1 a 365 dias, revogação que mantém a primeira data e o primeiro autor, sem criação para agente desativado;
  - `AgentProjectAccess`: papel Colaborador ou Observador, agente e projeto da mesma organização;
  - `AgentScopes` e `AgentTokens` (formato `gsk_` + 43 caracteres base64url, prefixo de 8 e hash hexadecimal de 64);
  - `AgentRuleException` para as regras recusadas.
- **Permissão:** `agents.manage` no catálogo e nos papéis de sistema Proprietário e Administrador das organizações novas.
- **Migração `AddAgents`:**
  - tabelas `agents`, `agent_credentials` (escopos em `text[]`) e `agent_project_access`;
  - FKs compostas pela organização;
  - `CHECK` do estado, do papel, dos escopos não vazios e da validade;
  - índices únicos do hash e do nome;
  - migração de dados de `agents.manage` para Proprietário e Administrador existentes.
- **Ajuste:** a coluna `last_used_at` foi criada agora e será gravada na etapa 10, junto com o contador do rate limiting (D2 e D11).
- **Testes novos:**
  - 19 unitários (`AgentRulesTests`);
  - 6 de integração (`AgentPersistenceTests`): migração da permissão (só Proprietário e Administrador, inclusive com papel personalizado), filtro de leitura com outra organização e sem organização, gravação cruzada bloqueada, FKs compostas, restrições do banco e escopos lidos de volta.
- **Validação:**
  - build em Release: 0 avisos e 0 erros;
  - `dotnet test`: 399/399 (197 unitários, 43 de componentes, 159 de integração).
- **Mutações detectadas:**
  - sem a migração de dados, o teste da permissão falha;
  - com o papel Gerente liberado no domínio, o teste do acesso falha.
- **Banco de desenvolvimento:** migração aplicada. 20 papéis (os Proprietários e Administradores existentes) receberam `agents.manage`, e nenhum outro.
- **Execução real:** `GET /organizations/{id}/me` devolve `agents.manage` para o Proprietário e não para o Membro; nenhum erro no log.

### Etapa 3: gestão de agentes por pessoas

- **Rotas** (`AgentsController`, política `User`, `[OrganizationPermission("agents.manage")]`) sob `/api/v1/organizations/{organizationId}/agents`:
  - listar, criar, ler e alterar;
  - desativar e reativar;
  - listar, criar e revogar credenciais;
  - conceder, alterar e remover o acesso a um projeto (`PUT` e `DELETE .../projects/{projectId}`), que exige também `project.members.manage` no projeto.
- **Serviço** (`AgentService`):
  - nome duplicado na organização responde 409;
  - credencial para agente desativado responde 409;
  - revogar de novo responde 200 sem gerar outro evento.
- **Token:**
  - `gsk_` + 32 bytes de `RandomNumberGenerator` (`OpaqueTokens`);
  - no banco, só o SHA-256 e o prefixo;
  - a resposta da criação traz `Cache-Control: no-store` e é a única que contém o token.
- **Validade e limite:**
  - padrão de 90 dias e máximo em `Agents:Credentials`, validados na inicialização;
  - a criação conta as credenciais ativas e grava numa transação serializável, então o limite de 3 vale também sob concorrência. (Substituída depois por uma trava otimista na linha do agente; veja as correções da revisão independente.)
- **Eventos:**
  - `agent.created`, `agent.updated`, `agent.disabled` e `agent.enabled`;
  - `agent.credential_created` e `agent.credential_revoked`, só com o ID do agente, os escopos e a validade, sem o token, o prefixo ou o hash;
  - `agent.project_access_granted`, `agent.project_access_updated` e `agent.project_access_removed`, no projeto;
  - todas as rotas novas de escrita entram no `ActivityCoverageTests`.
- **Ajustes em relação ao plano:**
  - **`GET .../agents/{agentId}/operations`** fica para a etapa 10, junto com `AgentOperation`: criá-la agora daria uma rota sem dados reais.
  - **Correção em código compartilhado (`SaveChangesConflicts`):** sob concorrência, o provedor do Npgsql embrulha a falha de serialização (40001) numa `InvalidOperationException` de "falha transitória". O teste de concorrência mostrou que isso virava 500 em vez de 409. `TrySaveChangesAsync` passa a tratar esse caso como conflito, e `IsSerializationFailure` reconhece a falha embrulhada. A correção beneficia também `AddDependencyAsync`, que usa o mesmo caminho.
- **Testes novos (`AgentManagementTests`, 7):**
  - ciclo de vida;
  - token só na criação, com o hash do token completo no banco e o token fora de leituras, eventos e logs;
  - escopos e validade inválidos (400), limite de 3, revogação e substituição;
  - 6 criações simultâneas sem passar do limite;
  - acesso a projetos (Gerente recusado; quem tem `agents.manage` mas não gerencia o projeto recebe 404 e depois 403);
  - permissões: Membro 403, fora da organização 404, anônimo 401 e Administrador permitido;
  - isolamento entre organizações (404 sem efeito).
- **Validação:**
  - build em Release: 0 avisos e 0 erros;
  - `dotnet test`: 406/406 (197 unitários, 43 de componentes, 166 de integração);
  - a suíte de integração repetida mais duas vezes, sem falhas.
- **Mutações detectadas:** permissão `agents.manage` retirada da criação de credencial, `no-store` retirado e a checagem de `project.members.manage` ignorada. Cada uma derruba um teste diferente.
- **Execução real** (API local, banco de desenvolvimento):
  - o Proprietário cria o agente (201) e o Membro recebe 403;
  - a credencial é criada com `no-store`, um token bem formado e o prefixo conferindo;
  - as leituras trazem o prefixo, sem o token;
  - concessão como Colaborador (200) e como Gerente (400), revogação, remoção e desativação;
  - os eventos ficam registrados com a origem `Cli` informada pelo cabeçalho, sem o token;
  - no banco, um hash de 64 caracteres e um prefixo de 8;
  - nenhum erro no log.

### Etapa 4: autenticação de agentes

- **Esquema `AgentToken`** (`AgentTokenAuthenticationHandler`): adicional ao JWT, que continua sendo o padrão, sem seletor. A cada requisição:
  - lê `Bearer gsk_...` e recusa o token malformado;
  - busca a credencial pelo hash no `AgentCredentialDirectory`, a exceção ao filtro por organização autorizada pelo ADR-016;
  - confere a revogação, o estado do agente e a validade (`AgentCredentialMatch.Evaluate`);
  - define a organização ativa do agente (`TenantContext.SetActiveAgent`, exclusiva com a associação de pessoas);
  - monta o principal com `sub`, `actor_kind=agent`, `org`, `cid` e os escopos.
- **Recusas:** qualquer uma responde o mesmo 401, com `WWW-Authenticate: Bearer` e o corpo em Problem Details. O log traz só os IDs da credencial e do agente e o motivo, nunca o token ou o prefixo.
- **Política `Agent`:** esquema `AgentToken` e `actor_kind=agent`. A convenção a aplicava aos controllers do namespace `ProjectManager.API.Agents` (substituída depois pela marca `[AgentApi]`; veja as correções da revisão independente), junto com `AgentOrganizationFilter`: rota ou cabeçalho de organização diferentes da credencial, ou inválidos, recebem 404.
- **`GET /api/v1/agents/me`:** agente, organização e credencial em uso (prefixo, escopos e validade), na tag "Agentes" do OpenAPI.
- **Teste de arquitetura novo (`TenantFilterBypassTests`):** `IgnoreQueryFilters` só pode aparecer no `MembershipDirectory` e no `AgentCredentialDirectory`. Um uso novo faz o teste falhar até ser autorizado num ADR.
- **Adiado para a etapa 10:**
  - o registro das recusas de credenciais conhecidas em `AgentOperation` (por ora, ficam no log estruturado);
  - o limite de falhas por IP (D11).
- **Testes novos:**
  - `AgentAuthenticationTests`, 9: autenticação sem sessão de usuário; cada esquema só nas próprias rotas (JWT em rota de agente, token de agente em rotas de pessoas e anônimo, todos 401); revogação e desativação na requisição seguinte, com a resposta igual à de um token desconhecido e outra credencial ainda válida; expiração no instante exato (relógio controlado); tokens malformados e adulterados; organização só da credencial; nenhum token ou prefixo nos logs;
  - 1 unitário de arquitetura.
- **Validação:**
  - build em Release: 0 avisos e 0 erros;
  - `dotnet test`: 416/416 (198 unitários, 43 de componentes, 175 de integração).
- **Mutações detectadas:**
  - a política de pessoas nas rotas de agentes (derruba o teste de políticas e os de autenticação);
  - a revogação ignorada;
  - a expiração ignorada;
  - a guarda de organização desligada.

  Cada uma derruba o teste correspondente.
- **Incidente na verificação:** depois de uma rodada de mutações, os arquivos restaurados mantiveram a data de modificação antiga, e a compilação incremental reutilizou binários mutados. A suíte falhou até uma recompilação completa (`--no-incremental`), que voltou a 416/416. O script de mutação passou a restaurar com data nova e a recompilar por completo.
- **Execução real** (API local, banco de desenvolvimento):
  - `/agents/me` com o token responde 200, com a organização certa;
  - o JWT da pessoa em `/agents/me` e o token do agente em `/organizations` respondem 401;
  - outra organização no cabeçalho responde 404;
  - um token desconhecido responde 401 com `WWW-Authenticate: Bearer`;
  - depois da revogação, 401 com o mesmo título;
  - o log mostra só IDs e motivos, sem erros.

### Correções da revisão independente (2026-11-18)

A revisão técnica independente do pacote das etapas 1 a 4 manteve a arquitetura e pediu correções pontuais. Antes das correções, os testes estavam em 416/416 (198 unitários, 43 de componentes e 175 de integração).

- **C1, política dependente do namespace** (`ApiPolicies.cs`):
  - **mudança:** a política `Agent` passa a valer para controllers marcados com `[AgentApi]` (`AgentApiAttribute`);
  - **conferência:** a convenção confere a marca contra a rota combinada de cada ação, e `ApiPolicies.IsAgentRoute` é a regra única do prefixo, usada também pelo teste de políticas;
  - **inicialização:** uma marca fora de `/api/v1/agents`, ou uma rota nesse prefixo sem a marca, impede a API de iniciar. `Program.cs` monta os endpoints antes de começar a atender;
  - **testes (`AgentPolicyMarkerTests`, 6), com controllers de sonda** (`[NonController]`, adicionados só nos testes):
    - um controller marcado fora do namespace convencional recebe `Agent`, inclusive numa rota absoluta;
    - controllers sem marca recebem `User`, até um declarado no namespace `ProjectManager.API.Agents`;
    - as quatro incoerências impedem a inicialização: marca fora do prefixo, ação com rota absoluta fora do prefixo, rota no prefixo sem marca e rota absoluta na ação sem marca.
- **Problema encontrado durante as correções (credenciais):**
  - **sintoma:** com mais testes criando credenciais em paralelo, a transação `Serializable` da criação fez agentes diferentes, inclusive de organizações diferentes, receberem 409 ("O agente foi alterado ao mesmo tempo...");
  - **correção:** o limite de 3 ativas passou a ser garantido por uma trava otimista na linha do próprio agente (`xmin`);
  - **efeito colateral aceito:** `agents.updated_at` muda quando uma credencial é criada;
  - **teste:** `ConcurrentCreations_ForDifferentAgents_DoNotConflict` faz 8 criações simultâneas em agentes de duas organizações. O código anterior falhou nele 2 de 2 vezes; o novo passou em todas as execuções. O teste de criações simultâneas para o mesmo agente continua passando.
- **C2, isolamento multi-tenant:** `TenantFilterBypassTests` foi mantido como proteção complementar. Os 6 testes de `AgentIsolationTests` usam uma sonda com `[AgentApi]` que lê o banco com o contexto montado pelo pipeline real:
  - o agente só enxerga os agentes, credenciais, concessões, projetos e a organização da própria credencial;
  - a credencial de outra organização, buscada pelo ID, dá 404;
  - organização divergente na rota ou no cabeçalho dá 404, inclusive com dois valores, `Guid.Empty` e valor inválido; o mesmo GUID em maiúsculas é aceito;
  - as rotas de pessoas recusam a credencial com 401;
  - outra organização não consegue revogar nem desativar (404), e a credencial continua válida;
  - **projetos com ou sem concessão:** não são alcançáveis antes da etapa 7, que cria as rotas de projeto para agentes. O acesso por concessão (`AgentAccessGrant`) será testado nas etapas 5 e 7;
  - **mutações detectadas:** a guarda sem checar a rota, e a credencial fora do filtro por organização, que derruba 4 dos 6 testes.
- **C3, recusas equivalentes:** `EveryRejection_IsIndistinguishable_AndRevealsNothingAboutTheCredential` compara cinco casos: token desconhecido, malformado, revogado, expirado (com relógio controlado) e de agente desativado.
  - **o que é comparado:** o status, todos os cabeçalhos (exceto a data) e o corpo (exceto o ID de rastreio);
  - **o que o corpo não pode conter:** IDs de agente, de credencial e de organização, prefixos, ou palavras como "revog", "expir", "desativ" e "desconhec";
  - **mutação:** um cabeçalho que expõe o estado da credencial só é detectado por este teste;
  - **limite conhecido:** o tempo de resposta não é comparado. O token malformado é recusado antes da consulta ao banco e responde mais rápido. Isso não ajuda a enumerar credenciais, porque o formato é público e um token válido tem 256 bits aleatórios. O limite por IP da etapa 10 reduz o volume de tentativas.
- **C4, integridade dos escopos:**
  - **quem grava:** só o construtor do domínio (`AgentScopes.Normalize`). Nenhum código de produção escreve em `agent_credentials` por SQL ou `ExecuteUpdate`;
  - **restrição:** `CHECK ck_agent_credentials_scopes_known`, na migração `AddAgentCredentialScopesCheck` (`AddAgents` intacta). Aceita só os escopos de `AgentScopes.All` e nenhum elemento nulo. O texto é gerado do catálogo do domínio, então um escopo novo exige uma migração;
  - **testes:**
    - cada escopo do catálogo é gravável sozinho, o que mantém a sincronia;
    - escopo desconhecido, maiúsculas, espaço, nulo e inserção direta são recusados;
    - a migração mantém credenciais válidas existentes;
  - **mutações:** retirar um escopo da restrição é detectado. Retirar a cláusula do nulo **não** é detectado, porque o `<@` do PostgreSQL já recusa arrays com nulo. É uma mutação equivalente; a cláusula fica explícita como defesa;
  - **não restringido:** escopos repetidos, porque o domínio deduplica e a semântica é de conjunto;
  - **banco de desenvolvimento local:** migração aplicada.
- **Proteções obrigatórias (pendentes, etapa 10):**
  - `AgentOperation`;
  - `last_used_at`;
  - limite de falhas por IP e rate limiting por credencial.

  **Onde estão no documento:** no plano (linha da etapa 10), nos critérios de aceitação ("Abuso de autenticação" e "Último uso da credencial") e na definição de conclusão.

  **Dependências conferidas:**
  - a autenticação (etapa 4) já existe;
  - as tabelas de idempotência, para a retenção, vêm da etapa 8;
  - os resultados de autorização das etapas 5 a 8 entram em `AgentOperation` à medida que existirem.
- **Validação:**
  - `dotnet restore` e `dotnet build -c Release --no-incremental`: 0 avisos e 0 erros;
  - `dotnet test -c Release --no-build`: **432/432** (198 unitários, 43 de componentes, 191 de integração), em 3 execuções seguidas sem falhas;
  - antes das correções: 416/416. São 16 testes de integração novos: 6 da marca, 1 de concorrência, 6 de isolamento, 1 de recusas e 2 de escopos.
- **Regressão das Fases 01 a 09:**
  - **o que poderia ter sido afetado:** a política de todas as rotas (convenção e montagem dos endpoints na inicialização). As demais mudanças ficam restritas a agentes;
  - **coberto pela suíte:** `EndpointPolicyTests` e os testes de integração das Fases 01 a 09, inclusive `ActivityCoverageTests`, que exercita todas as rotas de escrita;
  - **execução real** com a API e a Web locais, sem erros nos logs:
    - na API, a entrada e todas as rotas de leitura das Fases 03 a 09 responderam 200: organização, membros, papéis, convites, dashboards, atividades, projetos, fases, marcos, workflow, documentos, decisões, progresso, estatísticas, tarefas, histórico, comentários e anexos;
    - na Web, a página inicial e a de entrada responderam 200, e uma página protegida redirecionou para a entrada;
  - **não verificado:** a navegação autenticada pela Web no navegador. O ambiente recusou exibir as credenciais de teste. Os testes de componentes (43) cobrem as telas.

### Etapa 5: `AgentAccessGrant` e regras de transição para agentes

- **`AgentAccessGrant.Compute`** (`Application/Projects/AgentAccessGrant.cs`, ao lado de `ProjectAccessGrant`):
  - **recebe:** o agente (`ActiveAgent`, com os escopos), o estado do agente, o projeto, o papel na concessão e a exigência da operação (`AgentRequirement`);
  - **devolve:** `AgentAccessDecision`, com o resultado e a concessão. As recusas possíveis, na ordem:
    1. `AgentDisabled`;
    2. `ProjectNotVisible`, quando não há concessão ou o papel é Gerente. A visibilidade vem antes do escopo, para não revelar projetos;
    3. `ScopeMissing`;
    4. `PermissionMissing`;
  - **permissões:** papel do projeto ∩ permissões possíveis para agentes (`project.read`, `tasks.write`, `documents.write`) ∩ permissões dos escopos da credencial.
- **`AgentRequirement`:** a tabela única de escopos da D4, com o escopo, a permissão e o tipo da operação.
- **`ProjectAccessGrant`:** ganha `AgentId` e `ActorKind`. Para agentes, o `UserId` fica vazio.
- **`TransitionRules`:** para um ator agente, uma transição que exige aprovação dá `AgentCannotApprove`, e um destino da categoria Concluída dá `AgentCannotEnterDone`, antes das regras de pessoas e das dependências.
  - As regras de pessoas não mudaram. A cláusula antiga "agente responsável não aprova" foi substituída pela regra geral.
  - `IsOffered` é a mesma regra para montar as transições disponíveis.
- **`TaskService`:**
  - **transições disponíveis:** calculadas por `TransitionRules.IsOffered`, no lugar da cópia da regra;
  - **ator da transição:** vem da concessão (`TransitionActorOf`), e agentes nunca contam como responsáveis;
  - **novas recusas:** `AgentCannotApprove` e `AgentCannotEnterDone` respondem 403 com mensagens próprias.
- **Mudança de comportamento intencional:** os casos de agente do teste antigo (`WorkflowRulesTests`) esperavam `Allowed` para um agente com permissão de aprovar que não fosse responsável. Pela decisão 10, agora o resultado é `AgentCannotApprove`, e os casos foram atualizados.
- **Testes novos:**
  - `AgentTransitionRulesTests` (12):
    - aprovação recusada em todas as combinações;
    - atalho personalizado para Concluída recusado, até com dependências abertas;
    - Cancelada permitida sem aprovação e recusada com aprovação;
    - revisão permitida;
    - **equivalência para pessoas:** as transições oferecidas são exatamente as da fórmula anterior, em todas as 80 combinações;
  - `AgentAccessGrantTests` (8):
    - a tabela de escopos;
    - Colaborador e Observador;
    - escopo ausente;
    - projeto sem concessão ou com papel Gerente;
    - agente desativado;
    - nenhuma permissão de gestão, em todos os papéis e nos 32 conjuntos de escopos;
    - o agente como ator;
  - `AgentTransitionServiceTests` (1), no `TaskService` real com um workflow personalizado ("Em andamento → Concluída" sem aprovação e Cancelada):
    - o Colaborador humano continua vendo o atalho;
    - o agente vê as mesmas transições, menos Concluída;
    - sem `tasks:write`, o agente não vê nenhuma;
    - na revisão, Concluída com aprovação aparece para o Gerente e nunca para o agente.
- **Ainda não testado:** a execução de uma transição por um agente no serviço. Ela depende do ator nos serviços (etapa 6), porque `TransitionAsync` ainda recebe a associação de uma pessoa, e da rota da etapa 8. A recusa já está garantida pela regra única, que o serviço usa.
- **Validação:**
  - build em Release: 0 avisos e 0 erros;
  - `dotnet test`: **454/454** (219 unitários, 43 de componentes, 192 de integração), em duas execuções;
  - antes da etapa: 432/432.
- **Mutações:**

  | Mutação | Resultado |
  |---|---|
  | Regra de Concluída só para agentes que podem aprovar | Detectada |
  | `tasks.approve` acrescentado à tabela de escopos | Detectada pelo teste da tabela. O teste das permissões de gestão continuou passando, o que mostra que o conjunto de permissões possíveis barra a permissão por conta própria |
  | Checagem de escopo removida | Detectada |
  | Transições disponíveis com a fórmula anterior | Detectada pelo teste do serviço. Os testes de pessoas continuaram passando, como esperado |
  | `tasks.approve` no conjunto de permissões possíveis | **Não detectada.** É uma mutação equivalente: nenhum escopo concede `tasks.approve`, então as duas barreiras (tabela de escopos e permissões possíveis) são independentes, e tirar só uma não muda o resultado |

### Etapa 6: ator nos serviços, autoria e versão das tarefas

- **Ator nos serviços:**
  - `TaskService.CreateAsync` e `TransitionAsync` deixaram de receber a associação de uma pessoa (`ActiveMembership`). O autor vem da concessão (`ProjectAccessGrant.ActorId` e `ActorKind`), que pode ser de uma pessoa ou de um agente;
  - o histórico de estados grava o ator e o tipo da concessão, e não mais `ActorKind.User` fixo;
  - `HttpActivityContext` registra `ActorKind.Agent` quando o principal tem `actor_kind=agent`.
- **Operações só de pessoas:** documentos (criar, editar, arquivar), decisões (propor, aceitar, rejeitar) e responsáveis de tarefa recusam uma concessão de agente com 403 e `ProjectAccessGrant.PeopleOnlyMessage`, sem gravar nada. É defesa em profundidade: nenhuma rota de agente chama esses serviços.
- **Autoria das tarefas:**
  - `ProjectTask.CreatedByAgent` para agentes;
  - `CreatedByUserId` passou a anulável e entrou `CreatedByAgentId`, ambos com setter privado.
- **Migração `AddTaskAuthorshipAndVersion`:**
  - `created_by_user_id` anulável;
  - `created_by_agent_id`, com FK composta para `agents` pela organização;
  - `CHECK ck_tasks_single_author` (`num_nonnulls(...) = 1`);
  - gatilho `tasks_author_immutable`, escrito à mão, que recusa trocar o autor com `check_violation`;
  - `version integer NOT NULL DEFAULT 1`.
  - No banco de desenvolvimento local, as 10 tarefas existentes ficaram com o autor original e a versão 1.
- **Versão:**
  - `ProjectTask.Revision` (coluna `version`) é token de concorrência;
  - `TaskRevisionRules`, chamado no `SaveChanges`, é o ponto único que a incrementa a cada alteração da linha da tarefa: edição (inclusive só de etiquetas), transição e arquivamento;
  - responsáveis e comentários não mudam a versão;
  - `expectedVersion` é opcional no `PATCH` e na transição de pessoas. Uma versão divergente responde 409 sem sobrescrever;
  - a Web passa a enviar a versão em que a tarefa foi aberta, num campo oculto, como já fazia nos documentos.
- **Leituras:**
  - `TaskDetails` ganhou `createdBy { id, kind, name }` e `version`. `createdByUserId` ficou anulável; é mudança de contrato registrada no ADR-016, e a Web foi ajustada;
  - o histórico e as atividades resolvem nomes de agentes, além dos de pessoas.
- **Problema encontrado e corrigido (código das Fases 06 e 08):**
  - **sintoma:** edições simultâneas da mesma tarefa às vezes causavam um deadlock no PostgreSQL (40P01). Ele chegava embrulhado como "falha transitória" e virava 500;
  - **causa:** o `PATCH` marcava a tarefa inteira como modificada para forçar o `UPDATE` quando só as etiquetas mudavam. Isso reescrevia todas as colunas, inclusive as de chaves e de autoria;
  - **correção:** agora só a versão é marcada, o que basta para forçar o `UPDATE` e conferir a concorrência;
  - **verificação:** em 5 execuções dos testes de agentes, nenhum deadlock voltou a aparecer no log;
  - **proteção extra:** `SaveChangesConflicts` passa a tratar o 40P01 como conflito (409, para nova tentativa), como já fazia com a falha de serialização.
- **Testes novos:**
  - `TaskAuthorshipAndVersionTests` (8):
    - migração com tarefa existente;
    - autor duplo, ausente ou trocado recusado pelo banco, e agente de outra organização recusado pela FK;
    - tarefa criada por concessão de agente, com autoria, histórico e nome;
    - operações só de pessoas recusadas sem gravar;
    - autor da auditoria numa requisição real de agente (`Agent`, origem `Mcp`);
    - nomes de agentes nas atividades;
    - versão em todos os caminhos, com 409 sem sobrescrever na edição e na transição;
    - 5 edições simultâneas com a mesma versão: exatamente uma vence;
  - 1 de componente: o formulário leva a versão.
- **Validação:**
  - build em Release com `--no-incremental`: 0 avisos e 0 erros;
  - `dotnet test`: **463/463** (219 unitários, 44 de componentes, 200 de integração);
  - antes da etapa: 454/454;
  - os testes de agentes e de tarefas foram repetidos 4 vezes, sem falhas.
- **Mutações detectadas:**

  | Mutação | Resultado |
  |---|---|
  | Gatilho de autoria removido da migração | Detectada |
  | Autor da auditoria sempre pessoa | Detectada |
  | Histórico com `ActorKind.User` fixo | Detectada |
  | Versão sem incremento | Detectada |
  | Checagem da versão esperada removida do serviço | Detectada pelo teste de versão. O teste de edições simultâneas continuou passando, porque o token no banco recusa sozinho: são duas barreiras independentes |
- **Execução real** (API local, banco de desenvolvimento):
  - tarefa criada com a versão 1 e o autor `User`;
  - edição com a versão certa: 200, versão 2;
  - com a versão desatualizada: 409, e o título não mudou;
  - sem versão (cliente antigo): 200, versão 3;
  - nenhum erro no log;
  - a tarefa de verificação foi arquivada.
- **Ainda não testado:** a autoria de agente de ponta a ponta por uma rota de agente. As rotas de escrita de agentes chegam na etapa 8. Por ora, a criação por agente é testada no serviço real com a concessão calculada por `AgentAccessGrant`.

### Etapa 7: rotas de leitura de agentes

- **Rotas** (`AgentProjectsController`, `[AgentApi]`, tag "Agentes"):
  - `GET /api/v1/agents/projects`: projetos concedidos, com o papel e se estão arquivados. Exige `context:read`;
  - `GET /api/v1/agents/projects/{projectId}/context`: `context:read` e `project.read`;
  - `GET /api/v1/agents/projects/{projectId}/tasks`: `tasks:read`, com os filtros e a paginação da lista de pessoas;
  - `GET /api/v1/agents/tasks/{taskId}`: `tasks:read`, com a versão, o autor e as transições que este agente pode executar.
- **Autorização:** `AgentProjectPermission` e `AgentTaskPermission` leem a concessão e o estado do agente (`IAgentProjectAccess`, sempre com o filtro da organização da credencial) e decidem só por `AgentAccessGrant.Compute`.
  - **Respostas:** projeto sem concessão, de outra organização ou inexistente dá 404; escopo ou permissão ausente dá 403; agente desativado dá 401.
  - **Ordem:** a visibilidade vem antes do escopo, então um agente sem o escopo não descobre se um projeto sem concessão existe.
- **Contexto** (`AgentProjectContext`):
  - **projeto:** dados, papel e permissões do agente;
  - **workflow:** estados e transições, cada uma com `availableToAgent`, calculado por `TransitionRules.IsOffered`, mais `tasks.write` e projeto não arquivado;
  - **progresso:** pela regra única (`IDashboardService`);
  - **decisões:** até 50 aceitas, com o resultado limitado a 500 caracteres;
  - **documentos:** até 100 não arquivados, só título e versão;
  - **atividades:** as 20 mais recentes, com o resumo e o tipo e o nome do autor;
  - **fora do contexto:** e-mails, a lista de membros e o conteúdo dos documentos.
- **Ajuste no código de pessoas:** `ITaskService.ListAsync` passou a receber a concessão em vez da associação de uma pessoa. O filtro "atribuídas a mim" usa a pessoa da concessão; para agentes, nunca há tarefas atribuídas. A rota de pessoas não mudou.
- **Testes novos (`AgentReadRoutesTests`, 6):**
  - projetos concedidos com o papel, e 403 sem `context:read`;
  - contexto completo, com estas verificações:
    - nenhuma transição com aprovação ou para Concluída aparece como disponível;
    - só decisões aceitas, com o resultado truncado;
    - documento sem o conteúdo;
    - exatamente 20 atividades;
    - sem o e-mail do dono nem membros;
  - Observador lê o contexto sem nenhuma transição disponível;
  - matriz de 404 e 403 em todas as rotas: projeto sem concessão, de outra organização e inexistente; tarefa de projeto sem concessão e de outra organização; cada escopo ausente;
  - lista e detalhe de tarefas, com a página acima de 100 recusada (400), a versão, o autor e sem `tasks.approve`;
  - projeto arquivado continua legível, sem nenhuma transição disponível.
- **Validação:**
  - build em Release: 0 avisos e 0 erros;
  - `dotnet test`: **469/469** (219 unitários, 44 de componentes, 206 de integração), em duas execuções;
  - antes da etapa: 463/463.
- **Mutações detectadas:**
  - concessão ignorada no resolvedor;
  - transições sempre disponíveis no contexto;
  - decisões sem o filtro de aceitas.
- **Execução real** (API local, banco de desenvolvimento):
  - um agente com `context:read` e `tasks:read`, concedido como Colaborador, recebeu 200 nas quatro rotas;
  - o contexto veio com 6 estados, 14 transições e 0 disponíveis (a credencial não tem `tasks:write`), 20 atividades e sem o e-mail do dono;
  - a tarefa não oferece nenhuma transição para Concluída;
  - organização divergente no cabeçalho deu 404, e depois da revogação, 401;
  - nenhum erro no log;
  - o agente de verificação foi desativado.
- **Ainda não feito:** `AgentOperation` (etapa 10). As leituras de agentes ainda não ficam registradas na auditoria de operações.

### Etapa 8: idempotência e rotas de escrita de agentes

- **Rotas** (`AgentWritesController`, `[AgentApi]`, tag "Agentes"), pelos mesmos serviços das pessoas:
  - `POST /api/v1/agents/tasks`: `projectId` no corpo, `tasks:write` e `tasks.write`; 201 com `Location`. O agente é o autor, e agentes não definem responsáveis;
  - `PATCH /api/v1/agents/tasks/{taskId}`: `expectedVersion` obrigatório (sem ele, 400); versão divergente dá 409, sem sobrescrever. Sem `Idempotency-Key`, porque a versão já protege;
  - `POST /api/v1/agents/tasks/{taskId}/transitions`: `expectedVersion` e `Idempotency-Key` obrigatórios. A recusa de aprovação e de Concluída vem de `TransitionRules` (403), inclusive num atalho sem aprovação para Concluída;
  - `POST /api/v1/agents/activities`: `projectId` no corpo, `activities:write` e `documents.write`; registro manual com o ator agente.
- **Autorização das rotas com o projeto no corpo:** a ação chama `IAgentProjectAccess` e decide por `AgentAccessGrant.Compute`, com as mesmas respostas dos atributos (404 sem concessão, 403 sem escopo ou permissão).
- **Idempotência** (`AgentIdempotentAttribute`, `IAgentIdempotencyStore`, tabela `idempotency_records`, migração `AddIdempotencyRecords`):
  - filtro de recurso, depois da autenticação e da autorização da rota;
  - uma transação por requisição: trava consultiva `pg_try_advisory_xact_lock` pela credencial e pela chave (409 imediato se estiver presa), leitura do registro, ponto de salvamento, operação e gravação da resposta, e só então o commit;
  - repetição idêntica: reavalia o acesso ao projeto guardado e devolve o código e o corpo originais com `Idempotency-Replayed: true`. Sem acesso, a recusa atual (403 ou 404); credencial revogada, 401 na autenticação;
  - outra requisição com a mesma chave (método, rota, caminho, query ou corpo diferentes): 422;
  - guardados: 2xx, 400, 409 e 422. Nas recusas guardadas, o que a operação gravou é desfeito pelo ponto de salvamento antes de guardar a resposta;
  - não guardados: 401, 403, 404, 429 e 5xx, e as exceções. A transação é desfeita e a chave fica livre;
  - registro expirado (24 horas) é apagado e substituído na mesma transação.
- **Ajustes ao texto aprovado do D9** (registrados na especificação e no ADR-016):
  - o hash inclui o caminho concreto e a query, além do corpo. Sem isso, a mesma chave e o mesmo corpo em outra tarefa (`tasks/{taskId}/transitions`) devolveriam a resposta da primeira tarefa;
  - a resposta guardada vai até 256 KB em vez de 64 KB: o detalhe de uma tarefa com a descrição máxima pode passar de 64 KB depois do escape JSON. Uma resposta maior que o limite faz a gravação falhar, e a operação é desfeita (500), nunca gravada sem registro;
  - o registro guarda o projeto, não a tarefa: a tarefa nunca muda de projeto, e as rotas por tarefa já reavaliam o acesso pela tarefa (`AgentTaskPermission`) antes da idempotência.
- **Transações dos serviços:** nenhum serviço usado pelas rotas de agentes abre transação própria (hoje só `AddDependencyAsync`, `PlanningService` e `ProjectService` abrem, fora dessas rotas). Uma segunda transação faria o EF Core lançar exceção e a rota responder 500; os testes de escrita executam todas as rotas de agentes dentro da transação da idempotência e falhariam.
- **Auditoria:** `ActivityCoverageTests` cobre as três rotas de escrita de tarefas de agentes (`task.created`, `task.updated` e `task.transitioned`). O registro manual de agentes entra nas exceções, como o das pessoas: o próprio registro é a atividade.
- **Testes novos:**
  - `AgentWriteRoutesTests` (5):
    - criação com o agente como autor, `Location`, sem responsáveis, e visível para a pessoa;
    - matriz de recusas: projeto sem concessão, de outra organização e inexistente (404), Observador e escopos ausentes (403), sem `projectId` (400); nenhuma tarefa criada;
    - `PATCH` sem versão (400), com versão atual (200, versão 2) e desatualizada (409);
    - transições: sem versão (400), versão errada (409), passos válidos (200), atalho para Concluída e saída da revisão com aprovação (403);
    - registro de atividade com o agente como autor;
  - `AgentIdempotencyTests` (8):
    - repetição sem nova tarefa, com o mesmo ID e `Idempotency-Replayed`;
    - 422 com outro corpo, outra rota e outra tarefa no caminho;
    - chave ausente, com espaço e com 101 caracteres (400); com 100, aceita;
    - chave por credencial;
    - 8 requisições simultâneas com a mesma chave: 201 ou 409, uma única tarefa;
    - reautorização: Observador (403), sem concessão (404), concessão restaurada (repetição), credencial revogada (401);
    - 404 não fica guardado; 400 e 409 ficam e são repetidos;
    - atomicidade com uma sonda que grava e depois lança exceção (500, nada gravado, a chave executa de novo), responde 503 (nada gravado) ou 409 (resposta guardada, operação desfeita);
  - `IdempotencyRecordTests` (unitários, 13): formato e tamanho da chave, expiração exata, recusas do construtor e comparação da requisição.
- **Validação:**
  - build em Release: 0 avisos e 0 erros;
  - `dotnet ef migrations has-pending-model-changes`: nenhuma mudança pendente;
  - `dotnet test`: **495/495** (232 unitários, 44 de componentes, 219 de integração);
  - antes da etapa: 469/469.
- **Mutações detectadas:**
  - reautorização da repetição desligada;
  - hash sem o caminho e a query;
  - operação não desfeita nas recusas guardadas;
  - 404 guardado;
  - trava consultiva ignorada;
  - `PATCH` sem exigir a versão;
  - exceção guardada como resposta em vez de desfazer a operação.
- **Execução real** (API local, banco de desenvolvimento com a migração aplicada):
  - criação 201 e repetição 201 com `Idempotency-Replayed: true` e o mesmo ID; autor `Agent`, versão 1;
  - mesma chave com outro corpo, 422; sem chave, 400;
  - `PATCH` sem versão, 400; com a versão 1, 200 (versão 2); de novo com a versão 1, 409;
  - transições para A fazer, Em andamento e Em revisão, 200 (versões 3, 4 e 5); em revisão, Concluída não é oferecida, e a tentativa dá 403 com a mensagem de aprovação;
  - registro de atividade 201 com autor `Agent`;
  - repetição depois de remover o acesso, 404; depois de revogar a credencial, 401;
  - nenhum erro e nenhum token no log; o agente de verificação foi desativado.
- **Ainda não feito:** `AgentOperation`, rate limiting, `last_used_at`, limite por IP e retenção (etapa 10, obrigatórios). A limpeza dos registros de idempotência vencidos chega com a retenção.

### Etapa 9: relatórios de trabalho e leitura por pessoas

- **`WorkReport`** (domínio, tabela `work_reports`, migração `AddWorkReports`):
  - organização, projeto, tarefa opcional, agente e credencial;
  - **declarado:** resumo (2.000), alterações em Markdown (20.000), até 200 arquivos (500 cada), testes (comando e contagens não negativas) e commit (SHA de 40 ou 64 caracteres hexadecimais, guardado em minúsculas, e repositório);
  - **retrato do recebimento:** estado da tarefa e estado sugerido, com ID e nome do momento, sem FK (o workflow pode mudar depois), e o ID da requisição;
  - **no banco:** FKs compostas para projeto, tarefa (do mesmo projeto), agente e credencial; `CHECK` das contagens, do SHA, da quantidade de arquivos e da coerência dos retratos; gatilho que recusa `UPDATE`, `DELETE` e `TRUNCATE`, sem exceção de retenção.
- **`POST /api/v1/agents/reports`** (`reports:write` e `tasks.write`, `Idempotency-Key`):
  - valida o projeto (arquivado: 409), a tarefa (de outro projeto ou inexistente: 400) e o estado sugerido (de outro workflow: 400);
  - grava o relatório e o evento `work_report.received`, com o ator agente, no mesmo `SaveChanges`;
  - **não muda o estado da tarefa**;
  - responde 201 com o recibo da D8: `accepted`, `activityCreated`, `taskStatus`, `approvalRequired` e `suggestedTransition` (`direct` ou `none`), e os cinco níveis: declarado, aceito, `testsExecuted` e `ciConfirmed` como `notVerified`, e `humanApproval` (`pending`, ou `notApplicable` sem tarefa).
- **Leitura por pessoas** (`WorkReportsController`, `project.read`):
  - `GET /api/v1/projects/{projectId}/reports` e `GET /api/v1/tasks/{taskId}/reports`, paginadas, sem o texto longo;
  - `GET /api/v1/reports/{reportId}` com o cabeçalho de organização e `[ReportPermission]`, no modelo de `[TaskPermission]` (`IProjectAccess.FindByWorkReportAsync`);
  - **aprovação humana:** calculada na leitura. É a primeira entrada do histórico da tarefa posterior ao relatório, feita por uma pessoa e com destino em Concluída, com quem aprovou e quando.
- **Testes novos:**
  - `WorkReportRoutesTests` (7):
    - níveis separados, recibo completo e tarefa inalterada (estado e versão); leitura completa pela pessoa e nas duas listas;
    - `approvalRequired`: `false` e `true` pela transição direta, `null` sem transição direta e sem tarefa;
    - aprovação humana: mudança da pessoa que não vai para Concluída não conta; a conclusão depois do relatório conta, com quem e quando; o relatório enviado depois da conclusão fica pendente;
    - 13 recusas (validação, Observador, escopo ausente, projeto sem concessão) e projeto arquivado, sem nenhum relatório gravado;
    - `UPDATE`, `DELETE` e `TRUNCATE` recusados pelo banco (23000), numa transação sempre desfeita;
    - leitura restrita à organização e ao projeto;
    - repetição com a mesma chave devolve o mesmo recibo, sem segundo relatório;
  - `WorkReportRulesTests` (unitários, 11): SHA, limites da declaração, retrato e minúsculas;
  - `ActivityCoverageTests`: `POST /api/v1/agents/reports` com `work_report.received`.
- **Validação:**
  - build em Release: 0 avisos e 0 erros;
  - `dotnet ef migrations has-pending-model-changes`: nenhuma mudança pendente;
  - `dotnet test`: **513/513** (243 unitários, 44 de componentes, 226 de integração);
  - antes da etapa: 495/495.
- **Mutações:** 7 de 8 detectadas:
  - tarefa sem o filtro do projeto;
  - estado sugerido sem o filtro do projeto;
  - `approvalRequired` sem exigir a transição a partir do estado atual;
  - gatilho sem `DELETE`;
  - "pendente" no lugar de "não se aplica" sem tarefa;
  - aprovação sem exigir que seja posterior ao relatório;
  - aprovação sem exigir a categoria Concluída. Esta sobrevivia à primeira versão do teste, que foi reforçado.
- **Mutação equivalente** (documentada): aceitar entradas de agentes na aprovação humana não muda nada, porque agentes nunca entram em Concluída (D5, em `TransitionRules`).
- **Execução real** (API local, banco de desenvolvimento com a migração aplicada):
  - relatório 201, aceito, com a tarefa em "A fazer", `approvalRequired: false` para "Em andamento", `suggestedTransition: direct` e os cinco níveis separados;
  - repetição com a mesma chave: 201, `Idempotency-Replayed: true`, mesmo ID;
  - tarefa inalterada (estado e versão);
  - tarefa inexistente e SHA inválido: 400;
  - leitura pela pessoa: 200, autor `Agent`, aprovação humana pendente; as duas listas trazem o relatório; sem o cabeçalho de organização, 400;
  - depois de revogar a credencial, 401; nenhum erro e nenhum token no log.
- **Ainda não feito:** a tabela `work_report_verifications` (Fase 12) e as telas de relatórios (etapa 11). Continuam obrigatórios na etapa 10: `AgentOperation`, rate limiting, `last_used_at`, limite por IP e retenção.

### Etapa 10: auditoria de operações, rate limiting, último uso, limite por IP e retenção

Etapa obrigatória. Sem ela, a autenticação de agentes não estava pronta para produção.

- **Entrada das rotas de agentes** (`AgentRequestMiddleware`, logo depois de `UseAuthentication`, só para `/api/v1/agents`):
  1. token `gsk_` de um IP acima do limite de falhas: 429 com `Retry-After`, **antes de qualquer consulta ao banco**;
  2. autenticação explícita pelo esquema `AgentToken`. O ASP.NET Core guarda o resultado por requisição, e a política `Agent` da rota reaproveita, sem nova consulta. Com as políticas explícitas da etapa 1, o `UseAuthentication` só roda o JWT, o esquema padrão; sem esta chamada, a autenticação do agente aconteceria só na autorização, depois da organização ativa;
  3. rate limiting da credencial (D11);
  4. o resto do pipeline (organização ativa, autorização por escopo e projeto, idempotência e ação);
  5. no fim, com a credencial identificada, a linha de `AgentOperation`.
- **`AgentOperation`** (tabela `agent_operations`, migração `AddAgentOperationsAndRateLimits`):
  - **campos da D10:** agente, credencial, data, duração, método, modelo da rota (com as restrições, por exemplo `/api/v1/agents/projects/{projectId:guid}/context`), projeto e tarefa, status, resultado da autorização, código da recusa, `correlation_id` (o `TraceIdentifier`), repetição de idempotência e origem (`X-Gestalyn-Client`);
  - **projeto e tarefa** só entram quando o acesso foi concedido. IDs enviados pelo cliente numa requisição recusada nunca são gravados;
  - **quem preenche o motivo:**
    - o handler: `CredentialRevoked`, `CredentialExpired` e `AgentDisabled`;
    - a guarda de organização: `OrganizationMismatch`;
    - `ToResult`, o ponto único das decisões de `AgentAccessGrant`: `ProjectNotVisible`, `ScopeMissing`, `PermissionMissing` e `AgentDisabled`;
    - o rate limiting: `RateLimited`;
    - as regras de negócio: `BusinessRuleDenied`. `ResultError` ganhou um `Code` opcional (`agent_cannot_approve`, `agent_cannot_enter_done`, `people_only`), que `ToProblem` repassa. Um 403 sem código vira `business_rule`;
  - **gravação:** num escopo de DI e num `DbContext` próprios, fora da transação da operação, sem o token de cancelamento da requisição. A organização do escopo é a da credencial, e o isolamento confere a gravação. Uma falha vira log de erro e deixa `agents-audit` em `Degraded` até a próxima gravação bem-sucedida. A requisição nunca falha por causa disso;
  - **tokens inexistentes ou malformados:** só log e o limite por IP;
  - **no banco:** gatilho que recusa `UPDATE` e `TRUNCATE` sempre, e `DELETE` de linha com até 180 dias;
  - **leitura:** `GET /api/v1/organizations/{organizationId}/agents/{agentId}/operations` (`agents.manage`), paginada, com filtros `result`, `from` e `to` (início depois do fim: 400). Agente de outra organização dá 404.
- **Rate limiting por credencial** (`AgentRateLimiter`, tabela `agent_rate_limit_counters`):
  - o upsert da D11, com o relógio do banco, e `RateLimiting:Agents` (120 por 60 segundos), validado na inicialização;
  - acima do limite: 429 em Problem Details com `Retry-After`. Só a primeira rejeição da janela vai para a auditoria;
  - a primeira requisição da janela grava `last_used_at`: no máximo uma gravação por janela;
  - banco indisponível no contador: 503.
- **Limite de falhas por IP** (`AgentAuthFailureLimiter`, `RateLimiting:AgentAuthFailures`, 30 por minuto):
  - em memória, por instância, num `MemoryCache` limitado a 100.000 IPs (`MaxTrackedAddresses`), para não crescer sem fim sob um ataque distribuído;
  - conta só tokens `gsk_` inexistentes ou malformados. Atrás de um proxy, depende dos forwarded headers (Fase 14).
- **Retenção** (`AgentRetentionCleaner` e `AgentRetentionService`, `Agents:Retention`):
  - `PeriodicTimer` com atraso inicial, trava `pg_try_advisory_lock` (com outra instância limpando, o ciclo é pulado), lotes de até 1.000 linhas, cada lote numa transação com `statement_timeout` de 30 segundos;
  - operações com mais de 180 dias, registros de idempotência com mais de 7 e contadores com mais de 1;
  - a API recusa iniciar com a retenção das operações abaixo de 180 dias;
  - `Enabled` existe só para os testes, que executam o ciclo diretamente.
- **Health checks:**
  - `agents-audit`: `Degraded` enquanto a última gravação falhou;
  - `agents-retention`: `Degraded` sem ciclo, concluído ou pulado pela trava, há mais de 3 intervalos.
- **Testes novos:**
  - `AgentOperationsTests` (9):
    - todas as recusas classificadas, com o modelo da rota, projeto e tarefa só com acesso, repetição, origem, `correlation_id` e nenhuma coluna com o token, o prefixo ou o corpo;
    - credenciais revogada, expirada e de agente desativado registradas; tokens inexistentes e malformados, não;
    - a operação fica mesmo com o rollback da requisição;
    - 429 com `Retry-After`, só a primeira rejeição gravada, cota separada por credencial;
    - 12 requisições simultâneas com limite 5: exatamente 5 passam, e o contador chega a 12;
    - último uso só na primeira requisição da janela;
    - tokens inexistentes barrados por IP antes da consulta: nem uma credencial válida do mesmo IP é procurada, e nada vai para a auditoria. Rotas de pessoas não passam por esse limite;
    - falha da gravação sem afetar a requisição, com `agents-audit` degradado e depois recuperado;
    - filtros, paginação, período invertido (400), isolamento (404) e permissão (403) da rota de operações;
  - `AgentRetentionTests` (4, em sequência entre si):
    - o ciclo apaga só o vencido em duas organizações, mantém os relatórios e não apaga nada ao repetir;
    - `DELETE` de operação recente, `UPDATE` e `TRUNCATE` recusados pelo banco, numa transação sempre desfeita;
    - ciclo pulado com a trava de outra instância;
    - a API não inicia com retenção de 179 dias;
  - `AgentOperationRulesTests` (unitários, 16): contagem, `Retry-After`, saúde, opções e limites dos campos;
  - `HealthEndpointTests`: passa a esperar os dois health checks novos.
- **Validação:**
  - build em Release: 0 avisos e 0 erros;
  - `dotnet ef migrations has-pending-model-changes`: nenhuma mudança pendente;
  - `dotnet test`: **542/542** (259 unitários, 44 de componentes, 239 de integração);
  - antes da etapa: 513/513.
- **Mutações:**
  11 de 11 detectadas, cada uma pelo teste esperado:
  - limite com `>=` no lugar de `>`;
  - toda rejeição gravada, não só a primeira da janela;
  - último uso gravado em toda requisição;
  - credencial recusada sem identificação (fora da auditoria);
  - token inexistente sem contar para o limite por IP;
  - limite por IP nunca bloqueando;
  - "projeto não visível" classificado como "escopo ausente". A primeira versão desta mutação não compilava e foi refeita;
  - gatilho aceitando o `DELETE` de qualquer operação;
  - retenção das operações com o prazo dos registros de idempotência;
  - trava consultiva ignorada;
  - código da regra de negócio descartado.
- **Execução real** (API local com a configuração padrão, banco de desenvolvimento com a migração aplicada):
  - requisição comum 200 e organização divergente 404; último uso gravado;
  - rajada de 125 requisições: 118 com 200, completando as 120 da janela, e 7 com 429; a seguinte, 429 com `Retry-After: 5`;
  - 121 operações gravadas: as 120 permitidas, inclusive a organização divergente, e só uma `RateLimited`; origem `Cli`;
  - credencial revogada: 401, com uma operação `CredentialRevoked`;
  - 31 tokens desconhecidos do mesmo IP: 30 com 401 e o seguinte com 429;
  - `/health`: `database`, `agents-audit`, `agents-retention` e `self` saudáveis;
  - o primeiro ciclo de limpeza rodou um minuto depois da inicialização, sem nada vencido;
  - nenhum erro e nenhum token no log.
- **Ainda não feito:** as telas (etapa 11) e a suíte de segurança, a ponta a ponta e o encerramento (etapa 12). A latência adicional por requisição de agente (busca do hash, upsert do contador e gravação da operação) fica para a medição da Fase 13, como prevê o ADR-016.

### Etapa 11: telas Web de agentes, credenciais, acesso, operações e relatórios

Implementa a D13 e a tarefa F09-018, transferida da Fase 09. As telas seguem o ADR-015: SSR com formulários e antiforgery, sem script embutido, e o que aparece depende só das permissões e dos dados que a API devolve.

- **Clientes da Web:** `AgentsApi` (agentes, credenciais, projetos e operações) e `WorkReportsApi` (relatórios por projeto, por tarefa e por ID), com os contratos espelhados em `ApiAgentModels.cs`. A Web continua sem referenciar `Domain` ou `Application`.
- **Telas novas** (`Components/Pages/Agents` e `Components/Pages/Reports`):
  - `/o/{org}/agentes`: lista e criação. A criação redireciona para o agente com um aviso;
  - `/o/{org}/agentes/{id}`:
    - credenciais com nome, prefixo, escopos, situação, expiração e último uso;
    - criação de credencial (escopos marcados um a um, validade de 1 a 365 dias) e revogação com confirmação;
    - projetos concedidos, com concessão, troca de papel e remoção com confirmação. Só os papéis Colaborador e Observador são oferecidos, e só os projetos ainda não concedidos. Quem não gerencia a equipe do projeto recebe a recusa da API;
    - as 10 operações mais recentes, com o resultado da autorização;
    - dados, e desativação (com confirmação) ou reativação;
  - `/o/{org}/agentes/{id}/operacoes`: operações com filtros de resultado e período e paginação;
  - `/o/{org}/projetos/{id}/relatorios` (aba nova do projeto) e `/o/{org}/relatorios/{id}`: relatórios com os níveis da D8.
- **Permissão:** o menu mostra "Agentes" só com `agents.manage`. Sem ela, as telas de agentes respondem "Sem permissão" sem chamar a API de agentes; a API recusaria do mesmo jeito.
- **Token exibido uma vez** (D13):
  1. o token aparece na resposta do próprio formulário de criação, sem post-redirect-get e sem navegação aprimorada; a lista de credenciais é recarregada na mesma resposta;
  2. `UseNoStoreForAgentPages` põe `Cache-Control: no-store` e `Pragma: no-cache` em toda resposta de `/o/{org}/agentes/...`; a API já respondia `no-store` na criação;
  3. o token não vai para URL, aviso, sessão, cookie nem armazenamento do navegador. `CreatedAgentCredential.ToString()` não mostra o token;
  4. o cliente da API só registra falhas de rede, nunca corpos; um teste confere o log do cliente na criação;
  5. o botão "Copiar" usa a Clipboard API num tratador de `wwwroot/js/app.js`, sem guardar nada;
  6. a tela avisa que o token não volta e orienta criar uma credencial nova e revogar a antiga.
- **Escopos no formulário:** caixas desmarcadas não são enviadas, então o modelo começa com tudo desmarcado e a tela marca `context:read` e `tasks:read` só na primeira abertura. Um padrão `true` no modelo não poderia ser desmarcado.
- **Autoria "Agente: nome"** (`Display.Actor`): na tarefa ("Criada por", campo novo), no histórico de estados e nas atividades. Agente sem nome aparece como "Agente: removido".
- **Relatórios na tarefa:** a tarefa ganhou a seção "Relatórios de agentes".
- **Níveis de evidência** (`WorkReportEvidenceList`), sempre separados: "Declarado pelo agente", "Recebido e validado pela API", "Testes não verificados", "CI não verificado" e "Aprovação humana pendente" ou "Aprovado por {pessoa} em {data}". Sem tarefa, "não se aplica". Na tela do relatório, cada bloco é rotulado como declarado, e o Markdown das alterações passa pelo `MarkdownView`.
- **Testes novos** (bUnit, `tests/ProjectManager.WebTests`):
  - `AgentPagesTests` (14):
    - menu e lista conforme `agents.manage`;
    - credenciais pelo prefixo e sem token, revogação e desativação com confirmação;
    - só Colaborador e Observador, e só projetos não concedidos;
    - operações com o modelo da rota, o resultado e a repetição;
    - token uma vez: na resposta do formulário, sem navegar, fora da URL e do log, com os escopos marcados no corpo enviado, e ausente ao reabrir a tela;
    - recusa da API sem token;
    - agente desativado; agente de outra organização (404);
    - filtros da tela de operações; rotas cobertas pelo `no-store`; `ToString()` sem o token;
  - `WorkReportPagesTests` (7): os cinco níveis, a aprovação com nome e data, "não se aplica", a tela do relatório, a aba do projeto, a autoria e os relatórios na tarefa, e "Agente: nome" nas atividades.
- **Validação:**
  - build em Release: 0 avisos e 0 erros;
  - `dotnet test`: **563/563** (259 unitários, 65 de componentes, 239 de integração);
  - antes da etapa: 542/542.
- **Mutações:** 10 de 10 detectadas, cada uma pelo teste esperado. Duas não compilavam na primeira versão (campo nunca atribuído e código inalcançável, por causa dos avisos tratados como erros) e foram refeitas:
  - redirecionar depois de criar a credencial (post-redirect-get);
  - menu mostrando "Agentes" sem a permissão;
  - lista de agentes consultando a API sem a permissão;
  - autoria de agente sem o rótulo "Agente:", na tarefa e nas atividades (duas mutações);
  - `no-store` só na lista, sem as telas de cada agente;
  - aprovação pendente exibida como aprovada;
  - `ToString()` com o token;
  - projetos já concedidos oferecidos de novo;
  - escopos sem a marcação da primeira abertura.
- **Execução real** (API e Web locais, banco de desenvolvimento): roteiro por HTTP com o cookie de sessão e os formulários reais, imprimindo só códigos e booleanos:
  - login na Web e lista de agentes com `Cache-Control: no-store` e o item "Agentes" no menu;
  - criar agente (302 para o agente, com aviso) e conceder um projeto;
  - criar credencial: 200, sem redirecionamento, `no-store`, o token **uma única vez** na página e o aviso de que ele não volta. O token exibido funcionou na API (`GET /agents/me`: 200);
  - reabrir a tela: token ausente, credencial listada pelo prefixo e último uso preenchido;
  - revogar pela Web: 302 com aviso; o token passou a receber 401;
  - operações com "Permitida" e "Credencial revogada", o modelo da rota e nenhum token; o filtro de revogadas mostra só as revogadas;
  - desativar: a tela passa a oferecer só a reativação;
  - relatório real da etapa 9: aba do projeto, tela do relatório e tarefa com os quatro níveis, "Aprovação humana pendente" e "Agente: nome";
  - colaboradora sem `agents.manage`: "Sem permissão" e menu sem "Agentes";
  - nenhum `gsk_` e nenhum erro nos logs da API e da Web.
- **Correção encontrada na execução real:** a API devolve o prefixo sem o marcador (`Q2GnYhfx`), e a tela o mostrava assim, diferente do começo do token que a pessoa guardou. A tela passou a mostrar `gsk_Q2GnYhfx…`, e os dados dos testes passaram a usar o formato real do prefixo.
- **Documentação:** `docs/web/TELAS.md` ganhou a seção "Agentes", a aba de relatórios, a tela do relatório e a autoria de agentes.
- **Ainda não feito:** a etapa 12 (suíte de segurança, ponta a ponta, execução real completa e documentação de encerramento).

### Etapa 12: suíte de segurança, ponta a ponta, mutações, execução real e encerramento

- **Suíte de segurança:** cada critério de aceitação e cada item da estratégia de testes foi conferido contra os testes das etapas 1 a 11 (a matriz está no [relatório da fase](../reports/FASE-10-RELATORIO.md), seção 15). Faltavam:
  - o teste de ponta a ponta;
  - a escrita de agente em projeto arquivado (item 3 da estratégia: leitura sim, escrita 409).
- **Testes novos:**
  - `AgentEndToEndTests`: os oito passos da estratégia de testes com um token real. No fim, procura o token em todas as tabelas do schema público, como texto, e no log: não aparece. O hash aparece só em `agent_credentials`, o que confirma que a busca funciona;
  - `ArchivedProject_RefusesAgentWrites_WithConflict` (em `AgentWriteRoutesTests`): criação, alteração e atividade num projeto arquivado recebem 409, e a tarefa fica intacta.
- **Mutações de segurança** (as da estratégia de testes, contra os testes de agentes):
  - detectadas na primeira rodada: revogação (5 testes), expiração (3), regra de Concluída (4), organização do cabeçalho (3), replay sem reautorizar (1) e hash do corpo (1);
  - **não detectadas na primeira rodada, com os testes corrigidos:**
    - **`tasks.approve` nas permissões elegíveis:** `PermissionsFor` intersecta papel, permissões elegíveis e escopos, e nenhum escopo aponta para `tasks.approve`. A camada das elegíveis não tinha teste próprio. `ManagementPermissions_NeverReachAnAgent_ForAnyRoleOrScopeSet` passou a conferir as elegíveis e a tabela de escopos; a mutação passou a ser detectada. Uma variante mais forte, que concede `tasks.approve` depois de todas as camadas, é detectada por 5 unitários e 3 de integração;
    - **`CHECK` de autoria:** o teste só fazia `UPDATE`, e o gatilho de autoria imutável (`BEFORE UPDATE`) recusa antes, com o mesmo `check_violation`. `Database_RequiresExactlyOneAuthor_...` ganhou duas inserções (dois autores e nenhum), que só o `CHECK` recusa; a mutação passou a ser detectada.
- **Validação:**
  - `dotnet restore`: exit 0;
  - build em Release: 0 avisos e 0 erros;
  - `dotnet ef migrations has-pending-model-changes`: nenhuma mudança pendente;
  - `dotnet test`: **565/565** (259 unitários, 65 de componentes, 241 de integração);
  - antes da etapa: 563/563.
- **Execução real** (API e Web locais, banco de desenvolvimento):
  - fluxo de ponta a ponta por HTTP, com um token real: os oito passos com os códigos esperados (inclusive os dois 403 das tentativas de concluir e de aprovar, e 401 depois da revogação), 15 operações com as três recusas e um replay;
  - `pg_dump` do banco de desenvolvimento inteiro (285 KB), lido em memória: token ausente; só o hash e o prefixo;
  - leituras posteriores (operações, agente e relatório) sem o token;
  - o roteiro da Web da etapa 11 repetido, com o relatório agora "Aprovado por…";
  - nenhum `gsk_` e nenhum erro nos logs da API e da Web; os quatro health checks saudáveis.
- **Documentação de encerramento:** `docs/security/AGENTES.md` (novo), README, AGENTS.md, `docs/security/AUTENTICACAO.md`, ARCHITECTURE (seção 3.8), ADR-016 (estado e implementação das etapas 11 e 12), `PROJECT_STATUS.md` e o [relatório da Fase 10](../reports/FASE-10-RELATORIO.md).
- **Integração:** [PR #19](https://github.com/harrisonleandro/Gestalyn/pull/19), com o CI aprovado ([run 45039861969](https://github.com/harrisonleandro/Gestalyn/actions/runs/45039861969)); merge `b9e8daa` em `develop`, com o CI de `develop` aprovado ([run 45040388503](https://github.com/harrisonleandro/Gestalyn/actions/runs/45040388503)).
