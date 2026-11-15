# API: workflow e estados das tarefas

Contratos da Fase 07 ([ADR-013](../decisions/ADR-013-WORKFLOW-CONFIGURAVEL.md)). Todas as rotas exigem `Authorization: Bearer` e o cabeçalho **`X-Gestalyn-Organization`**. O documento OpenAPI completo fica em `/openapi/v1.json` (Development).

## Acesso

| Permissão | Gerente | Colaborador | Observador |
|---|---|---|---|
| `project.read`: ler o workflow e o histórico | sim | sim | sim |
| `tasks.write`: mudar o estado de tarefas | sim | sim | não |
| `tasks.approve`: executar transições que exigem aprovação | sim | não | não |
| `workflow.manage`: alterar estados, transições e configurações | sim | não | não |

- **Rotas:** `/projects/{projectId}/...` usam o acesso ao projeto; `/workflow/states/{id}`, `/workflow/transitions/{id}` e `/tasks/{id}/...` usam o acesso ao projeto do recurso.
- **Respostas:**
  - recurso invisível: 404;
  - sem a permissão: 403;
  - violação de regra ou conflito de concorrência: 409;
  - projeto arquivado: somente leitura (409).

## Workflow

| Método e rota | Permissão | Respostas |
|---|---|---|
| `GET /projects/{projectId}/workflow` | `project.read` | **200** `WorkflowDetails` |
| `PUT /projects/{projectId}/workflow` | `workflow.manage` | **200** `WorkflowDetails` · 404 (estado inexistente) · 409 (estado inicial final) |
| `POST /projects/{projectId}/workflow/states` | `workflow.manage` | **201** `WorkflowStateDetails` · 400 · 409 (nome repetido, limite) |
| `PATCH /workflow/states/{stateId}` | `workflow.manage` | **200** `WorkflowStateDetails` · 409 |
| `POST /workflow/states/{stateId}/move` | `workflow.manage` | **200** `WorkflowDetails` |
| `DELETE /workflow/states/{stateId}` | `workflow.manage` | **204** · 409 (estado com tarefas, ou o inicial) |
| `POST /workflow/transitions` | `workflow.manage` (no projeto do estado de origem) | **201** `WorkflowTransitionDetails` · 404 (destino de outro projeto) · 409 (repetida, laço) |
| `DELETE /workflow/transitions/{transitionId}` | `workflow.manage` | **204** |

**`WorkflowDetails`:** `{ projectId, requireIndependentApproval, states: [...], transitions: [...] }`.

- Os estados vêm em ordem de posição, cada um como `{ id, name, category, isFinal, isInitial, position, taskCount }`.
- As transições vêm como `{ id, fromStateId, toStateId, requiresApproval }`.

**Corpos:**

- **Configurações (`PUT`):** `{ "initialStateId": "…", "requireIndependentApproval": true }`; os dois campos são opcionais. Com `requireIndependentApproval`, quem é responsável pela tarefa não aprova o próprio trabalho. Para agentes, essa regra vale sempre.
- **Estado novo:** `{ "name": "Cancelada", "category": "Canceled" }`.
  - `category` aceita `Todo`, `InProgress`, `Blocked`, `Done` ou `Canceled`;
  - `Done` e `Canceled` são finais;
  - o nome tem até 60 caracteres e é único no workflow.
- **Alterar estado (`PATCH`):** `{ "name": "…", "category": "…" }`, os dois opcionais. O estado inicial não pode virar final.
- **Mover:** `{ "position": 1 }`; as demais posições são renumeradas.
- **Transição:** `{ "fromStateId": "…", "toStateId": "…", "requiresApproval": false }`.

**Configuração inicial** de todo projeto:

| Estado | Categoria |
|---|---|
| Backlog (inicial) | `Todo` |
| A fazer | `Todo` |
| Em andamento | `InProgress` |
| Em revisão | `InProgress` |
| Bloqueada | `Blocked` |
| Concluída | `Done` |

São 14 transições. Exigem aprovação Em revisão → Concluída e Concluída → Em andamento.

## Estado das tarefas

| Método e rota | Permissão | Respostas |
|---|---|---|
| `POST /tasks/{taskId}/transitions` | `tasks.write`, e `tasks.approve` se a transição exigir aprovação | **200** `TaskDetails` · 403 · 404 · 409 |
| `GET /tasks/{taskId}/history` | `project.read` | **200** `TaskHistoryEntry[]` |

**Transição:** `{ "toStateId": "…", "expectedStateId": "…", "comment": "Aprovada na revisão" }`.

- `expectedStateId` é opcional. Se a tarefa não estiver mais nesse estado, a resposta é 409.
- **Recusas com 409:**
  - não existe transição do estado atual para o destino, inclusive para o próprio estado;
  - o destino é da categoria `Done` e alguma tarefa da qual esta depende não está num estado final nem arquivada;
  - outra pessoa moveu a tarefa ao mesmo tempo.
- **Recusas com 403:**
  - a transição exige aprovação e falta `tasks.approve`;
  - quem pede é responsável pela tarefa e o workflow exige aprovação independente.

**Histórico:** cada entrada é `{ id, fromStateId, fromStateName, toStateId, toStateName, changedById, changedByName, actorKind, comment, changedAt }`, em ordem cronológica.

- Os nomes são os da data da mudança.
- A primeira entrada registra a criação da tarefa e não tem estado de origem.

**Na tarefa** (ver [TASKS.md](TASKS.md)):

- os detalhes trazem `state` (`{ id, name, category, isFinal }`) e `availableTransitions` (`{ transitionId, toStateId, toStateName, toCategory, requiresApproval }`), as transições que o usuário pode executar agora;
- a listagem traz `state` e aceita `stateId`, `category`, `open=true` (fora dos estados finais) e `sortBy=State`;
- **em estado final**, a tarefa é somente leitura: dados, responsáveis, etiquetas, dependências e anexos respondem 409. Comentários e transições continuam permitidos.
