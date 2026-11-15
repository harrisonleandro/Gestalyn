# Relatório final: Fase 07, Workflow configurável e histórico

- **Data:** 2026-11-15
- **Ambiente:** Windows 11 Pro · .NET SDK 10.0.404 · Docker 29.8.2 · PostgreSQL 18 · Mailpit v1.29
- **Branch:** `feature/fase-07-workflow` (a partir de `develop` `308f910`, com as Fases 01 a 06 e as correções da auditoria integradas)
- **Estado:** **Completed.** Critérios verificados localmente e no CI; integrada em `develop` pelo [PR #13](https://github.com/harrisonleandro/Gestalyn/pull/13) (merge `a6f87f9`).
- **Formato:** seção 17 da [especificação mestre](../specifications/ESPECIFICACAO-MESTRE.md)

## 1. Nome e número da fase

Fase 07: Workflow configurável e histórico. A especificação da fase está em [FASE-07-WORKFLOW.md](../specifications/FASE-07-WORKFLOW.md), e as decisões, no [ADR-013](../decisions/ADR-013-WORKFLOW-CONFIGURAVEL.md).

## 2. Objetivo

Dar às tarefas estados configuráveis por projeto, sem enum rígido: categorias e estados finais, transições explícitas validadas no backend, restrições por permissão, regras de aprovação (agentes nunca aprovam o próprio trabalho por padrão), uso das dependências na conclusão e histórico de cada mudança de estado.

## 3. Resumo das implementações

- **Modelo (D1):**
  - um `WorkflowDefinition` por projeto, criado junto com ele;
  - `WorkflowState` (nome, categoria, posição, inicial) e `WorkflowTransition` (origem, destino, exige aprovação);
  - a tarefa ganhou `state_id` obrigatório, com FK composta `(project_id, state_id)`.
- **Categorias (D2):** `Todo`, `InProgress`, `Blocked`, `Done` e `Canceled`; as duas últimas são finais. Tarefa em estado final é somente leitura, exceto comentários e transições.
- **Configuração inicial e migração (D3):** Backlog (inicial), A fazer, Em andamento, Em revisão, Bloqueada e Concluída, com 14 transições. A migration `AddWorkflows` cria essa configuração em todos os projetos existentes e coloca as tarefas no estado inicial, com histórico.
- **Transições (D4):** `POST /tasks/{id}/transitions`, com `expectedStateId` e comentário opcionais. `TransitionRules.Evaluate` é a regra única: transição existente, aprovação, próprio trabalho e dependências. Transições simultâneas são resolvidas pelo `xmin` da tarefa.
- **Aprovação (D5):** `tasks.approve` (Gerente). Quem é responsável pela tarefa não aprova o próprio trabalho: agentes sempre; pessoas quando o workflow liga `requireIndependentApproval`.
- **Configuração (D6):** `workflow.manage` (Gerente), com rotas por projeto e por ID de estado ou transição (`[WorkflowStatePermission]`, `[WorkflowTransitionPermission]`). As invariantes ficam no domínio e respondem 409.
- **Histórico (D7):** `task_status_history`, com os nomes dos estados na data, autor, tipo de autor, comentário e data; `GET /tasks/{id}/history`.
- **Tarefas:** detalhes com o estado e as transições disponíveis para o usuário; listagem com o estado, filtros `stateId`, `category` e `open` e ordenação `State`; `overdue` desconsidera estados finais; anexos bloqueados em estado final.
- **Documentação:** ADR-013, `docs/api/WORKFLOW.md`, `docs/api/TASKS.md`, ARCHITECTURE (§3.6 com diagrama ER), PROJECT_STRUCTURE, README, AGENTS, especificação e status.

## 4. Arquivos criados

| Área | Arquivos |
|---|---|
| Domain | `Workflows/{StateCategory,WorkflowState,WorkflowTransition,WorkflowDefinition,TransitionRules,TaskStatusHistory}.cs` |
| Application | `Workflows/{IWorkflowService,WorkflowContracts}.cs` |
| Infrastructure | `Workflows/WorkflowService.cs`, `Tasks/TaskStates.cs`, `Persistence/Configurations/WorkflowConfigurations.cs`, migration `20261115012750_AddWorkflows` |
| API | `Controllers/WorkflowController.cs` |
| Testes | `UnitTests/Domain/WorkflowRulesTests.cs`; `IntegrationTests/Workflows/{WorkflowMigrationTests,WorkflowConfigurationTests,TaskTransitionTests,WorkflowIsolationTests}.cs` |
| Documentação | `docs/specifications/FASE-07-WORKFLOW.md`, `docs/decisions/ADR-013-WORKFLOW-CONFIGURAVEL.md`, `docs/api/WORKFLOW.md`, este relatório |

## 5. Arquivos modificados

| Arquivo | Alteração |
|---|---|
| `Domain/Tasks/ProjectTask.cs` | `StateId` e `EnterState`, que só aceita estados do mesmo projeto |
| `Domain/Projects/ProjectPermissions.cs` | `tasks.approve` e `workflow.manage` |
| `Application/Tasks/{ITaskService,TaskContracts}.cs` | Transição, histórico, estado nos contratos, filtros e ordenação por estado |
| `Application/Projects/ProjectAccess.cs` | `ProjectAccessGrant.UserId`; acesso pelo estado e pela transição |
| `Infrastructure/Tasks/{TaskService,AttachmentService}.cs` | Estado inicial na criação, transições, histórico, somente leitura em estado final e dependências abertas |
| `Infrastructure/Projects/{ProjectService,ProjectAccessResolver}.cs` | Workflow inicial em projetos novos; acesso pelo projeto do estado ou da transição |
| `Infrastructure/Persistence/{ProjectManagerDbContext.cs,Configurations/TaskConfigurations.cs,Migrations/ProjectManagerDbContextModelSnapshot.cs}`, `DependencyInjection.cs` | Novas tabelas, FK do estado da tarefa e registro do serviço |
| `API/Controllers/TasksController.cs` | `POST /tasks/{id}/transitions` e `GET /tasks/{id}/history` |
| `API/Organizations/ProjectPermissionAttribute.cs` | Atributos de estado e de transição; verificação reutilizável para recursos identificados pelo corpo |
| `README.md`, `AGENTS.md`, `docs/architecture/*`, `docs/api/TASKS.md`, `docs/specifications/README.md`, `docs/PROJECT_STATUS.md` | Documentação da fase |

## 6. Dependências adicionadas

Nenhuma.

## 7. Justificativas técnicas

- **Workflow por projeto (D1):** a especificação mestre pede que cada projeto configure os próprios estados. As FKs compostas com `project_id` impedem, no banco, que uma tarefa ou transição use um estado de outro projeto.
- **Categorias fixas (D2):** dão a dashboards e filtros um significado estável, com nomes livres. Um estado final é definido pela categoria, então não existe "Em andamento" final.
- **Migration com dados (D3):** é a única forma de dar estado às tarefas existentes. O SQL roda uma vez, dentro da migration, e um teste compara o resultado com `CreateDefault` (exigência do AGENTS.md para SQL bruto sobre dados de organizações).
- **Endpoint próprio de transição (D4):** separa a edição de dados de uma operação com regras, aprovação e histórico próprios. `expectedStateId` evita mover uma tarefa que outra pessoa acabou de mudar.
- **Regra única em `TransitionRules`:** a mesma função decide a transição e calcula as transições disponíveis nos detalhes, então a interface nunca oferece o que a API recusaria.
- **Trava do workflow:** toda edição marca a linha do workflow como alterada, e o `xmin` serializa edições simultâneas. Isso protege as invariantes que não cabem em índices, como o estado inicial único.
- **Histórico com nomes (D7):** sem FK para os estados, o histórico continua legível depois de renomear ou excluir um estado.

## 8. Testes executados

| Suíte | Novos nesta fase | Conteúdo |
|---|---|---|
| UnitTests (`WorkflowRulesTests`) | 10 métodos, 21 casos | Configuração inicial, estados finais, nomes únicos, estado inicial, transições (laço, duplicata, estado de outro projeto), exclusão e renumeração, estado da tarefa no mesmo projeto, aprovação com pessoas e agentes (responsáveis ou não, com e sem aprovação independente) e transições inexistentes ou com dependências abertas |
| IntegrationTests (`WorkflowMigrationTests`) | 1 | Banco migrado até a Fase 06 com projetos e tarefas, depois até o fim: workflows iguais a `CreateDefault`, tarefas no estado inicial e histórico |
| IntegrationTests (`WorkflowConfigurationTests`) | 4 | Workflow inicial em projeto novo; configuração completa com as invariantes; projetos com workflows independentes; permissões e 404 entre organizações |
| IntegrationTests (`TaskTransitionTests`) | 8 | Fluxo completo com aprovação; transições inválidas sem efeito; papéis e `tasks.approve`; aprovação independente; dependências; corrida de duas transições; histórico depois de renomear e excluir estados; filtros e ordenação por estado |
| IntegrationTests (`WorkflowIsolationTests`) | 2 | Filtro de leitura das quatro tabelas novas e FKs compostas recusando vínculos entre projetos |

## 9. Resultado do build

`dotnet restore`: exit 0. `dotnet build -c Release`: **0 avisos, 0 erros**, com analisadores `Recommended` e avisos tratados como erros.

## 10. Resultado dos testes

| Suíte | Resultado |
|---|---|
| UnitTests | **148/148** |
| IntegrationTests (PostgreSQL 18 e Mailpit via Testcontainers) | **132/132** |
| Total | **280/280** |

`SimultaneousTransitions_LetOnlyOneWin` passou em 5 execuções seguidas.

## 11. Evidências

- **Mutações** (todas desfeitas e conferidas):
  1. transição procurada só pelo destino: `InvalidTransitions_AreRejected_WithoutChangingTheTaskOrTheHistory` falha;
  2. dependências abertas ignoradas: `OpenDependencies_BlockCompletion_UntilTheyReachAFinalState` falha;
  3. tarefa em estado final editável: o teste do fluxo completo falha;
  4. aprovação independente ignorada para pessoas: `IndependentApproval_StopsAssigneesFromApprovingTheirOwnWork` falha.
- **Migração no banco de desenvolvimento:** 7 projetos receberam 7 workflows, 42 estados e 98 transições; as 5 tarefas ficaram com estado e 5 entradas de histórico.
- **Execução real** (API local, dois usuários reais, Gerente e Colaboradora):

  | Verificação | Resultado |
  |---|---|
  | Workflow inicial | 6 estados, Backlog inicial, Concluída final, 14 transições |
  | Gerente cria o estado Cancelada | 201, final |
  | Colaboradora cria estado | 403 |
  | Tarefa nova | Em Backlog, com as transições disponíveis |
  | Pular de Backlog para Concluída | 409 |
  | Colaboradora: A fazer, Em andamento, Em revisão | 200, 200, 200 |
  | Colaboradora aprova | 403 |
  | Gerente aprova com dependência aberta | 409 |
  | Gerente aprova depois de concluir a dependência | 200, estado final |
  | Editar a tarefa concluída / comentar | 409 / 201 |
  | Histórico | 5 entradas, com os nomes dos estados e de quem mudou |
  | Filtros `open=true` e `category=Done` | 0 e 2 |

## 12. Problemas encontrados

1. A migration gerada criava `state_id` obrigatório com o GUID vazio, o que falharia com tarefas existentes.
2. O índice único parcial para o estado inicial era violado ao trocar o estado inicial, porque a troca desmarca um e marca outro na mesma gravação.
3. Excluir um estado respondia 500: retirá-lo da coleção do workflow deixava órfãos numa relação obrigatória.
4. Depois de excluir um estado, as posições ficavam com um buraco.
5. Uma das mutações planejadas não compilava (CS1718, comparação consigo mesmo).
6. O Docker Desktop estava parado no início de algumas execuções dos testes de integração.

## 13. Problemas corrigidos

1. Migration ajustada à mão: coluna opcional, dados, depois obrigatória e FK.
2. Índice removido; a regra ficou no domínio, protegida pela trava do workflow (`xmin`). A migration foi gerada de novo (`dotnet ef migrations remove --force`), com a migração de dados reaplicada.
3. O serviço exclui explicitamente as transições e o estado.
4. `RemoveState` renumera as posições.
5. A mutação foi refeita de outra forma e detectada.
6. Docker iniciado antes dos testes.

## 14. Pendências

- Teste de integração "agentes não aprovam o próprio trabalho" com agentes reais: Fase 10, quando houver credenciais de agente. A regra já está implementada e coberta por teste unitário.
- Validar se o workflow tem caminho até um estado final: tela de configuração, Fase 09.
- Auditoria completa das alterações e dashboards por categoria: Fase 08.
- Quadro e configuração do workflow na Web (Fase 09) e comandos `gestalyn task move` (Fase 11).

## 15. Critérios atendidos

| Critério | Evidência |
|---|---|
| Projetos podem ter fluxos diferentes | `Projects_HaveIndependentWorkflows`; `CompositeForeignKeys_KeepStatesAndTransitionsInsideTheirProject`; `Task_EntersOnlyStatesOfItsProject` |
| Transições inválidas são rejeitadas | `InvalidTransitions_AreRejected_WithoutChangingTheTaskOrTheHistory`, `OpenDependencies_BlockCompletion_UntilTheyReachAFinalState`, mutações 1 e 2 e execução real |
| Alterações são registradas | `Task_GoesFromBacklogToDone_WithApproval_AndTheHistoryRecordsEveryStep`, `History_StaysReadable_AfterStatesAreRenamedOrDeleted`, `WorkflowMigrationTests` e execução real |
| Estados finais são respeitados | Fluxo completo (edição recusada, comentário aceito, reabertura), mutação 3 e execução real |
| Permissões são verificadas | `ProjectRoles_AndTheApprovalPermission_AreEnforced`, `OnlyWorkflowManagers_ChangeTheWorkflow_AndOtherOrganizationsSeeNothing`, `ReadFilter_HidesWorkflowsStatesTransitionsAndHistoryOfOtherOrganizations` |
| Agentes não aprovam o próprio trabalho por padrão | Teste unitário `ApprovalTransitions_RequireThePermission_AndAgentsNeverApproveTheirOwnWorkByDefault`; teste de integração com pessoas e `requireIndependentApproval` (`IndependentApproval_StopsAssigneesFromApprovingTheirOwnWork`) e mutação 4, como previsto na especificação |

## 16. Critérios não atendidos

Nenhum. A definição de conclusão também foi cumprida: CI aprovado ([run 43666361156](https://github.com/harrisonleandro/Gestalyn/actions/runs/43666361156): 0 avisos, 148/148 e 132/132) e integração por PR ([PR #13](https://github.com/harrisonleandro/Gestalyn/pull/13), merge `a6f87f9`).

## 17. Riscos identificados

| Risco | Impacto | Mitigação |
|---|---|---|
| Workflow sem caminho até um estado final | Tarefas que nunca podem ser concluídas | Permitido por ora; validação na tela de configuração (Fase 09) |
| Edições simultâneas do workflow | Uma delas recebe 409 | Trava do workflow (`xmin`); a interface recarrega e tenta de novo |
| Excluir um estado deixa o histórico apontando para um ID inexistente | Nenhum na leitura | O histórico guarda os nomes na data e não tem FK para os estados |
| Migração de dados em bancos grandes | Migration demorada | SQL em lote, sem laço por linha; medir na Fase 13 |
| Regra do próprio trabalho para agentes sem teste de integração | Regressão não percebida até a Fase 10 | Teste unitário e critério obrigatório na Fase 10 |

## 18. Estado do Git

- **Branch:** `feature/fase-07-workflow`, a partir de `develop` `308f910`, enviada e incorporada pelo [PR #13](https://github.com/harrisonleandro/Gestalyn/pull/13).
- **Commits:**
  - `4099945` (especificação);
  - `6d16a42` (modelo e migração dos dados existentes);
  - `6858a4c` (configuração do workflow);
  - `93e33e1` (transições, aprovação, histórico e estado final);
  - `f3d000a` (documentação de encerramento).
- **PR #13 (Fase 07):** CI aprovado sobre `f3d000a`; merge `a6f87f9`.
- **`develop`:** `a6f87f9`.

## 19. Próxima fase recomendada

1. **Fase 08, Auditoria, documentação e dashboards:**
   - auditoria das alterações de projetos, tarefas e workflow, complementando o histórico de estados;
   - progresso por projeto a partir das categorias de estado;
   - dashboards com tarefas abertas, atrasadas e concluídas.
