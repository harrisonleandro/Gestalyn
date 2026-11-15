# Fase 07: Workflow configurável e histórico

- **Estado:** Completed. Validada localmente (testes, mutação e execução real da API) e no CI ([run 43666361156](https://github.com/harrisonleandro/Gestalyn/actions/runs/43666361156)), e integrada em `develop` pelo [PR #13](https://github.com/harrisonleandro/Gestalyn/pull/13) (merge `a6f87f9`). Decisões D1 a D7 aprovadas pelo responsável em 2026-11-14.
- **Referência:** [Especificação mestre](ESPECIFICACAO-MESTRE.md), seção 9 (Fase 07), seção 4.5 (fluxos de trabalho configuráveis) e seção 5.5 (contratos)
- **Branch:** `feature/fase-07-workflow` (a partir de `develop`, com as Fases 01 a 06 e as correções da auditoria integradas)
- **Pré-requisito para implementar:** decisões aprovadas

## Objetivo

Dar estado às tarefas por meio de um workflow configurável por projeto: estados com categoria, estados finais, transições validadas no backend e restritas por permissão, regras de aprovação e histórico de mudanças de estado. A especificação mestre proíbe estados em enum rígido: cada projeto define os próprios estados, a partir de uma configuração inicial.

## Escopo

1. **Workflow por projeto** (`WorkflowDefinition`), criado com o projeto a partir da configuração inicial. Os projetos que já existem recebem o workflow por migração (D3).
2. **Estados** (`WorkflowState`): nome, categoria, posição, estado inicial. Os estados finais vêm da categoria (D2).
3. **Transições** (`WorkflowTransition`): de um estado para outro, opcionalmente exigindo aprovação (D4 e D5).
4. **Mudança de estado da tarefa** por um endpoint próprio, com todas as validações no backend (D4).
5. **Regras de aprovação**, incluindo "quem é responsável pela tarefa não aprova o próprio trabalho", ativa por padrão para agentes e configurável para pessoas (D5).
6. **Histórico de estados** (`TaskStatusHistory`) e `GET /tasks/{id}/history` (D7).
7. **Proteção contra alterações inválidas:**
   - estado final é somente leitura;
   - dependências abertas impedem a conclusão;
   - não se exclui estado em uso;
   - concorrência entre mudanças simultâneas.
8. **Permissões novas** no catálogo de projeto (D6).
9. **Testes:** configuração, transições válidas e inválidas, permissões, aprovação, dependências, histórico, migração e isolamento cross-tenant.
10. **Documentação:** ADR-013, `docs/api/WORKFLOW.md` e atualização de ARCHITECTURE, README e AGENTS.md.

## Fora de escopo

- **Agentes:** credenciais, transições feitas por agentes e "sugerir transições" (Fase 10). O modelo já registra o tipo de quem fez a mudança (pessoa ou agente) e a regra de autoaprovação já distingue agentes, mas só pessoas existem nesta fase (D5).
- **Fluxo de pedido de aprovação** (alguém pede, outra pessoa aprova depois): nesta fase, a aprovação é a própria transição, restrita (D5). O pedido assíncrono pode entrar junto com os agentes.
- **Modelos de workflow compartilhados pela organização:** cada projeto tem o seu.
- **Histórico de todas as alterações da tarefa** (título, responsáveis etc.): Fase 08, com a auditoria.
- **Quadro de tarefas e tela de configuração do workflow:** Fase 09.
- **Progresso e dashboards por categoria:** Fase 08.

## Decisões (D1 a D7 aprovadas)

| ID | Proposta | Alternativas |
|---|---|---|
| **D1** Modelo | **Um workflow por projeto**, criado junto com ele. Estados e transições pertencem ao workflow e carregam `organization_id` e `project_id`. **FKs compostas** garantem no banco que a tarefa só aponta para um estado do próprio projeto, e que a transição liga dois estados do mesmo projeto. A tarefa passa a ter um `state_id` obrigatório. | (a) Workflow da organização, compartilhado entre projetos: contraria "cada projeto poderá configurar seus próprios estados". (b) Vários workflows por projeto, por tipo de tarefa: sem critério da fase que o peça. |
| **D2** Categorias e estados finais | Os **estados** são livres; a **categoria** de cada um vem de uma lista fixa, porque é ela que permite calcular progresso e regras sem conhecer os nomes: **A fazer** (`Todo`), **Em andamento** (`InProgress`), **Bloqueada** (`Blocked`), **Concluída** (`Done`) e **Cancelada** (`Canceled`). **São finais os estados das categorias Concluída e Cancelada.** A tarefa num estado final fica **somente leitura** (dados, responsáveis, etiquetas, dependências e anexos respondem 409), mas ainda aceita comentários e as transições que saem desse estado, como "reabrir". | (a) Marcação "final" independente da categoria: permitiria um estado "Em andamento" final, o que tiraria o sentido das categorias. (b) Categorias livres: impossibilita progresso e dashboards na Fase 08 sem mapear nomes. |
| **D3** Configuração inicial e migração | Todo projeto novo recebe a configuração inicial da especificação: **Backlog** (A fazer, **inicial**), **A fazer** (A fazer), **Em andamento** (Em andamento), **Em revisão** (Em andamento), **Bloqueada** (Bloqueada) e **Concluída** (Concluída). As transições iniciais estão listadas abaixo. **A migration cria o mesmo workflow para os projetos que já existem e coloca todas as tarefas existentes no estado inicial (Backlog)**, registrando essa entrada no histórico. É SQL sobre dados de organizações, por isso fica justificado no ADR-013. | Deixar as tarefas existentes sem estado até alguém configurar: quebraria a regra de que toda tarefa tem estado e exigiria tratar o "sem estado" em todo lugar. |
| **D4** Transições e mudança de estado | Só há **transições explícitas** (de A para B), sem "de qualquer estado". Mudar o estado é feito por **`POST /tasks/{id}/transitions`** (`{ toStateId, expectedStateId?, comment? }`), nunca pelo `PATCH`. O backend valida, nesta ordem: <br>1. a permissão `tasks.write`; <br>2. se a transição existe; <br>3. a regra de aprovação (D5); <br>4. **as dependências: entrar num estado Concluída exige que todas as tarefas das quais esta depende estejam num estado final**; <br>5. a concorrência: o `expectedStateId` opcional mais o `xmin`, para que duas pessoas não movam a mesma tarefa ao mesmo tempo sem perceber. <br>Transição inexistente ou dependências abertas respondem 409; falta de permissão, 403. | (a) Mudar o estado pelo `PATCH /tasks/{id}`: mistura a edição de dados com uma operação que tem regras e histórico próprios. (b) Transição "de qualquer estado": mais curta de configurar, mas enfraquece "transições inválidas são rejeitadas". |
| **D5** Aprovação | Uma transição pode ser marcada **"exige aprovação"**: só a executa quem tem a permissão nova **`tasks.approve`** (Gerente). Além disso, **quem é responsável pela tarefa não aprova o próprio trabalho**: <br>- para **agentes**, a regra vale sempre, por padrão (critério da especificação), e entra em vigor quando os agentes existirem (Fase 10); <br>- para **pessoas**, ela é uma configuração do workflow, `requireIndependentApproval`, desligada por padrão para não travar equipes de uma pessoa só. <br>Na configuração inicial, exigem aprovação **Em revisão → Concluída** e **Concluída → Em andamento** (reabrir). | (a) Pedido de aprovação assíncrono (pedir e aprovar depois): exige fila, notificações e telas, e entra melhor com os agentes. (b) Regra independente sempre ligada também para pessoas: impediria um Gerente sozinho de concluir as próprias tarefas. |
| **D6** Configuração e permissões | **Permissão nova `workflow.manage`** (Gerente) para alterar estados, transições e configurações. Ler o workflow exige `project.read`. **Endpoints da especificação**, com os ajustes abaixo: <br>- `PUT /projects/{id}/workflow` altera as **configurações do workflow** (estado inicial e aprovação independente), e não substitui o workflow inteiro, porque isso exigiria mapear tarefas de estados removidos; <br>- `POST /workflow/transitions` recebe os dois estados no corpo, e o projeto é deduzido deles; <br>- novos: **`DELETE /workflow/states/{id}`** (só sem tarefas e se não for o inicial) e **`POST /workflow/states/{id}/move`** (reordenar). <br>**Invariantes:** exatamente um estado inicial, que não pode ser final; nomes únicos no workflow (sem diferenciar maiúsculas e minúsculas); sem transição de um estado para ele mesmo nem duplicada; até 30 estados e 200 transições. Mudar a categoria de um estado em uso para final ou não final é permitido: as tarefas nele passam a seguir a nova categoria. | (a) Reaproveitar `project.plan` (fases e marcos): mudar o workflow afeta a regra de conclusão e de aprovação de todas as tarefas, merece uma permissão própria. (b) `PUT` substituindo o workflow inteiro: exigiria um mapa de estados antigos para novos a cada chamada. |
| **D7** Histórico | `task_status_history` registra **toda mudança de estado**, inclusive a entrada no estado inicial ao criar a tarefa: estado de origem, estado de destino, o **nome dos dois na data**, quem fez, se foi **pessoa ou agente**, quando e o comentário opcional. Os registros nunca são alterados nem apagados pela API. **`GET /tasks/{id}/history`** lista o histórico em ordem cronológica. O histórico de todas as outras alterações fica para a auditoria da Fase 08. | Guardar só os IDs dos estados: um estado renomeado ou excluído deixaria o histórico ilegível. |

### Configuração inicial (D3)

| Estado | Categoria | Inicial |
|---|---|---|
| Backlog | A fazer | Sim |
| A fazer | A fazer | |
| Em andamento | Em andamento | |
| Em revisão | Em andamento | |
| Bloqueada | Bloqueada | |
| Concluída | Concluída (final) | |

**Transições iniciais:**
- Backlog → A fazer
- A fazer → Backlog
- A fazer → Em andamento
- Em andamento → A fazer
- Em andamento → Em revisão
- Em revisão → Em andamento
- Em revisão → Concluída (**exige aprovação**)
- Backlog, A fazer, Em andamento e Em revisão → Bloqueada
- Bloqueada → A fazer
- Bloqueada → Em andamento
- Concluída → Em andamento (reabrir; **exige aprovação**)

### Decisões de baixo risco (serão registradas no ADR-013)

- **Tarefa nova:** entra no estado inicial do workflow do projeto. O `POST /projects/{id}/tasks` não aceita outro estado.
- **Listagem de tarefas:**
  - filtros novos: `stateId` e `category`; `open=true` traz as tarefas fora dos estados finais;
  - ordenação nova: `state`, pela posição do estado;
  - cada item traz o estado e a categoria.
- **Detalhes da tarefa:**
  - mostram o estado atual e as **transições disponíveis para o usuário** (as que ele pode executar agora);
  - a interface da Fase 09 e os agentes usam isso para não oferecer opções que seriam recusadas.
- **Atraso (`overdue`):** passa a desconsiderar tarefas em estado final.
- **Projeto arquivado:** o workflow fica somente leitura, assim como o resto do projeto.
- **Concorrência:** `xmin` no workflow, nos estados, nas transições e na tarefa; conflito responde 409.

## Contratos previstos (`/api/v1`)

| Método e rota | Permissão |
|---|---|
| `GET /projects/{projectId}/workflow` (estados, transições e configurações) | `project.read` |
| `PUT /projects/{projectId}/workflow` (estado inicial e `requireIndependentApproval`) | `workflow.manage` |
| `POST /projects/{projectId}/workflow/states` | `workflow.manage` |
| `PATCH /workflow/states/{stateId}` (nome e categoria) | `workflow.manage` |
| `POST /workflow/states/{stateId}/move` (`{ position }`) | `workflow.manage` |
| `DELETE /workflow/states/{stateId}` (sem tarefas; não pode ser o inicial) | `workflow.manage` |
| `POST /workflow/transitions` (`{ fromStateId, toStateId, requiresApproval }`) | `workflow.manage` |
| `DELETE /workflow/transitions/{transitionId}` | `workflow.manage` |
| `POST /tasks/{taskId}/transitions` (`{ toStateId, expectedStateId?, comment? }`) | `tasks.write` (e `tasks.approve` se a transição exigir aprovação) |
| `GET /tasks/{taskId}/history` | `project.read` |

As rotas `/workflow/...` são por ID do recurso. Elas recebem a organização pelo cabeçalho `X-Gestalyn-Organization` e calculam o acesso pelo projeto do estado ou da transição, como `[TaskPermission]` faz para as tarefas (ADR-011).

## Dependências

| Item | Estado |
|---|---|
| Fase 06 (tarefas, dependências) | Completed, integrada em `develop` |
| Correções da auditoria (refresh token, validação, proteção das branches) | Completed, integradas em `develop` |
| Docker (PostgreSQL e Mailpit) | Instalado; precisa estar em execução para os testes de integração |
| Pacotes novos | Nenhum previsto |

## Tarefas

| ID | Tarefa | Subtarefas |
|---|---|---|
| F07-001 | Criar `WorkflowDefinition` | Um por projeto; configurações (estado inicial, aprovação independente) |
| F07-002 | Criar `WorkflowState` | Nome, categoria, posição; nomes únicos; FKs compostas |
| F07-003 | Criar `WorkflowTransition` | Origem, destino, exige aprovação; sem duplicadas nem para o mesmo estado |
| F07-004 | Criar estados iniciais | Configuração inicial em projetos novos; migração dos projetos e das tarefas existentes (D3) |
| F07-005 | Implementar configuração por projeto | Endpoints de estados, transições e configurações; permissão `workflow.manage` (D6) |
| F07-006 | Implementar validação de transições | `POST /tasks/{id}/transitions`, na ordem de validação de D4 |
| F07-007 | Implementar categorias de estado | Lista fixa de categorias; filtros e ordenação na listagem (D2) |
| F07-008 | Implementar estados finais | Tarefa em estado final somente leitura; dependências exigem estado final (D2 e D4) |
| F07-009 | Implementar restrições de aprovação | `tasks.approve` e a regra de não aprovar o próprio trabalho (D5) |
| F07-010 | Implementar histórico de status | `TaskStatusHistory`, `GET /tasks/{id}/history` (D7) |
| F07-011 | Implementar proteção contra alterações inválidas | Estado em uso, estado inicial, invariantes do workflow, concorrência, projeto arquivado |
| F07-012 | Criar testes de workflow | Domínio, fluxos, matriz de permissões, aprovação, dependências, migração, isolamento |

## Arquivos previstos

```
src/ProjectManager.Domain/Workflows/           WorkflowDefinition, WorkflowState, WorkflowTransition, StateCategory,
                                               TaskStatusHistory, ActorKind, regras de transição e de aprovação
src/ProjectManager.Domain/Projects/            ProjectPermissions (workflow.manage, tasks.approve)
src/ProjectManager.Domain/Tasks/ProjectTask.cs StateId; somente leitura em estado final
src/ProjectManager.Application/Workflows/      IWorkflowService, contratos
src/ProjectManager.Infrastructure/Workflows/   WorkflowService (configuração e transições)
src/ProjectManager.Infrastructure/Persistence/Migrations/  Migration do workflow, com a migração de dados (D3)
src/ProjectManager.API/Controllers/            WorkflowController; TasksController (transições e histórico)
src/ProjectManager.API/Organizations/          WorkflowPermissionAttribute (acesso pelo projeto do estado ou da transição)
tests/ProjectManager.UnitTests/Domain/         Regras do workflow e de aprovação
tests/ProjectManager.IntegrationTests/Workflows/  Fluxos, permissões, aprovação, dependências, migração e isolamento
docs/decisions/ADR-013-WORKFLOW-CONFIGURAVEL.md
docs/api/WORKFLOW.md
```

## Critérios de aceitação

| Critério (especificação mestre) | Como será verificado |
|---|---|
| Projetos podem ter fluxos diferentes | Dois projetos com estados e transições diferentes; uma transição válida num projeto é recusada no outro; um estado de um projeto não pode ser usado em outro (FK composta) |
| Transições inválidas são rejeitadas | Transição inexistente, para o mesmo estado, a partir de um estado diferente do esperado, para um estado de outro projeto e para Concluída com dependências abertas: todas recusadas, sem mudar a tarefa nem o histórico |
| Alterações são registradas | Cada mudança de estado, inclusive a criação e a migração, aparece no histórico com origem, destino, nomes, autor, tipo de autor e data; o histórico continua legível depois de renomear ou excluir um estado |
| Estados finais são respeitados | Tarefa em estado final recusa edição de dados, responsáveis, etiquetas, dependências e anexos; aceita comentário e reabertura pela transição configurada |
| Permissões são verificadas | Matriz por papel: Observador não muda estado; Colaborador muda, mas não aprova; Gerente aprova; só `workflow.manage` altera o workflow; 404 entre organizações |
| Agentes não aprovam o próprio trabalho por padrão | **Teste unitário** da regra de aprovação com um autor do tipo agente que é responsável pela tarefa, recusado por padrão. **Teste de integração** com pessoas e `requireIndependentApproval` ligado. O teste de integração com agentes reais fica para a Fase 10, quando houver credenciais de agente |

## Estratégia de testes

- **Unitários:**
  - invariantes do workflow (estado inicial único e não final, nomes únicos, transições sem duplicata nem laço);
  - categorias e estados finais;
  - regra de aprovação, com as combinações de pessoa ou agente, responsável ou não e configuração ligada ou desligada;
  - configuração inicial.
- **Integração** (PostgreSQL via Testcontainers):
  - configuração completa do workflow pela API;
  - transições válidas e cada tipo de transição inválida;
  - matriz de permissões;
  - aprovação, dependências e somente leitura em estado final;
  - histórico, inclusive depois de renomear um estado;
  - corrida de duas transições simultâneas na mesma tarefa;
  - **migração**: banco migrado até a Fase 06 com projeto e tarefas, depois até o fim. Todos os projetos ficam com o workflow e todas as tarefas no estado inicial, com o histórico;
  - isolamento cross-tenant de todas as novas tabelas, com recusa das FKs compostas.
- **Mutação:** retirar a verificação de transição existente e a de dependências deve fazer os testes falharem.
- **Execução real:** API em execução, workflow configurado num projeto, tarefa levada de Backlog até Concluída com aprovação e o histórico conferido.

## Riscos

| Risco | Mitigação |
|---|---|
| A migração de dados criar workflows ou estados inconsistentes nos projetos existentes | SQL dentro da migration, numa transação, com teste que migra de um banco da Fase 06 com dados |
| Workflow configurado sem caminho até um estado final | Fica permitido (o projeto pode querer assim), mas os detalhes da tarefa mostram as transições disponíveis. Validar alcançabilidade fica para a Fase 09, com a tela de configuração |
| Mudar a categoria de um estado em uso muda o comportamento de tarefas existentes | Documentado. A mudança fica restrita a `workflow.manage` e aparece no histórico do workflow (Fase 08) |
| O critério de agentes não pode ter teste de integração completo antes da Fase 10 | Regra coberta por teste unitário agora e por teste de integração na Fase 10. A especificação da Fase 10 deve trazer esse teste como critério |
| Volume da fase | Commits por área: modelo e migração; configuração; transições, aprovação e histórico |

## Definição de conclusão

- Todos os critérios verificados com evidência, local e no CI.
- Mutações da verificação de transição e de dependências detectadas.
- ADR-013 registrado.
- Documentação atualizada: `docs/api/WORKFLOW.md`, README, AGENTS.md, ARCHITECTURE, `PROJECT_STATUS.md` e relatório da Fase 07.
- Alterações integradas por PR, com o check "Build e testes" aprovado.

## Execução e ajustes em relação ao plano

| ID | Estado | Evidência |
|---|---|---|
| F07-001 | Completed | `WorkflowDefinition` (um por projeto, `project_id` único) com `requireIndependentApproval` |
| F07-002 | Completed | `WorkflowState` com nomes únicos no domínio e FKs compostas; `StateNames_AreUniqueIgnoringCase` |
| F07-003 | Completed | `WorkflowTransition` sem laço (check) nem duplicata (índice único); `Transitions_RejectLoopsDuplicatesAndForeignStates` |
| F07-004 | Completed | `CreateDefault` em projetos novos; migração de dados testada (`ExistingProjectsAndTasks_ReceiveTheDefaultWorkflow_AndTheInitialState`) e aplicada no banco de desenvolvimento (7 projetos, 5 tarefas) |
| F07-005 | Completed | `WorkflowController` e `workflow.manage`; `Manager_ConfiguresStatesAndTransitions_WithinTheInvariants`, `Projects_HaveIndependentWorkflows` |
| F07-006 | Completed | `POST /tasks/{id}/transitions`; `InvalidTransitions_AreRejected_WithoutChangingTheTaskOrTheHistory`; mutação detectada |
| F07-007 | Completed | Categorias fixas; filtros `category`, `stateId` e `open`, e ordenação `State`; `List_FiltersAndSortsByState` |
| F07-008 | Completed | Somente leitura em estado final; conclusão exige dependências resolvidas; `OpenDependencies_BlockCompletion_UntilTheyReachAFinalState`; 2 mutações detectadas |
| F07-009 | Completed | `tasks.approve` e a regra do próprio trabalho; `IndependentApproval_StopsAssigneesFromApprovingTheirOwnWork`; teste unitário com agentes; mutação detectada |
| F07-010 | Completed | `TaskStatusHistory` e `GET /tasks/{id}/history`; `History_StaysReadable_AfterStatesAreRenamedOrDeleted` |
| F07-011 | Completed | Estado em uso, estado inicial, invariantes, concorrência (`SimultaneousTransitions_LetOnlyOneWin`, estável em 5 execuções) e projeto arquivado |
| F07-012 | Completed | 21 casos unitários novos e 15 testes de integração novos; 4 mutações detectadas |
| CI | Completed | [run 43666361156](https://github.com/harrisonleandro/Gestalyn/actions/runs/43666361156): 0 avisos, 148/148 e 132/132; [PR #13](https://github.com/harrisonleandro/Gestalyn/pull/13), merge `a6f87f9` |

**Critério "agentes não aprovam o próprio trabalho por padrão":** a regra está implementada (`TransitionRules`) e coberta por teste unitário. O teste de integração com agentes reais depende das credenciais de agente da Fase 10 e deve ser critério dela.

Ajustes feitos durante a implementação, dentro das decisões aprovadas:

1. **Sem índice único parcial para o estado inicial:** trocar o estado inicial violava o índice no meio da gravação. A regra ficou no domínio, e toda edição do workflow trava a linha do workflow (`xmin`). A migration foi gerada de novo, com a migração de dados reaplicada.
2. **Migration ajustada à mão:** o EF criaria `state_id` obrigatório com o GUID vazio, o que falharia com tarefas existentes. A coluna nasce opcional, recebe os estados e só então vira obrigatória.
3. **Exclusão explícita de estados e transições:** sair da coleção do workflow deixava órfãos numa relação obrigatória (500). O serviço marca as linhas como excluídas, e o domínio renumera as posições.
4. **`ProjectAccessGrant.UserId`:** necessário para calcular, nos detalhes da tarefa, se o usuário é responsável por ela, sem passar o usuário por todas as chamadas.
5. **Dependência arquivada conta como resolvida:** uma tarefa arquivada saiu do fluxo e não deve impedir a conclusão das que dependiam dela.
