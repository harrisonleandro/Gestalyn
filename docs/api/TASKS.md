# API: fases, marcos e tarefas

Contratos da Fase 06 ([ADR-011](../decisions/ADR-011-TAREFAS-E-ANEXOS.md)). Todas as rotas exigem `Authorization: Bearer` e o cabeçalho **`X-Gestalyn-Organization`** ([PROJECTS.md](PROJECTS.md)). O documento OpenAPI completo fica em `/openapi/v1.json` (Development).

## Acesso

- As rotas `/projects/{projectId}/...` usam o acesso ao projeto, e as rotas `/tasks/{taskId}/...`, o acesso ao projeto da tarefa.
- Projeto ou tarefa invisível responde 404; com leitura, mas sem a permissão, 403; tarefa ou projeto arquivado (alterações) e conflitos de concorrência ou unicidade, 409.

| Permissão | Gerente | Colaborador | Observador |
|---|---|---|---|
| `project.read`: ler fases, marcos, tarefas, comentários e anexos | sim | sim | sim |
| `project.plan`: gerenciar fases e marcos | sim | não | não |
| `tasks.write`: criar e editar tarefas, responsáveis, dependências e anexos | sim | sim | não |
| `tasks.comment`: comentar (só o autor edita) | sim | sim | não |
| `tasks.archive`: arquivar tarefas e excluir comentários de outros | sim | não | não |

## Fases e marcos

| Método e rota | Permissão | Respostas |
|---|---|---|
| `GET /projects/{projectId}/phases` | `project.read` | **200** `PhaseDetails[]`, em ordem |
| `POST /projects/{projectId}/phases` | `project.plan` | **201** `PhaseDetails` (no fim da ordem) · 400 |
| `PATCH /projects/{projectId}/phases/{phaseId}` | `project.plan` | **200** · 400 · 404 |
| `PUT /projects/{projectId}/phases/order` | `project.plan` | **200** `PhaseDetails[]` · 400 (lista incompleta ou repetida) |
| `DELETE /projects/{projectId}/phases/{phaseId}` | `project.plan` | **204** · 409 (há tarefas na fase) |
| `GET /projects/{projectId}/milestones` | `project.read` | **200** `MilestoneDetails[]` (por data prevista) |
| `POST /projects/{projectId}/milestones` | `project.plan` | **201** `MilestoneDetails` · 404 (fase de outro projeto) |
| `PATCH /projects/{projectId}/milestones/{milestoneId}` | `project.plan` | **200** |
| `DELETE /projects/{projectId}/milestones/{milestoneId}` | `project.plan` | **204** · 409 (há tarefas no marco) |

**Corpos:**

- Fase: `{ "name": "Construção", "description": null, "startDate": "2027-01-01", "endDate": "2027-03-31" }`. O fim precisa ser igual ou posterior ao início.
- Reordenar: `{ "phaseIds": ["…", "…"] }`, com todas as fases do projeto, cada uma uma vez.
- Marco: `{ "name": "Beta", "dueDate": "2027-03-31", "phaseId": "…" }`.
- No `PATCH` de fase ou marco, `null` limpa a descrição, as datas e a fase, e omitir o campo o mantém. No marco, `isReached: true` ou `false` marca ou desmarca o marco como atingido.

## Tarefas

| Método e rota | Permissão | Respostas |
|---|---|---|
| `POST /projects/{projectId}/tasks` | `tasks.write` | **201** `TaskDetails` · 400 · 404 (fase, marco ou responsável inválido) · 409 |
| `GET /projects/{projectId}/tasks` | `project.read` | **200** `PagedResult<TaskSummary>` · 400 |
| `GET /tasks/{taskId}` | `project.read` | **200** `TaskDetails` |
| `PATCH /tasks/{taskId}` | `tasks.write` | **200** `TaskDetails` · 400 · 404 · 409 |
| `DELETE /tasks/{taskId}` (arquivar) | `tasks.archive` | **204** (também se já arquivada) |
| `POST /tasks/{taskId}/assignments` (`{ userId }`) | `tasks.write` | **200** `TaskDetails` · 404 (fora da equipe) · 409 (já é responsável) |
| `DELETE /tasks/{taskId}/assignments/{userId}` | `tasks.write` | **204** · 404 |
| `POST /tasks/{taskId}/dependencies` (`{ dependsOnTaskId }`) | `tasks.write` | **200** `TaskDetails` · 400 (consigo mesma ou outro projeto) · 409 (duplicada, ciclo ou tarefa arquivada) |
| `DELETE /tasks/{taskId}/dependencies/{dependsOnTaskId}` | `tasks.write` | **204** · 404 |

**Criar:**

```json
{
  "title": "Tela de login",
  "description": "Texto em Markdown, até 20.000 caracteres",
  "priority": "High",
  "dueDate": "2027-02-15",
  "phaseId": "…",
  "milestoneId": "…",
  "labels": ["Web", "Segurança"],
  "assigneeIds": ["…"]
}
```

- `priority`: `Low`, `Medium` (padrão), `High` ou `Critical`.
- Até 20 etiquetas e 10 responsáveis, todos da equipe do projeto.
- O número da tarefa (`number`) é sequencial no projeto e nunca muda.

**Alterar** (`PATCH /tasks/{taskId}`): só os campos enviados mudam. Em `description`, `dueDate`, `phaseId` e `milestoneId`, `null` limpa o valor e omitir o campo o mantém. `labels` substitui o conjunto inteiro.

**Listar:** parâmetros de consulta.

| Parâmetro | Efeito |
|---|---|
| `page`, `pageSize` | Paginação (padrão 50, máximo 100) |
| `phaseId`, `milestoneId`, `assigneeId` | Tarefas da fase, do marco ou do responsável |
| `assignedToMe=true` | Tarefas do usuário autenticado |
| `label`, `priority` | Etiqueta (sem diferenciar maiúsculas e minúsculas) e prioridade |
| `dueBefore`, `dueAfter` | Prazo anterior ou posterior à data (exclusivo) |
| `overdue=true` | Prazo anterior a hoje (UTC) |
| `search` | Texto no título, sem diferenciar maiúsculas e minúsculas |
| `includeArchived=true` | Inclui as arquivadas |
| `sortBy`, `sortDirection` | `Number` (padrão), `DueDate` (sem prazo por último), `Priority` ou `UpdatedAt`; `Asc` (padrão) ou `Desc` |

A resposta tem o formato `{ items, page, pageSize, totalCount }`. Cada item traz `id`, `number`, `title`, `priority`, `dueDate`, `phaseId`, `milestoneId`, `labels`, `assigneeIds`, `archivedAt` e `updatedAt`.

**Detalhes:** os campos da listagem, mais:
- `projectId`, `description`, `createdByUserId`, `archivedByUserId`, `createdAt` e `myPermissions`;
- `assignees`: `{ userId, displayName, assignedAt }`;
- `dependsOn`: as tarefas das quais esta depende;
- `blocks`: as tarefas que dependem desta.

Nas dependências, cada tarefa aparece como `{ id, number, title, archivedAt }`.

## Comentários

| Método e rota | Permissão | Respostas |
|---|---|---|
| `GET /tasks/{taskId}/comments` | `project.read` | **200** `CommentDetails[]` (em ordem de criação) |
| `POST /tasks/{taskId}/comments` (`{ body }`) | `tasks.comment` | **201** `CommentDetails` · 400 |
| `PATCH /tasks/{taskId}/comments/{commentId}` (`{ body }`) | autor, com `tasks.comment` | **200** · 403 (não é o autor) |
| `DELETE /tasks/{taskId}/comments/{commentId}` | autor, ou `tasks.archive` | **204** · 403 |

`CommentDetails`: `{ id, authorUserId, authorDisplayName, body, createdAt, editedAt }`. O texto vai até 10.000 caracteres.

## Anexos

| Método e rota | Permissão | Respostas |
|---|---|---|
| `GET /tasks/{taskId}/attachments` | `project.read` | **200** `AttachmentDetails[]` |
| `POST /tasks/{taskId}/attachments` (`multipart/form-data`, campo `file`) | `tasks.write` | **201** `AttachmentDetails` · 400 (tipo, conteúdo, tamanho ou limite) · 409 |
| `GET /tasks/{taskId}/attachments/{attachmentId}/content` | `project.read` | **200** o arquivo, como anexo |
| `DELETE /tasks/{taskId}/attachments/{attachmentId}` | `tasks.write` | **204** · 404 · 409 |

- Até 10 MB por arquivo e 20 por tarefa.
- Tipos permitidos: PNG, JPEG, GIF, WebP, PDF, DOCX, XLSX, PPTX, TXT, LOG, MD, CSV e JSON. O conteúdo precisa corresponder à extensão.
- `AttachmentDetails`: `{ id, fileName, contentType, sizeBytes, sha256, uploadedByUserId, createdAt }`. O `fileName` é o nome enviado, depois de limpo.
- O download responde com `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff` e `Content-Security-Policy: default-src 'none'; sandbox`.
