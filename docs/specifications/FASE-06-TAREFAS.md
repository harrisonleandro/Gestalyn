# Fase 06: Fases, marcos e tarefas

- **Estado:** In Review. Implementada e validada localmente (testes, mutação e execução real da API); faltam o CI no GitHub e a integração em `develop`. Decisões D1 a D7 aprovadas pelo responsável em 2026-11-11.
- **Referência:** [Especificação mestre](ESPECIFICACAO-MESTRE.md), seção 9 (Fase 06), seções 4.3 e 4.4 (modelo funcional), 5.4 (contratos) e 7 (segurança: upload de arquivos)
- **Branch:** `feature/fase-06-tarefas` (a partir de `develop`, com as Fases 01 a 05 integradas)
- **Pré-requisito para implementar:** decisões aprovadas

## Objetivo

Implementar o gerenciamento operacional do trabalho dentro de cada projeto: fases e marcos do projeto e tarefas com vários responsáveis, prioridade, prazo, comentários, etiquetas, dependências e anexos. Também entram nesta fase o arquivamento lógico, os filtros e a paginação. Todo acesso passa pelo cálculo de acesso ao projeto da Fase 05 ([ADR-010](../decisions/ADR-010-PROJETOS-E-EQUIPES.md)).

## Escopo

1. **Fases do projeto** (`ProjectPhase` completa): criar, listar, editar, reordenar e excluir quando não houver tarefas nelas.
2. **Marcos** (`Milestone`): criar, listar, editar, marcar como atingido e excluir quando não houver tarefas neles.
3. **Tarefas** (`Task`): criar, listar (com filtros e paginação), consultar, editar e arquivar logicamente, com número sequencial por projeto (D7) e ligação opcional a uma fase e a um marco do mesmo projeto.
4. **Responsáveis** (`TaskAssignment`, D4): vários por tarefa, todos da equipe do projeto.
5. **Prioridade e prazo** da tarefa.
6. **Comentários** (`TaskComment`): criar, listar, editar e excluir.
7. **Etiquetas** (`TaskLabel`) e **dependências** (`TaskDependency`, D5).
8. **Anexos** (`TaskAttachment`, D2): upload, listagem, download protegido e exclusão.
9. **Permissões de tarefa** no catálogo de projeto (D6).
10. **Testes:** fluxos, permissões por papel, regras de dependência, segurança dos anexos, filtros e isolamento cross-tenant das novas entidades.
11. **Documentação:** ADR-011, `docs/api/TASKS.md` e atualização de ARCHITECTURE, README e AGENTS.md.

## Fora de escopo

- **Estados e transições das tarefas** (workflow configurável): Fase 07. Veja D1.
- **`GET /tasks/{id}/history`:** o histórico de estados vem com o workflow (Fase 07), e o de alterações, com a auditoria (Fase 08).
- Progresso, dashboards e estatísticas: Fase 08.
- Interface Web de tarefas, fases e marcos: Fase 09.
- Tarefas criadas ou alteradas por agentes (`/agents/tasks`): Fase 10.
- Comandos `gestalyn task` e `gestalyn phase`: Fase 11.
- Armazenamento em object storage compatível com S3: Fase 14 (veja D2).
- Antivírus nos anexos e cotas de armazenamento por organização: Fases 13 e 14.
- Notificações (menções, atribuição): fora do MVP desta fase.

## Decisões (D1 a D7 aprovadas)

| ID | Proposta | Alternativas |
|---|---|---|
| **D1** Estado da tarefa | **Nenhum campo de estado nesta fase.** A especificação mestre proíbe estados em enum rígido e os coloca na Fase 07, que cria o workflow por projeto e dá a todas as tarefas existentes o estado inicial, por migration. Nesta fase, a tarefa só tem o arquivamento lógico. | (a) Enum provisório (A fazer, Em andamento, Concluída) para migrar na Fase 07: contraria a regra de não usar enum rígido e cria um contrato que mudaria na fase seguinte. (b) Antecipar o workflow mínimo: mistura duas fases e invade o escopo da Fase 07. |
| **D2** Armazenamento dos anexos | Abstração **`IFileStorage`** na Application, com implementação em **disco local** na Infrastructure (diretório configurado em `Storage:Attachments:RootPath`, fora do `wwwroot` e fora do repositório). Cada arquivo é gravado com o ID do anexo como nome, nunca com o nome enviado. Proteções: **limite de 10 MB**; **lista de tipos permitidos**, conferida pela extensão e pela assinatura do conteúdo (*magic bytes*), sem SVG nem executáveis; tipo de conteúdo definido pelo servidor; SHA-256 registrado; download **só pela API**, com autorização, `Content-Disposition: attachment` e `X-Content-Type-Options: nosniff`. A implementação S3 entra na Fase 14 sem mudar a Application. | (a) Arquivos no PostgreSQL (`bytea`): transacional e já coberto pelo backup, mas aumenta o banco e a memória das consultas. (b) S3 compatível (MinIO) agora: exige contêiner, pacote novo e credenciais antes da fase de infraestrutura. Com a abstração, a troca futura é só de implementação. |
| **D3** Fases e marcos | **Fase:** nome, descrição, início e fim (fim igual ou posterior ao início) e posição, com reordenação pela lista completa de IDs. **Marco:** nome, descrição, data prevista, fase opcional e data em que foi atingido. A tarefa pode estar em **uma fase e em um marco, ambos do mesmo projeto**, o que o banco garante por FK composta `(project_id, phase_id)`. Fases e marcos com tarefas não podem ser excluídos (409); sem tarefas, a exclusão é física. Gerenciar fases e marcos exige `project.plan` (D6). | (a) Arquivamento lógico de fases e marcos: sem critério da fase que o peça, e a exclusão de itens vazios basta para corrigir erros de planejamento. (b) Tarefa em várias fases: o modelo da especificação liga a tarefa a uma fase. |
| **D4** Responsáveis | Vários responsáveis por tarefa (até 10), **todos da equipe do projeto**. O banco garante isso com uma FK composta `(project_id, user_id)` → `project_members` **com cascata**: quem sai da equipe deixa de ser responsável pelas tarefas. Endpoints: `POST /tasks/{id}/assignments` (adicionar) e `DELETE /tasks/{id}/assignments/{userId}`. | (a) Qualquer membro da organização: a pessoa ficaria responsável por uma tarefa de um projeto que talvez nem veja (D1 da Fase 05). (b) Adicionar a pessoa automaticamente à equipe: mudaria a equipe por um efeito colateral. |
| **D5** Dependências | Só um tipo: **"bloqueada por"** (a tarefa A depende da B). Ambas precisam ser do **mesmo projeto**. A dependência não pode ser da tarefa consigo mesma nem duplicada, e **ciclos são recusados** (409): antes de gravar, uma busca no grafo de dependências do projeto, dentro de uma transação serializável. Nesta fase, a dependência é informativa; a Fase 07 poderá impedir a conclusão de uma tarefa bloqueada. | (a) Dependências entre projetos da organização: complica a visibilidade (D1 da Fase 05), porque a tarefa bloqueadora pode estar num projeto que o usuário não vê. (b) Vários tipos (relacionada, duplicada): sem critério da fase. |
| **D6** Permissões de tarefa | Permissões novas no catálogo de projeto: **`project.plan`** (fases e marcos), **`tasks.write`** (criar e editar tarefas, responsáveis, etiquetas, dependências e anexos), **`tasks.comment`** e **`tasks.archive`**. **Gerente:** todas. **Colaborador:** `tasks.write` e `tasks.comment`. **Observador:** só leitura (`project.read`), como no ADR-010. Para ler tarefas, comentários e anexos, basta `project.read`. **Comentários:** só o autor edita; o autor ou quem tem `tasks.archive` exclui. `projects.manage_all` continua concedendo todas as permissões de projeto. Como os papéis de projeto são definidos no código, não há migração de dados. | (a) Observador também comenta: útil para quem acompanha o projeto, mas o ADR-010 o definiu como somente leitura. É simples de mudar depois. (b) Colaborador também arquiva: tirar tarefas da lista do projeto parece decisão do Gerente. |
| **D7** Número da tarefa | Cada tarefa recebe um **número sequencial no projeto** (1, 2, 3...), além do UUID, para ser citada em conversas, commits, na CLI (`gestalyn task show 42`) e pelos agentes. Esse número não muda nem é reutilizado. Um contador no projeto (`next_task_number`) é incrementado com trava otimista (`xmin`), com até 3 tentativas em caso de criação simultânea, e um índice único `(project_id, number)` garante a unicidade. A API continua usando o UUID nas rotas. | (a) Só o UUID: impraticável para pessoas e para a CLI. (b) Número calculado como o maior número existente + 1: depende de travar toda a tabela, ou dá conflito de unicidade sob concorrência. |

Decisões de baixo risco já tomadas (serão registradas no ADR-011):

- **Rotas:**
  - tarefas sob o projeto (`/projects/{projectId}/tasks`) e por ID (`/tasks/{taskId}`), como na especificação mestre;
  - fases e marcos aninhados no projeto (`/projects/{projectId}/phases[/{phaseId}]` e `/projects/{projectId}/milestones[/{milestoneId}]`);
  - todas com o cabeçalho `X-Gestalyn-Organization`;
  - em `/tasks/{taskId}`, o acesso é calculado pelo projeto da tarefa. Tarefa de projeto invisível responde 404.
- **Dados da tarefa:**
  - título (até 200 caracteres) e descrição (até 20.000, texto Markdown guardado como texto);
  - prioridade `Low`, `Medium` (padrão), `High` ou `Critical`, a mesma enumeração do projeto, renomeada para `Priority` (os valores no JSON não mudam);
  - prazo como data sem hora.
- **Etiquetas de tarefa:** texto livre, como as do projeto: até 50 caracteres, únicas sem diferenciar maiúsculas e minúsculas, até 20 por tarefa, enviadas como conjunto completo no `PATCH`.
- **Arquivamento:**
  - `DELETE /tasks/{id}` arquiva; a tarefa arquivada fica somente leitura e sai da listagem padrão;
  - em projeto arquivado, tarefas, fases e marcos ficam somente leitura (409).
- **Listagem de tarefas:**
  - paginada (`page`, `pageSize` com padrão 50 e máximo 100);
  - filtros `phaseId`, `milestoneId`, `assigneeId` (ou `assignee=me`), `label`, `priority`, `dueBefore`, `dueAfter`, `overdue`, `search` (no título, sem diferenciar maiúsculas e minúsculas) e `includeArchived`;
  - ordenação `number` (padrão), `dueDate`, `priority` ou `updatedAt`, crescente ou decrescente.
- **Comentários:** texto de até 10.000 caracteres; a exclusão é física. O histórico fica para a Fase 08.
- **Anexos:**
  - até 20 por tarefa;
  - tipos permitidos: PNG, JPEG, GIF, WebP, PDF, TXT, MD, CSV, JSON, LOG, DOCX, XLSX e PPTX;
  - o nome original é guardado limpo (sem caminho nem caracteres de controle) e só no banco;
  - o arquivo é gravado antes da linha no banco e apagado se a gravação da linha falhar;
  - ao excluir, a linha sai primeiro e depois o arquivo. Uma falha nessa etapa só é registrada em log, porque o arquivo órfão não fica acessível.
- **Concorrência:** `xmin` em tarefas, fases e marcos; conflito responde 409.
- **Reordenação de fases:** em transação, em duas etapas (deslocamento e posição final), para respeitar o índice único `(project_id, position)` e a regra `position > 0`.

## Contratos previstos (`/api/v1`)

| Método e rota | Permissão |
|---|---|
| `GET /projects/{projectId}/phases` | `project.read` |
| `POST /projects/{projectId}/phases` | `project.plan` |
| `PATCH /projects/{projectId}/phases/{phaseId}` | `project.plan` |
| `PUT /projects/{projectId}/phases/order` (`{ phaseIds: [...] }`) | `project.plan` |
| `DELETE /projects/{projectId}/phases/{phaseId}` (sem tarefas) | `project.plan` |
| `GET /projects/{projectId}/milestones` | `project.read` |
| `POST /projects/{projectId}/milestones` | `project.plan` |
| `PATCH /projects/{projectId}/milestones/{milestoneId}` (dados, atingido) | `project.plan` |
| `DELETE /projects/{projectId}/milestones/{milestoneId}` (sem tarefas) | `project.plan` |
| `POST /projects/{projectId}/tasks` | `tasks.write` |
| `GET /projects/{projectId}/tasks` (filtros e paginação) | `project.read` |
| `GET /tasks/{taskId}` (com responsáveis, etiquetas e dependências) | `project.read` |
| `PATCH /tasks/{taskId}` | `tasks.write` |
| `DELETE /tasks/{taskId}` (arquivar) | `tasks.archive` |
| `POST /tasks/{taskId}/assignments` (`{ userId }`) | `tasks.write` |
| `DELETE /tasks/{taskId}/assignments/{userId}` | `tasks.write` |
| `POST /tasks/{taskId}/dependencies` (`{ dependsOnTaskId }`) | `tasks.write` |
| `DELETE /tasks/{taskId}/dependencies/{dependsOnTaskId}` | `tasks.write` |
| `GET /tasks/{taskId}/comments` | `project.read` |
| `POST /tasks/{taskId}/comments` | `tasks.comment` |
| `PATCH /tasks/{taskId}/comments/{commentId}` | autor, com `tasks.comment` |
| `DELETE /tasks/{taskId}/comments/{commentId}` | autor, ou `tasks.archive` |
| `GET /tasks/{taskId}/attachments` | `project.read` |
| `POST /tasks/{taskId}/attachments` (`multipart/form-data`) | `tasks.write` |
| `GET /tasks/{taskId}/attachments/{attachmentId}/content` (download) | `project.read` |
| `DELETE /tasks/{taskId}/attachments/{attachmentId}` | `tasks.write` |

Os endpoints `DELETE` de responsáveis e dependências e os de edição e exclusão de comentários e anexos complementam os da especificação mestre (seção 5.4), que só lista a criação. Gerenciar esses itens exige poder desfazer.

## Dependências

| Item | Estado |
|---|---|
| Fase 05 (projetos, equipes e acesso a projetos) | Completed, integrada em `develop` |
| Docker (PostgreSQL e Mailpit) | Instalado |
| Pacotes novos | Nenhum previsto: a verificação de tipos usa assinaturas conhecidas, sem biblioteca externa |

## Tarefas

| ID | Tarefa | Subtarefas |
|---|---|---|
| F06-001 | Implementar fases | `ProjectPhase` completa; CRUD; reordenação; exclusão só sem tarefas (D3) |
| F06-002 | Implementar marcos | `Milestone`; CRUD; marcar como atingido; fase opcional (D3) |
| F06-003 | Implementar tarefas | `Task` com número por projeto (D7); fase e marco do mesmo projeto; `TaskPermissionAttribute` com acesso pelo projeto da tarefa |
| F06-004 | Implementar múltiplos responsáveis | `TaskAssignment` com FK composta para a equipe e cascata (D4) |
| F06-005 | Implementar prioridades | Enumeração `Priority` compartilhada com o projeto |
| F06-006 | Implementar prazos | Prazo e filtros `dueBefore`, `dueAfter` e `overdue` |
| F06-007 | Implementar comentários | `TaskComment`; regras de autoria (D6) |
| F06-008 | Implementar etiquetas | `TaskLabel` como as etiquetas de projeto |
| F06-009 | Implementar dependências | `TaskDependency`; mesmo projeto; sem duplicatas nem ciclos (D5) |
| F06-010 | Implementar anexos | `IFileStorage` com disco local; validação de tamanho e tipo; download protegido (D2) |
| F06-011 | Implementar arquivamento | Tarefa somente leitura; projeto arquivado bloqueia tarefas, fases e marcos |
| F06-012 | Implementar filtros e paginação | Filtros e ordenações da listagem de tarefas |
| F06-013 | Criar testes | Domínio, fluxos, matriz de permissões, ciclos, segurança dos anexos, filtros e isolamento cross-tenant |

## Arquivos previstos

```
src/ProjectManager.Domain/Common/Priority.cs               (renomeia ProjectPriority)
src/ProjectManager.Domain/Projects/                        ProjectPhase (completa), Milestone, ProjectPermissions (novas)
src/ProjectManager.Domain/Tasks/                           ProjectTask, TaskAssignment, TaskComment, TaskLabel, TaskDependency, TaskAttachment
src/ProjectManager.Application/Abstractions/Storage/       IFileStorage
src/ProjectManager.Application/Planning/                   Contratos e serviço de fases e marcos
src/ProjectManager.Application/Tasks/                      Contratos, ITaskService, regras de anexos (tipos e limites)
src/ProjectManager.Infrastructure/Storage/                 LocalFileStorage, StorageOptions
src/ProjectManager.Infrastructure/Planning/, Tasks/        Serviços
src/ProjectManager.Infrastructure/Persistence/Migrations/  Migration de fases, marcos e tarefas
src/ProjectManager.API/Controllers/                        PlanningController, TasksController
src/ProjectManager.API/Organizations/                      TaskPermissionAttribute
tests/ProjectManager.UnitTests/Domain/                     Regras de tarefas, dependências e tipos de arquivo
tests/ProjectManager.IntegrationTests/Tasks/               Fluxos, permissões, anexos, filtros e isolamento
docs/decisions/ADR-011-TAREFAS-E-ANEXOS.md
docs/api/TASKS.md
```

O nome da entidade no código será `ProjectTask`, para não colidir com `System.Threading.Tasks.Task`. A tabela e os contratos usam "task".

## Critérios de aceitação

| Critério (especificação mestre) | Como será verificado |
|---|---|
| Tarefas funcionais | Criar, listar, consultar, editar e arquivar pela API, com número, fase, marco, prioridade, prazo, etiquetas, comentários e anexos |
| Responsáveis corretamente associados | Vários responsáveis; só da equipe do projeto (aplicação e FK no banco); quem sai da equipe deixa de ser responsável |
| Dependências validadas | Recusa de dependência consigo mesma, duplicada, de outro projeto e circular (inclusive ciclos longos) |
| Anexos protegidos | Download só com acesso ao projeto; 404 entre organizações; limite de tamanho; tipo falsificado (extensão sem a assinatura correspondente) recusado; nome de arquivo malicioso neutralizado; `Content-Disposition: attachment` e `nosniff` no download |
| Filtros funcionando | Cada filtro e cada ordenação testados, com paginação e total |

## Estratégia de testes

- **Unitários:**
  - número e datas da tarefa;
  - normalização de etiquetas;
  - detecção de ciclos (grafo em memória);
  - validação de tipo e assinatura de arquivo e limpeza do nome;
  - permissões de cada papel de projeto.
- **Integração** (PostgreSQL via Testcontainers, armazenamento em diretório temporário):
  - fluxos completos pela API com usuários reais e matriz de permissões por papel;
  - dependências circulares e números de tarefa em criações simultâneas;
  - upload e download de anexos (tamanho, tipo, nome, cabeçalhos);
  - filtros e ordenações;
  - isolamento cross-tenant de todas as novas tabelas, com recusa das FKs compostas entre projetos e organizações.
- **Mutação:** remover a verificação de ciclo e a checagem de acesso no download deve fazer os testes falharem.
- **Execução real:** API em execução, upload e download de um arquivo de verdade e conferência do arquivo gravado no disco.

## Riscos

| Risco | Mitigação |
|---|---|
| Arquivo malicioso enviado como anexo | Lista de tipos com conferência da assinatura, sem SVG, HTML e executáveis; download sempre como anexo, com `nosniff`; antivírus avaliado nas Fases 13 e 14 |
| Caminho de arquivo manipulado (*path traversal*) | O arquivo é gravado com o ID do anexo como nome; o nome enviado nunca é usado no disco; teste com nomes como `../../x` |
| Disco local não serve para várias instâncias na nuvem | A abstração `IFileStorage` permite a implementação S3 na Fase 14 sem mudar a Application |
| Corrida na criação de dependências gerar ciclo | Verificação e gravação na mesma transação serializável; conflito responde 409 |
| Números de tarefa repetidos sob concorrência | Contador com trava otimista e índice único `(project_id, number)`; teste com criações simultâneas |
| Volume desta fase | Tarefas em passos pequenos, com commits por área: fases e marcos; tarefas; comentários e dependências; anexos |

## Definição de conclusão

- Todos os critérios verificados com evidência, local e no CI.
- Mutações da verificação de ciclo e do acesso no download detectadas.
- ADR-011 registrado.
- Documentação atualizada: `docs/api/TASKS.md`, README, AGENTS.md, ARCHITECTURE, `PROJECT_STATUS.md` e relatório da Fase 06.
- Alterações integradas por PR.

## Execução e ajustes em relação ao plano

| ID | Estado | Evidência |
|---|---|---|
| F06-001 | Completed | Fases com datas, reordenação em transação e exclusão só sem tarefas; `Phases_AreCreatedInOrder_EditedReordered_AndDeletedOnlyWithoutTasks` |
| F06-002 | Completed | Marcos com fase do mesmo projeto e data em que foi atingido; `Milestones_UsePhasesOfTheSameProject_TrackReaching_AndKeepTheirTasks` |
| F06-003 | Completed | `ProjectTask` com número por projeto; `[TaskPermission]`; `Create_NumbersTasksPerProject_WithPhaseMilestoneLabelsAndAssignees`, `ConcurrentCreation_NeverRepeatsNumbers` |
| F06-004 | Completed | `TaskAssignment` com FK composta para a equipe e cascata; `Assignees_MustBeOnTheTeam_AndLeaveTasksWhenTheyLeaveTheTeam` |
| F06-005 | Completed | `Priority` compartilhada; ordenação pela ordem da enumeração |
| F06-006 | Completed | Prazo; filtros `dueBefore`, `dueAfter` e `overdue` |
| F06-007 | Completed | Comentários com regras de autoria; `Comments_AreEditedOnlyByTheAuthor_AndDeletedByTheAuthorOrAManager` |
| F06-008 | Completed | `TaskLabel` com as regras de `Labels` |
| F06-009 | Completed | Dependências no mesmo projeto, sem duplicatas nem ciclos; `Dependencies_RejectSelfDuplicatesOtherProjectsAndCycles` |
| F06-010 | Completed | `IFileStorage` e `LocalFileStorage`; `AttachmentTests` (4) e `AttachmentFilesTests` |
| F06-011 | Completed | Tarefa arquivada somente leitura; projeto arquivado bloqueia tarefas, fases e marcos |
| F06-012 | Completed | Filtros, ordenações e paginação; `List_FiltersSortsAndPaginates` |
| F06-013 | Completed | 40 casos unitários novos e 19 testes de integração novos; 2 mutações detectadas |
| CI | **In Review** | Depende do push de `feature/fase-06-tarefas`, autorizado pelo responsável |

Ajustes feitos durante a implementação, dentro das decisões aprovadas:

1. **Um commit para fases, marcos, tarefas, comentários e dependências, e outro para anexos:** o plano previa quatro, mas a exclusão de fases e marcos depende das tarefas, e comentários e dependências entraram na mesma migration (`AddPlanningAndTasks`). Os anexos ficaram em migration e commit próprios (`AddTaskAttachments`).
2. **Filtro "minhas tarefas":** `assignedToMe=true` em vez de `assignee=me`, para não misturar um texto com o `assigneeId` (UUID).
3. **Regras de etiquetas unificadas em `Labels`:** valem para projetos e tarefas. A mensagem de limite passou a ser "São permitidas no máximo 20 etiquetas".
4. **Chave alternativa `(organization_id, project_id, user_id)` em `project_members`:** é o alvo da FK dos responsáveis e substituiu o índice simples `(organization_id, project_id)`, que ficou redundante.
5. **Teste de download por membro da organização fora da equipe:** acrescentado quando a mutação no download mostrou que o filtro de organização sozinho bastava para os testes entre organizações. Agora a remoção do `[TaskPermission]` no download é detectada.
6. **Responsáveis atribuídos no mesmo instante:** a ordem é desempatada pelo nome, e o defeito apareceu num teste. O caminho de desenvolvimento dos anexos passou a ser `%LOCALAPPDATA%/Gestalyn/storage`.
7. **Docker parado no início da validação:** os testes de integração falharam ao conectar. O Docker Desktop foi iniciado, e os testes rodaram em seguida.
