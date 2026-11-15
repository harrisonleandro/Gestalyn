# API: atividades e auditoria

Contratos da Fase 08 ([ADR-014](../decisions/ADR-014-AUDITORIA-DOCUMENTOS-E-DASHBOARDS.md)). Todas as rotas exigem `Authorization: Bearer`. As rotas por projeto exigem também o cabeçalho **`X-Gestalyn-Organization`**. O documento OpenAPI completo fica em `/openapi/v1.json` (Development).

## Como as atividades são registradas

- **Eventos (`Event`):** toda rota de escrita da API gera exatamente um evento, na mesma gravação da alteração. Uma operação recusada (400, 403, 404 ou 409) não gera evento.
  - Exceções: as rotas de autenticação, cujos eventos ficam nos logs estruturados, e o próprio registro manual.
- **Registros manuais (`Log`):** notas e reuniões registradas por alguém da equipe.
- **Imutabilidade:** eventos e registros manuais não podem ser alterados nem excluídos, nem pela API nem por SQL direto. Uma correção é um novo registro.

| Campo | Origem |
|---|---|
| `actorId`, `actorName` | Do token (`sub`), nunca do corpo da requisição |
| `actorKind` | `User` (pessoa) ou `Agent` (Fase 10) |
| `origin` | Do cabeçalho `X-Gestalyn-Client`: `web` vira `Web`, `cli` vira `Cli` e `mcp` vira `Mcp`. Sem o cabeçalho, ou com outro valor, `Api`. `System` é reservado a operações internas. **É declarada pelo cliente: informativa, nunca concede acesso.** |
| `requestId` | ID da requisição, para correlacionar com os logs |

## Rotas

| Método e rota | Permissão | Respostas |
|---|---|---|
| `GET /projects/{projectId}/activities` | `project.read` | **200** `PagedResult<ActivityItem>` · 400 · 404 |
| `POST /projects/{projectId}/activities` | `documents.write` | **201** `ActivityItem` · 400 · 403 · 404 (tarefa de outro projeto) · 409 (projeto arquivado) |
| `GET /organizations/{organizationId}/activities` | `activity.read_all` (Proprietário e Administrador) | **200** `PagedResult<ActivityItem>` · 403 · 404 |

**Filtros** (query string): `actorId`, `actorKind`, `origin`, `action`, `entityType`, `taskId`, `from`, `to` (data e hora com fuso), `source` (`Event` ou `Log`), `page` e `pageSize` (até 100). Na rota da organização, também `projectId`.

- A lista vem em ordem decrescente de `occurredAt`.
- `action` e `entityType` só existem nos eventos: usá-los exclui os registros manuais.

**`ActivityItem`:** `{ id, source, projectId, taskId, actorId, actorName, actorKind, origin, requestId, action, entityType, entityId, kind, summary, details, changes, occurredAt, recordedAt }`.

- **Eventos** trazem `action`, `entityType`, `entityId` e `changes`.
- **Registros manuais** trazem `kind`, `details` e `recordedAt`.

**`changes`:** as alterações campo a campo, com os nomes do contrato.

- `{ "campo": { "from": …, "to": … } }` para valores simples;
- `{ "description": { "changed": true } }` para textos longos (descrições, corpos de documentos e comentários, contexto e resultado das decisões), **sem o conteúdo**;
- `{ "labels": { "added": [...], "removed": [...] } }` para listas.

**Registro manual:** `{ "kind": "Meeting", "summary": "Revisão com o cliente", "details": "Ata em Markdown", "taskId": "…", "occurredAt": "2026-11-14T15:00:00Z" }`.

- `kind` aceita `Note`, `Meeting` ou `Other`.
- `summary` é obrigatório, com até 500 caracteres; `details` tem até 10 mil caracteres.
- `occurredAt` é opcional (agora, se ausente) e não pode estar no futuro.

## Catálogo de ações

| Área | Ações |
|---|---|
| Organização | `organization.created`, `organization.updated`, `member.invited`, `invitation.revoked`, `invitation.accepted`, `member.role_changed`, `member.removed` |
| Projeto | `project.created`, `project.updated` (inclui etiquetas), `project.archived`, `project.member_added`, `project.member_updated`, `project.member_removed`, `project.repository_added`, `project.repository_removed` |
| Planejamento | `phase.created`, `phase.updated`, `phase.reordered`, `phase.deleted`, `milestone.created`, `milestone.updated`, `milestone.deleted` |
| Tarefa | `task.created`, `task.updated`, `task.archived`, `task.assignee_added`, `task.assignee_removed`, `task.dependency_added`, `task.dependency_removed`, `task.transitioned`, `task.comment_added`, `task.comment_edited`, `task.comment_deleted`, `task.attachment_added`, `task.attachment_deleted` |
| Workflow | `workflow.updated`, `workflow.state_added`, `workflow.state_updated`, `workflow.state_moved`, `workflow.state_deleted`, `workflow.transition_added`, `workflow.transition_removed` |
| Documentos e decisões | `document.created`, `document.updated`, `document.archived`, `document.restored`, `decision.proposed`, `decision.updated`, `decision.accepted`, `decision.rejected`, `decision.superseded` |

Tipos de entidade (`entityType`): `organization`, `member`, `invitation`, `project`, `project_member`, `repository`, `phase`, `milestone`, `task`, `comment`, `attachment`, `workflow`, `workflow_state`, `workflow_transition`, `document` e `decision`.

- **Membros:** o `entityId` de `member` e de `project_member` é o ID do usuário.
- **Convites:** aparecem pelo ID e pelo papel, nunca pelo e-mail.
