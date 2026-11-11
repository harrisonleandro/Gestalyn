# API: projetos e equipes

Contratos da Fase 05 ([ADR-010](../decisions/ADR-010-PROJETOS-E-EQUIPES.md)). Todos exigem `Authorization: Bearer`. O documento OpenAPI completo fica em `/openapi/v1.json` (Development).

## Organização ativa

- Criação e listagem ficam sob a organização da rota (`/api/v1/organizations/{organizationId}/projects`).
- As rotas por ID do projeto (`/api/v1/projects/{projectId}/...`) exigem o cabeçalho **`X-Gestalyn-Organization: <id da organização>`**:
  - sem o cabeçalho, ou com valor inválido: **400**;
  - organização da qual o usuário não é membro, ou projeto de outra organização: **404**.

## Acesso

| Situação | Resposta |
|---|---|
| Não é membro da organização, ou o projeto é de outra organização | 404 |
| Membro sem `projects.read_all` e fora da equipe do projeto | 404 (o projeto não aparece) |
| Com acesso de leitura, mas sem a permissão do endpoint | 403 |
| Projeto arquivado (alterações) ou conflito de concorrência e unicidade | 409 |

Permissões efetivas no projeto:

| Origem | Permissões |
|---|---|
| `projects.manage_all` (Proprietário, Administrador) | todas |
| `projects.read_all` (Proprietário, Administrador) | `project.read` |
| Gerente na equipe | `project.read`, `project.update`, `project.archive`, `project.members.manage` |
| Colaborador na equipe | `project.read`, `project.update` |
| Observador na equipe | `project.read` |

## Endpoints

| Método e rota | Permissão | Respostas |
|---|---|---|
| `POST /organizations/{organizationId}/projects` | `projects.create` | **201** `ProjectDetails` (o criador vira Gerente) · 400 · 403 · 404 |
| `GET /organizations/{organizationId}/projects` | membro da organização | **200** `PagedResult<ProjectSummary>` · 400 · 404 |
| `GET /projects/{projectId}` | `project.read` | **200** `ProjectDetails` |
| `PATCH /projects/{projectId}` | `project.update` | **200** `ProjectDetails` · 400 · 409 |
| `DELETE /projects/{projectId}` (arquivar) | `project.archive` | **204** (também se já arquivado) |
| `GET /projects/{projectId}/members` | `project.read` | **200** `ProjectMemberDetails[]` |
| `POST /projects/{projectId}/members` | `project.members.manage` | **201** `ProjectMemberDetails` · 404 (pessoa fora da organização) · 409 (já na equipe) |
| `PATCH /projects/{projectId}/members/{userId}` | `project.members.manage` | **200** `ProjectMemberDetails` · 404 |
| `DELETE /projects/{projectId}/members/{userId}` | `project.members.manage` | **204** · 404 |
| `GET /projects/{projectId}/repositories` | `project.read` | **200** `ProjectRepositoryDetails[]` |
| `POST /projects/{projectId}/repositories` | `project.update` | **201** `ProjectRepositoryDetails` · 400 · 409 (já associado) |
| `DELETE /projects/{projectId}/repositories/{repositoryId}` | `project.update` | **204** · 404 |

## Corpos

**Criar** (`POST .../projects`):

```json
{
  "name": "Portal do cliente",
  "description": "Opcional, até 4.000 caracteres",
  "priority": "High",
  "startDate": "2026-12-01",
  "dueDate": "2027-03-31",
  "tags": ["Web", "Cliente"]
}
```

- `priority`: `Low`, `Medium` (padrão), `High` ou `Critical`.
- `dueDate` igual ou posterior a `startDate`.
- Até 20 etiquetas de 1 a 50 caracteres. Repetidas sem diferenciar maiúsculas e minúsculas são unificadas.

**Alterar** (`PATCH /projects/{projectId}`): só os campos enviados mudam.

- Em `description`, `startDate` e `dueDate`, enviar `null` limpa o valor; omitir o campo o mantém.
- `tags` substitui o conjunto inteiro, e `[]` limpa.

**Listar:** `?page=1&pageSize=50&tag=web&priority=High&includeArchived=false`. A ordem é por nome, e `pageSize` vai até 100.

```json
{ "items": [ { "id": "…", "name": "Portal", "priority": "High", "startDate": "2026-12-01", "dueDate": null,
  "tags": ["Cliente", "Web"], "archivedAt": null, "myRole": "Manager" } ], "page": 1, "pageSize": 50, "totalCount": 1 }
```

**Detalhes:** os campos da listagem, mais `description`, `responsible` (`{ userId, displayName }` ou `null`), `archivedByUserId`, `createdAt`, `updatedAt` e `myPermissions` (permissões efetivas do usuário).

**Equipe:**

- Adicionar: `POST .../members` com `{ "userId": "…", "role": "Contributor", "isResponsible": false }`, em que `role` é `Manager`, `Contributor` ou `Viewer`.
- Alterar: `PATCH .../members/{userId}` com `{ "role": "Viewer", "isResponsible": true }` (os dois campos são opcionais).
- Há no máximo um responsável por projeto: marcar outro desmarca o anterior.

**Repositório:** `{ "url": "https://github.com/org/repo", "name": "Opcional" }`.

- A URL é normalizada: host em minúsculas, sem `.git` e sem barra final.
- Só `https`, sem credenciais, parâmetros de consulta ou fragmento.
- `provider` é inferido: `GitHub`, `GitLab`, `Bitbucket` ou `Other`.
