# Fase 10: API e autenticação de agentes

- **Estado:** In Progress (desde 2026-11-18). As decisões arquiteturais foram aprovadas pelo responsável em 2026-11-15:
  - as 24 decisões da revisão;
  - as 4 decisões complementares (exibição do token, rate limiting, retenção e transição para Cancelada).

  A documentação corrigida depois da revisão técnica (esta especificação e o ADR-016, inclusive as escolhas técnicas que detalham as decisões) foi **aprovada pelo responsável em 2026-11-18**. Nada foi implementado.
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
  - a contagem e a criação rodam numa transação `Serializable`, como em `AddDependencyAsync`;
  - uma falha de serialização responde 409, para nova tentativa.
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

  - Uma convenção aplica `Agent` aos controllers de agentes (namespace `ProjectManager.API.Agents`, rotas `/api/v1/agents/*`) e `User` a todos os demais.
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
  - Vale `null` sem estado sugerido ou sem transição direta, e então `suggestedTransition` é `"none"`.
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
  - **SHA-256 dos bytes do corpo**;
  - projeto e tarefa afetados;
  - código e corpo da resposta (até 64 KB);
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
| 10 | `AgentOperation`, rate limiting no PostgreSQL, retenção | Concorrência do contador; 429; auditoria das negadas; ciclo de limpeza |
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
