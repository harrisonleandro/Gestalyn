# ADR-013: Workflow configurável, aprovação e histórico de estados

- **Status:** Aceito
- **Data:** 2026-11-15
- **Fase:** 07 (Workflow configurável e histórico). Decisões D1 a D7 aprovadas pelo responsável em 2026-11-14.

## Contexto

A especificação mestre (seção 4.5) exige estados e transições configuráveis por projeto, sem enum rígido, com categorias, estados finais, transições validadas no backend e restritas por permissão, regras de aprovação, controle de transições feitas por agentes e histórico de mudanças. Ela também exige que agentes não aprovem o próprio trabalho por padrão. Até a Fase 06, as tarefas não tinham estado (ADR-011, D1).

## Decisão

### Modelo (D1)

- **Um workflow por projeto** (`workflows`, `project_id` único), criado junto com o projeto.
- Estados e transições carregam `organization_id` e `project_id`, com FK composta para o workflow.
- A tarefa tem `state_id` obrigatório, com FK composta `(project_id, state_id)` para o estado. Assim, só aponta para um estado do próprio projeto.
- As transições também ligam dois estados do mesmo projeto pelas FKs `(project_id, from_state_id)` e `(project_id, to_state_id)`, e têm *check constraint* contra laço.

### Categorias e estados finais (D2)

- **Categorias fixas:** `Todo`, `InProgress`, `Blocked`, `Done` e `Canceled`. Os nomes dos estados são livres.
- **Estados finais:** os das categorias `Done` e `Canceled`.
- **Tarefa em estado final é somente leitura:** dados, responsáveis, etiquetas, dependências e anexos respondem 409. Ela ainda aceita comentários e as transições que saem desse estado, como "reabrir".

### Configuração inicial e migração (D3)

- **Estados:** Backlog (inicial), A fazer, Em andamento, Em revisão, Bloqueada e Concluída.
- **Transições:** 14, definidas em `WorkflowDefinition.CreateDefault`.
- **Migration `AddWorkflows`**, ajustada à mão porque o EF criaria `state_id` obrigatório com o GUID vazio:
  1. cria a coluna como opcional;
  2. cria a configuração inicial em cada projeto existente;
  3. coloca as tarefas no estado inicial, com uma entrada no histórico;
  4. só então torna a coluna obrigatória e cria a FK.
- **Justificativa do SQL sobre dados de organizações** (exigida pelo AGENTS.md):
  - é a única forma de dar estado às tarefas existentes;
  - roda uma única vez, dentro da migration;
  - um teste migra um banco da Fase 06 e compara o resultado com `CreateDefault`.
- **Banco de desenvolvimento:** 7 projetos migrados, com 42 estados e 98 transições; as 5 tarefas existentes ficaram com estado e histórico.

### Transições (D4)

- Só há transições explícitas.
- O estado muda por `POST /tasks/{id}/transitions` (`toStateId`, `expectedStateId` opcional e `comment` opcional), nunca pelo `PATCH`.
- **Ordem das verificações:**
  1. permissão `tasks.write`;
  2. tarefa e projeto não arquivados;
  3. estado esperado igual ao atual;
  4. estado de destino no mesmo projeto;
  5. a regra única, `TransitionRules.Evaluate`: transição existente, aprovação, próprio trabalho e dependências;
  6. ao gravar, a trava otimista da tarefa (`xmin`), que resolve transições simultâneas.
- **Respostas:** transição inexistente, estado esperado diferente ou dependências abertas, 409; aprovação sem permissão ou do próprio trabalho, 403.
- **Dependências:** entrar num estado `Done` exige que todas as tarefas das quais esta depende estejam num estado final ou arquivadas.

### Aprovação (D5)

- **Exige aprovação:** uma transição marcada assim só é executada por quem tem `tasks.approve` (Gerente; também `projects.manage_all`).
- **Próprio trabalho:** quem é responsável pela tarefa não executa transições que exigem aprovação:
  - **agentes:** sempre (`ActorKind.Agent`), como pede a especificação. A regra está implementada e coberta por teste unitário, e entra em uso quando os agentes existirem (Fase 10);
  - **pessoas:** só com `requireIndependentApproval` ligado no workflow (desligado por padrão).
- **Na configuração inicial**, exigem aprovação Em revisão → Concluída e Concluída → Em andamento.

### Configuração e permissões (D6)

- `workflow.manage` (Gerente) altera o workflow; `project.read` lê.
- **Rotas por ID do recurso:** `/workflow/states/{id}` e `/workflow/transitions/{id}` calculam o acesso pelo projeto do recurso (`WorkflowStatePermission`, `WorkflowTransitionPermission`).
- **Criação de transição** (`POST /workflow/transitions`): o projeto é deduzido do estado de origem, com a mesma verificação dos atributos (`ProjectResourcePermissionAttribute.AuthorizeAsync`).
- **`PUT /projects/{id}/workflow`** altera só as configurações: o estado inicial e `requireIndependentApproval`.
- **Invariantes no domínio** (`WorkflowRuleException`, respondem 409):
  - exatamente um estado inicial, que não pode ser final;
  - nomes únicos no workflow, sem diferenciar maiúsculas e minúsculas;
  - sem laço nem transição duplicada;
  - até 30 estados e 200 transições;
  - o estado inicial não pode ser excluído;
  - estado com tarefas não pode ser excluído.
- **Sem índice único parcial para o estado inicial:** trocar o estado inicial desmarca um e marca outro na mesma gravação, e um índice seria violado no meio dela. Toda edição marca o workflow como alterado: o `xmin` da linha serializa edições simultâneas e protege as invariantes. Excluir um estado remove explicitamente as transições dele e renumera as posições.

### Histórico (D7)

- `task_status_history` registra cada mudança, inclusive a criação da tarefa e a migração.
- Cada registro guarda: estados de origem e de destino, com os **nomes na data**; quem fez (`ChangedById`, sem FK, porque pode ser um agente); `ActorKind`; o comentário; e a data.
- Os registros nunca são alterados pela API.
- `GET /tasks/{id}/history` lista em ordem cronológica, com o nome de quem fez quando é uma pessoa.

### Demais regras

- **Tarefa nova:** entra no estado inicial, com histórico.
- **Detalhes da tarefa:** trazem o estado e as transições que o usuário pode executar agora, já filtradas por permissão, aprovação e próprio trabalho.
- **Listagem:**
  - cada item traz o estado;
  - filtros `stateId`, `category` e `open`;
  - ordenação `State`, pela posição;
  - `overdue` desconsidera estados finais.
- **Acesso por usuário:** `ProjectAccessGrant` passou a carregar o `UserId`, para o cálculo do próprio trabalho nas transições disponíveis.
- **Projeto arquivado:** workflow e transições ficam somente leitura.

## Alternativas consideradas

| Alternativa | Motivo da rejeição |
|---|---|
| Workflow da organização, compartilhado | Contraria "cada projeto poderá configurar seus próprios estados" |
| Marcação "final" independente da categoria | Permitiria um estado "Em andamento" final |
| Mudar o estado pelo `PATCH` da tarefa | Mistura edição de dados com uma operação que tem regras e histórico próprios |
| Transições "de qualquer estado" | Enfraquece "transições inválidas são rejeitadas" |
| Pedido de aprovação assíncrono | Exige fila, notificações e telas; entra melhor com os agentes |
| Índice único parcial para o estado inicial | Violado no meio da troca de estado inicial (descrito acima) |
| Guardar só os IDs no histórico | Estado renomeado ou excluído deixaria o histórico ilegível |

## Consequências

- **Fase 08:** progresso e dashboards podem usar as categorias, e a auditoria completa complementa o histórico de estados.
- **Fase 09:** usa as transições disponíveis para montar o quadro e a tela de configuração.
- **Fase 10:** registra agentes como `ActorKind.Agent`; a regra do próprio trabalho já vale para eles. O teste de integração com agentes reais deve ser critério da Fase 10.
- **Alcançabilidade:** um workflow sem caminho até um estado final é permitido. Validar a alcançabilidade fica para a tela de configuração (Fase 09).
