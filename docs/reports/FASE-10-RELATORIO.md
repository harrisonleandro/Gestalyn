# Relatório final: Fase 10, API e autenticação de agentes

- **Data:** 2026-11-22
- **Ambiente:** Windows 11 Pro · .NET SDK 10.0.404 · Docker 29.8.2 · PostgreSQL 18 · Mailpit v1.29
- **Branch:** `feature/fase-10-agentes` (rebaseada em `develop` `d6c34fc`, com as Fases 01 a 09 integradas). Só commits locais: nada foi enviado ao repositório remoto.
- **Estado:** **In Review.** As 12 etapas estão implementadas e verificadas localmente. A fase só fica `Completed` depois do PR para `develop` com o check "Build e testes" aprovado (definição de conclusão), o que depende de autorização do responsável.
- **Formato:** seção 17 da [especificação mestre](../specifications/ESPECIFICACAO-MESTRE.md)

## 1. Nome e número da fase

Fase 10: API e autenticação de agentes. A especificação está em [FASE-10-AGENTES.md](../specifications/FASE-10-AGENTES.md), as decisões no [ADR-016](../decisions/ADR-016-AGENTES.md) e o modelo de segurança em [docs/security/AGENTES.md](../security/AGENTES.md). A fase inclui a interface de agentes transferida da Fase 09 (F09-018).

## 2. Objetivo

Permitir que agentes de IA participem dos projetos como integrações da organização:
- com credenciais próprias, revogáveis e com expiração, sem usar a sessão de uma pessoa;
- com permissões limitadas por escopo e por projeto;
- sem operações duplicadas e sem sobrescrever o trabalho de outros;
- com cada operação registrada;
- sem aprovar nem concluir o próprio trabalho.

## 3. Resumo das implementações

| Etapa | Conteúdo |
|---|---|
| 1 | Políticas explícitas `User` e `Agent` em todos os endpoints, sem esquema padrão |
| 2 | `Agent`, `AgentCredential` e `AgentProjectAccess`; permissão `agents.manage` com migração de dados |
| 3 | Gestão por pessoas: agentes, credenciais (token só na criação, até 3 ativas), acesso a projetos, eventos de auditoria |
| 4 | Esquema `AgentToken`, `AgentCredentialDirectory`, organização do agente pela credencial, `GET /agents/me` |
| Revisão | C1 (política por marca `[AgentApi]`), C2 (isolamento no pipeline real), C3 (recusas indistinguíveis), C4 (`CHECK` de escopos) |
| 5 | `AgentAccessGrant.Compute` e as regras de agentes em `TransitionRules` (sem aprovação, sem Concluída, inclusive em workflows personalizados) |
| 6 | Ator nos serviços; autoria por pessoa ou agente (`CHECK`, gatilho imutável) e versão das tarefas |
| 7 | Rotas de leitura de agentes: projetos, contexto sem dados pessoais, tarefas |
| 8 | Idempotência transacional com trava consultiva e reautorização; rotas de escrita (tarefas, transições, atividades) |
| 9 | `WorkReport` imutável, com os cinco níveis de evidência e a aprovação humana calculada pelo histórico |
| 10 | `AgentOperation` (inclusive recusas), rate limiting por credencial no PostgreSQL, último uso, limite de falhas por IP, retenção de 180 dias, dois health checks |
| 11 | Telas Web de agentes, credenciais (token exibido uma única vez, `no-store`), acesso, operações e relatórios; autoria "Agente: nome" |
| 12 | Teste de ponta a ponta, teste de escrita em projeto arquivado, mutações de segurança, execução real completa e documentação de encerramento |

O detalhamento de cada etapa, com testes, mutações e execução real, está na seção "Execução e ajustes em relação ao plano" da especificação.

## 4. Arquivos criados

| Área | Arquivos |
|---|---|
| Domain | `Agents/{Agent,AgentCredential,AgentProjectAccess,AgentScopes,AgentTokens,AgentOperation,IdempotencyRecord,WorkReport}.cs` |
| Application | `Agents/{AgentContracts,AgentWorkContracts,AgentWriteContracts,AgentOperationContracts,WorkReportContracts,IAgentIdempotencyStore}.cs`, `Projects/{AgentAccessGrant,IAgentProjectAccess}.cs`, `Common/ActorReference.cs`, `Abstractions/Authentication/IAgentCredentialDirectory.cs` |
| Infrastructure | `Agents/*` (13 arquivos: serviços, diretório de credenciais, idempotência, rate limiting, auditoria, retenção, opções), `Persistence/Configurations/AgentConfigurations.cs`, `Persistence/ChangeTracking/TaskRevisionRules.cs` |
| Migrações | `AddAgents`, `AddAgentCredentialScopesCheck`, `AddTaskAuthorshipAndVersion`, `AddIdempotencyRecords`, `AddWorkReports`, `AddAgentOperationsAndRateLimits` |
| API | `Agents/{AgentApiAttribute,AgentIdempotentAttribute,AgentOperationContext,AgentOrganizationFilter,AgentPermissionAttributes,AgentProjectsController,AgentRequestMiddleware,AgentSelfController,AgentWritesController}.cs`, `Authentication/{AgentTokenAuthenticationHandler,ApiPolicies}.cs`, `Controllers/{AgentsController,WorkReportsController}.cs` |
| Web | `Api/{ApiAgentClients,ApiAgentModels}.cs`, `Components/Pages/Agents/*` (4), `Components/Pages/Reports/*` (4) |
| Testes | Unitários: `AgentAccessGrantTests`, `AgentOperationRulesTests`, `TenantFilterBypassTests`, `AgentRulesTests`, `AgentTransitionRulesTests`, `IdempotencyRecordTests`, `WorkReportRulesTests`. Integração: `Agents/*` (14 classes, o `AgentTestKit` e as sondas) e `Api/EndpointPolicyTests`. Componentes: `AgentPagesTests`, `WorkReportPagesTests` |
| Documentação | `docs/specifications/FASE-10-AGENTES.md`, `docs/decisions/ADR-016-AGENTES.md`, `docs/security/AGENTES.md`, este relatório |

## 5. Arquivos modificados

| Arquivo | Alteração |
|---|---|
| `API/Program.cs`, `API/Authentication/ApiAuthentication.cs` | Políticas explícitas, esquema `AgentToken`, middleware de agentes, opções validadas na inicialização |
| `Infrastructure/DependencyInjection.cs`, `API/appsettings.json` | Serviços, opções, health checks e o serviço de retenção |
| `Infrastructure/Tasks/TaskService.cs`, `Infrastructure/{Activity/ActivityService,Documents/DocumentService,Documents/DecisionService}.cs`, `API/Controllers/TasksController.cs`, `Application/Tasks/{ITaskService,TaskContracts}.cs` | Ator pessoa ou agente; autoria e versão no contrato; nomes de agentes nas leituras; códigos das recusas por regra |
| `Application/Abstractions/MultiTenancy/ITenantContext.cs`, `Infrastructure/MultiTenancy/TenantContext.cs`, `Application/Projects/ProjectAccess.cs`, `Infrastructure/Projects/ProjectAccessResolver.cs`, `API/Organizations/ProjectPermissionAttribute.cs`, `API/Activity/HttpActivityContext.cs` | Organização ativa de agentes; ator na concessão de acesso; autor agente na auditoria |
| `Domain/{Activity/ActivityActions,Organizations/OrganizationPermissions}.cs`, `Application/Abstractions/Authentication/AuthClaimTypes.cs`, `Infrastructure/Persistence/Configurations/TaskConfigurations.cs` | Ações de auditoria de agentes, `agents.manage`, claims do agente, autoria e versão das tarefas |
| `Domain/Workflows/TransitionRules.cs`, `Domain/Tasks/ProjectTask.cs` | Regras de agentes; autoria e versão |
| `Infrastructure/Persistence/{ProjectManagerDbContext,SaveChangesConflicts}.cs` | Entidades novas; 40001 embrulhado e 40P01 tratados como conflito (409) |
| `Application/Common/Result.cs`, `API/Common/ResultProblems.cs` | Código opcional da recusa (`ResultError.Code`) |
| Web: `Display.cs`, `Program.cs`, `Authentication/WebEndpoints.cs`, `Components/Shared/{ActivityList,OrgNav,ProjectFrame,Flash,Icon}`, `Pages/Tasks/TaskDetailsPage.razor`, `wwwroot/{app.css,js/app.js}` | Telas de agentes, `no-store`, autoria, relatórios, botão de copiar |
| `tests/*/Infrastructure/*`, `ActivityCoverageTests`, `HealthEndpointTests`, `WorkflowRulesTests`, `PagePermissionTests` | Fábrica com limites de teste, cobertura das rotas novas, health checks novos, regras de transição e versão na edição |
| `README.md`, `AGENTS.md`, `docs/architecture/ARCHITECTURE.md`, `docs/security/AUTENTICACAO.md`, `docs/web/TELAS.md`, `docs/PROJECT_STATUS.md`, ADR-006 | Documentação da fase |

## 6. Dependências adicionadas

Nenhuma. Tudo usa ASP.NET Core, EF Core, Npgsql e a biblioteca padrão do .NET 10, como previa a especificação.

## 7. Justificativas técnicas

- **Políticas explícitas por esquema (C1):** um seletor de esquema como padrão aceitaria um token de agente numa rota de pessoas por engano. A marca `[AgentApi]` e o prefixo são conferidos na inicialização.
- **Token opaco com hash (D2):** um JWT de agente seria irrevogável até expirar. O token opaco é verificado a cada requisição pelo hash, com índice único.
- **Organização só pela credencial (C3):** o agente não escolhe a organização; um cabeçalho divergente responde 404, como para quem não é membro.
- **Regra única de acesso e de transição:** `AgentAccessGrant.Compute` e `TransitionRules` são os únicos pontos de decisão, usados também nas transições disponíveis, para a tela e a API nunca divergirem.
- **Idempotência na mesma transação (C5 e C6):** reserva, operação e resposta são gravadas juntas, ou nenhuma; o replay reautoriza a credencial e o projeto antes de devolver a resposta guardada.
- **Rate limiting no PostgreSQL (A6):** um upsert atômico por requisição, correto com várias instâncias, sem Redis. A latência fica para a medição da Fase 13.
- **Auditoria fora da transação (A7):** a operação sobrevive ao rollback da requisição, e uma falha na auditoria não derruba a API.
- **Token na Web uma vez, sem post-redirect-get (C4):** guardar o token para exibi-lo depois de um redirecionamento exigiria sessão, cookie ou URL; a resposta do próprio formulário evita todos.

## 8. Testes executados

| Suíte | Novos na fase | Conteúdo principal |
|---|---|---|
| Unitários | 81 (178 → 259) | Escopo × papel × exigência; nunca `tasks.approve`; regras de transição de agentes e de pessoas; formato e prefixo do token; níveis do relatório; idempotência; rate limiting, retenção e saúde; bypass do filtro de organização |
| Integração | 90 (151 → 241) | Autenticação e recusas indistinguíveis; isolamento; gestão; persistência e restrições do banco; políticas; leitura; escrita; projeto arquivado; idempotência; autoria e versão; relatórios; operações, rate limiting e limite por IP; retenção; ponta a ponta |
| Componentes (bUnit) | 22 (43 → 65) | Telas de agentes, token uma vez, permissões, acessos, operações, relatórios e autoria |

O teste de ponta a ponta (`AgentEndToEndTests`) segue os oito passos da estratégia de testes com um token real e, no fim, procura o token em todas as tabelas do banco (como texto) e no log: não aparece. O hash aparece só em `agent_credentials`, o que confirma que a busca funciona.

## 9. Resultado do build

`dotnet restore`: exit 0. `dotnet build -c Release`: **0 avisos, 0 erros**, com analisadores `Recommended` e avisos tratados como erros. `dotnet ef migrations has-pending-model-changes`: nenhuma mudança pendente.

## 10. Resultado dos testes

| Suíte | Resultado |
|---|---|
| UnitTests | **259/259** |
| WebTests (bUnit) | **65/65** |
| IntegrationTests (PostgreSQL 18 e Mailpit via Testcontainers) | **241/241** |
| Total | **565/565** (antes da fase: 372/372) |

## 11. Evidências

- **Mutações de segurança da estratégia de testes** (todas desfeitas e conferidas):

  | Mutação | Resultado |
  |---|---|
  | Remover a checagem de revogação | Detectada por 5 testes, inclusive a ponta a ponta |
  | Remover a checagem de expiração | Detectada por 3 testes |
  | Incluir `tasks.approve` nas permissões elegíveis | **Não detectada na primeira rodada** (ver seção 12, item 8); detectada depois da correção do teste |
  | Conceder `tasks.approve` ao agente depois de todas as camadas | Detectada por 5 unitários e 3 de integração |
  | Remover a regra de Concluída para agentes | Detectada por 4 testes, inclusive a ponta a ponta |
  | Aceitar a organização do cabeçalho | Detectada por 3 testes |
  | Devolver o replay sem reautorizar | Detectada por 1 teste |
  | Pular o hash do corpo | Detectada por 1 teste |
  | Remover o `CHECK` de autoria | **Não detectada na primeira rodada** (ver seção 12, item 9); detectada depois da correção do teste |

- **Mutações das etapas:** cada etapa registrou as próprias na especificação (por exemplo, 7 de 7 na etapa 8, 11 de 11 na etapa 10 e 10 de 10 na etapa 11). As não detectadas foram analisadas e registradas como equivalentes: a cláusula de nulo do `CHECK` de escopos (o `<@` já recusa) e aceitar agentes na aprovação humana (agentes nunca chegam a Concluída).
- **Execução real da etapa 12** (API local, banco de desenvolvimento com as migrações aplicadas, fluxo de ponta a ponta por HTTP):

  | Passo | Resultado |
  |---|---|
  | 1. Agente, credencial (`no-store`) e concessão como Colaborador | 201, 201, 200 |
  | 2. `me` e contexto do projeto | 200, 200 |
  | 3. Tarefa com `Idempotency-Key`, replay, `PATCH` pela versão e `PATCH` desatualizado | 201; replay 201 com `Idempotency-Replayed`, uma só tarefa no projeto; 200; 409 |
  | 4. Até a revisão; Concluída por um atalho sem aprovação | 200, 200; atalho 403; Em revisão 200 |
  | 5. Atividade e relatório | 201 e 201; níveis `declared`, `accepted`, `notVerified` (testes e CI) e `pending`; tarefa em Em revisão |
  | 6. Concluída com aprovação | 403; estado e versão inalterados |
  | 7. Aprovação por uma pessoa | 200; aprovação humana `approved` |
  | 8. Revogação e chamada seguinte | 200 e 401 |
  | Operações do agente | 15, com `agent_cannot_enter_done`, `agent_cannot_approve` e `credential_revoked`, e um replay |
  | Busca no banco (`pg_dump` inteiro do banco de desenvolvimento, 285 KB, lido em memória) | Token ausente; só o hash e o prefixo presentes |
  | Leituras posteriores (operações, agente, relatório) | Token ausente |
  | Logs da API e da Web | Nenhum `gsk_` e nenhum erro |
  | `/health` | `database`, `agents-audit`, `agents-retention` e `self` saudáveis |

- **Execução real da etapa 11** (Web e API locais, formulários reais com antiforgery): token exibido uma única vez, sem redirecionamento e com `no-store`; o token exibido funcionou na API; ausente ao reabrir a tela; revogação pela Web; operações e filtros; relatórios com os níveis; pessoa sem `agents.manage` sem acesso; nenhum token e nenhum erro nos logs.

## 12. Problemas encontrados

1. **Corrida na criação de credenciais (etapa 3, revisão):** a transação `Serializable` fazia agentes diferentes, até de organizações diferentes, receberem 409.
2. **Falha de serialização embrulhada (etapa 3):** o Npgsql embrulha o 40001 numa "falha transitória", que virava 500.
3. **Deadlock entre edições simultâneas da mesma tarefa (etapa 6, código das Fases 06 e 08):** o `PATCH` marcava a tarefa inteira como modificada, e o 40P01 virava 500.
4. **Hash da idempotência sem o caminho (etapa 8):** a mesma chave com o mesmo corpo em outra tarefa repetia a resposta da primeira.
5. **Teste fraco da aprovação humana (etapa 9):** não detectava a retirada da condição de Concluída.
6. **Alias do SQL bruto do rate limiting (etapa 10):** as colunas do resultado precisam estar em snake_case; um alias em PascalCase causava 500.
7. **Prefixo na Web sem o marcador (etapa 11):** a tela mostrava `Q2GnYhfx…`, diferente do começo do token que a pessoa guardou. Os dados dos testes de componente usavam um formato que a API nunca devolve, por isso os testes não pegaram; a execução real pegou.
8. **Mutação de `tasks.approve` não detectada (etapa 12):** `PermissionsFor` intersecta três camadas (papel, permissões elegíveis e escopos). Nenhum escopo aponta para `tasks.approve`, então incluí-la só nas elegíveis não chegava ao agente, e nenhum teste conferia essa camada sozinha.
9. **Mutação do `CHECK` de autoria não detectada (etapa 12):** o teste só fazia `UPDATE`, e o gatilho de autoria imutável (`BEFORE UPDATE`) recusa antes com o mesmo código (`check_violation`). O `CHECK` nunca era exercitado.

## 13. Problemas corrigidos

1. O limite de 3 ativas passou a usar a trava otimista da linha do agente (`xmin`), com teste de 8 criações simultâneas em duas organizações.
2. e 3. `SaveChangesConflicts` reconhece o 40001 embrulhado e o 40P01 como conflito (409); o `PATCH` marca só a versão. Nenhum deadlock em 5 execuções seguidas.
4. O hash cobre método, caminho, query e corpo; teste da mesma chave em outra tarefa (422).
5. O teste ganhou uma mudança não Concluída feita por uma pessoa e passou a detectar a mutação.
6. Alias em snake_case (`database_now`).
7. A Web mostra `gsk_` mais o prefixo, e os dados dos testes usam o formato real.
8. `ManagementPermissions_NeverReachAnAgent_ForAnyRoleOrScopeSet` passou a conferir cada camada: nenhuma permissão de gestão nas elegíveis nem na tabela de escopos.
9. `Database_RequiresExactlyOneAuthor_...` ganhou duas inserções (dois autores e nenhum autor), que só o `CHECK` recusa.

## 14. Pendências

- **Integração:** push da branch, PR para `develop` e CI, que dependem de autorização do responsável. Só depois disso a fase fica `Completed`.
- **Fase 12:** verificação de testes e CI dos relatórios (`work_report_verifications`) e o servidor MCP.
- **Fase 13:** latência adicional por requisição de agente e testes de carga do rate limiting.
- **Fase 14:** forwarded headers para o limite por IP atrás de proxy; limite por IP compartilhado entre instâncias, se houver mais de uma.
- **Tempo real (D14):** continua fora, sem necessidade comprovada.

## 15. Critérios atendidos

| Critério | Evidência |
|---|---|
| Agentes autenticam independentemente de usuários | `Token_AuthenticatesTheAgentWithoutAnyUserSession`, `EachScheme_WorksOnlyOnItsOwnRoutes`, `EndpointPolicyTests` |
| Credenciais podem ser revogadas | `RevocationAndDisabling_TakeEffectOnTheNextRequest_...`, `ExpiredCredential_IsRejected`, `RejectedCredentials_AreRecorded_...`, limite de 3 com concorrência; mutações de revogação e expiração |
| Token inválido não acessa recursos | `EveryRejection_IsIndistinguishable_...`, `MalformedTokens_AreRejected`, `TamperedToken_IsRejected` |
| Escopos são respeitados | `EveryReadRoute_IsNotFound_..._AndForbidden_WithoutItsScope`, `Writes_AreRefusedByVisibilityRoleAndScope`, `AgentAccessGrantTests`; mutação de `tasks.approve` |
| Acesso limitado aos projetos autorizados | Os mesmos, mais `OrganizationInRouteOrHeader_MustMatchTheCredential` e `ArchivedProject_RefusesAgentWrites_WithConflict` |
| Isolamento entre organizações | `AgentIsolationTests`, `AgentsAndProjects_OfAnotherOrganization_AreNotFound`, `Reading_IsLimitedToTheProjectAndTheOrganization`, `OperationsRoute_FiltersAndIsolates`, retenção em duas organizações |
| Agentes não aprovam nem concluem | `Transitions_UseTheSameRules_...`, `CustomWorkflow_OffersAgentsNeitherApprovalNorDone_...`, ponta a ponta (recusas em `AgentOperation`); mutação da regra de Concluída |
| Autoria íntegra | `Database_RequiresExactlyOneAuthor_...`, `Migration_PreservesExistingTasks_...`; mutação do `CHECK` |
| Concorrência | `Patch_RequiresTheExpectedVersion_...`, `ConcurrentEdits_WithTheSameVersion_ExactlyOneWins`, `Version_GrowsOnEveryTaskChange_...` |
| Operações registradas | `EveryRequest_IsRecorded_...`, `TheOperation_SurvivesTheRollbackOfTheRequest`, `ActivityNames_IncludeAgents`, ponta a ponta |
| Idempotência funcional | `AgentIdempotencyTests` (8); mutações do replay sem reautorizar e do hash do corpo |
| Relatórios íntegros | `Reports_AreImmutableInTheDatabase`, `Submit_SeparatesTheLevels_AndLeavesTheTaskUnchanged`, telas com os níveis |
| Limite de requisições | `RateLimit_Returns...`, `RateLimit_IsAtomicUnderConcurrency`; execução real da etapa 10 |
| Retenção | `AgentRetentionTests` (4) |
| Token na Web | `CreatingACredential_ShowsTheTokenOnce_...`, `NoStoreHeader_...`; execuções reais das etapas 11 e 12 (logs e dump do banco) |
| Abuso de autenticação | `UnknownTokens_AreLimitedPerAddress_BeforeAnyLookup`, `RejectedCredentials_AreRecorded_...` |
| Último uso da credencial | `LastUse_IsWrittenOnlyByTheFirstRequestOfTheWindow`; visível na tela do agente |
| Ponta a ponta | `AgentEndToEndTests` |

## 16. Critérios não atendidos

- **"Todos os critérios verificados local e no CI" e "integradas por PR, com o check aprovado"** (definição de conclusão): a verificação local está completa; o CI e o PR dependem de autorização para o push.
- Nenhum critério de aceitação funcional ficou sem verificação.

## 17. Riscos identificados

| Risco | Situação |
|---|---|
| Vazamento de credencial | Mitigado: hash, expiração, revogação imediata, escopos mínimos, rate limiting, auditoria; o token não aparece no banco nem nos logs (teste e execução real) |
| Latência do pipeline de agentes | Aberto: busca do hash, upsert do contador e gravação da operação por requisição; medição na Fase 13 |
| Limite por IP por instância | Aberto: em memória; com várias instâncias, o limite efetivo se multiplica. Fase 14 |
| Tempo de resposta da recusa de token malformado | Aceito: é mais rápido (sem consulta), mas o formato é público e não ajuda a enumerar credenciais |
| Volume de `agent_operations` | Mitigado: linha pequena, índices por agente e data, retenção de 180 dias |
| Tamanho da fase | Mitigado: 12 etapas com verificação própria e revisão independente das etapas 1 a 4 |

## 18. Estado do Git

- **Branch:** `feature/fase-10-agentes`, com 21 commits locais sobre `develop` `d6c34fc`: a documentação aprovada, uma etapa por commit (1 a 12), as correções da revisão independente em commits próprios e este encerramento. O último commit é o da etapa 12.
- **Remoto:** nada enviado. A branch não existe no GitHub; não há PR.
- **`main` e `develop`:** inalteradas (`c6bd32a` e `d6c34fc`).
- **Working tree:** limpa depois do commit da etapa 12.
- **Banco de desenvolvimento local:** as seis migrações da fase aplicadas (não é um ambiente compartilhado).

## 19. Próxima fase recomendada

Fase 11 (CLI `gestalyn`), depois da integração desta fase em `develop`. A CLI usa a API por HTTP e pode reaproveitar as rotas de agentes para o modo de automação, com a credencial guardada no cofre do sistema operacional. Antes, recomenda-se revisar o PR da Fase 10 com atenção às exceções de isolamento do ADR-016 e à exibição do token na Web.
