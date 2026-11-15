# API: documentos e decisões

Contratos da Fase 08 ([ADR-014](../decisions/ADR-014-AUDITORIA-DOCUMENTOS-E-DASHBOARDS.md)). Todas as rotas exigem `Authorization: Bearer` e o cabeçalho **`X-Gestalyn-Organization`**. O documento OpenAPI completo fica em `/openapi/v1.json` (Development).

## Acesso

| Permissão | Gerente | Colaborador | Observador |
|---|---|---|---|
| `project.read`: ler documentos, versões e decisões | sim | sim | sim |
| `documents.write`: criar e editar documentos; propor e editar decisões; registrar atividades | sim | sim | não |
| `documents.manage`: arquivar e restaurar documentos; aceitar, rejeitar e substituir decisões | sim | não | não |

- **Rotas:** `/projects/{projectId}/...` usam o acesso ao projeto; `/documents/{id}` e `/decisions/{id}` usam o acesso ao projeto do recurso.
- **Respostas:**
  - recurso invisível: 404;
  - sem a permissão: 403;
  - regra violada, versão desatualizada ou projeto arquivado: 409.

## Documentos

| Método e rota | Permissão | Respostas |
|---|---|---|
| `GET /projects/{projectId}/documents?includeArchived=` | `project.read` | **200** `DocumentSummary[]`, por título |
| `POST /projects/{projectId}/documents` | `documents.write` | **201** `DocumentDetails` · 400 · 403 · 409 |
| `GET /documents/{documentId}` | `project.read` | **200** `DocumentDetails` · 404 |
| `PUT /documents/{documentId}` | `documents.write` | **200** `DocumentDetails` · 400 · 403 · 404 · 409 |
| `GET /documents/{documentId}/versions` | `project.read` | **200** `DocumentVersionSummary[]`, da mais nova para a mais antiga |
| `GET /documents/{documentId}/versions/{number}` | `project.read` | **200** `DocumentVersionDetails` · 404 |
| `POST /documents/{documentId}/archive` · `/restore` | `documents.manage` | **200** `DocumentDetails` · 403 · 409 |

- **Criar:** `{ "title": "Visão do produto", "body": "# Visão\n..." }`. O título tem até 200 caracteres; o corpo é Markdown, com até 200 mil caracteres, e pode começar vazio.
- **Editar:** `{ "title": "…", "body": "…", "expectedVersion": 3 }`.
  - Toda edição cria uma versão nova, imutável, com o autor e a data.
  - `expectedVersion` é obrigatório: se outra pessoa já salvou uma versão depois da que você abriu, a resposta é 409 e nada é sobrescrito.
- **Arquivado:** o documento sai da listagem padrão e fica somente leitura até ser restaurado.
- **Conteúdo:** o Markdown é guardado como veio. Quem exibe o documento é responsável por renderizá-lo com sanitização; a Web (Fase 09) não executa HTML do documento.

**`DocumentDetails`:** `{ id, projectId, title, body, currentVersion, createdById, archivedAt, archivedById, createdAt, updatedAt }`.

## Decisões

| Método e rota | Permissão | Respostas |
|---|---|---|
| `GET /projects/{projectId}/decisions?status=` | `project.read` | **200** `DecisionSummary[]`, mais novas primeiro |
| `POST /projects/{projectId}/decisions` | `documents.write` | **201** `DecisionDetails` · 400 · 403 · 404 (tarefa de outro projeto) · 409 |
| `GET /decisions/{decisionId}` | `project.read` | **200** `DecisionDetails` · 404 |
| `PATCH /decisions/{decisionId}` | `documents.write` | **200** `DecisionDetails` · 400 · 403 · 409 (já decidida) |
| `POST /decisions/{decisionId}/accept` · `/reject` | `documents.manage` | **200** `DecisionDetails` · 403 · 409 |
| `POST /decisions/{decisionId}/supersede` | `documents.manage` | **200** `DecisionDetails` · 400 · 403 · 404 · 409 |

**Estados:** `Proposed` → `Accepted` ou `Rejected`; `Accepted` → `Superseded`.

- Só a proposta pode ser editada. Aceita, rejeitada ou substituída, a decisão é imutável.
- Substituir exige `{ "supersededById": "…" }`: outra decisão **aceita** do mesmo projeto, diferente da própria.

**Corpos:**

- **Propor:** `{ "title": "Banco de dados", "context": "…", "outcome": "PostgreSQL", "alternatives": "…", "consequences": "…", "taskId": "…" }`.
  - `title` (até 200 caracteres), `context` e `outcome` são obrigatórios.
  - Os textos têm até 20 mil caracteres cada.
  - `taskId` é opcional e precisa ser do mesmo projeto.
- **Editar a proposta:** os mesmos campos, todos opcionais. `alternatives`, `consequences` e `taskId` aceitam `null` para limpar.

**`DecisionDetails`:** `{ id, projectId, title, context, outcome, alternatives, consequences, taskId, status, proposedById, decidedById, decidedAt, supersededById, createdAt, updatedAt }`.
