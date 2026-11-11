# API: organizações, membros e convites

Contratos da Fase 04 (especificação mestre, seção 5.2, complementados com membros, papéis, revogação e aceitação). Todos os endpoints exigem `Authorization: Bearer <accessToken>` ([AUTH.md](AUTH.md)).

- **Formato:** JSON (camelCase), datas em UTC (ISO 8601).
- **Erros:** Problem Details (`application/problem+json`).
- **Organização ativa:** rotas com `{organizationId}` só valem para membros dessa organização. Para quem não é membro, a resposta é **404**, sem revelar que a organização existe.

## Organizações

| Método e rota | Permissão | Respostas |
|---|---|---|
| `POST /api/v1/organizations` · `{ name }` | autenticado | **201** `OrganizationSummary` (o criador vira Proprietário) · 400 |
| `GET /api/v1/organizations` | autenticado | **200** `OrganizationSummary[]` (somente as organizações do usuário, com o papel dele) |
| `GET /api/v1/organizations/{organizationId}` | `organization.read` | **200** `OrganizationDetails` · 404 |
| `PATCH /api/v1/organizations/{organizationId}` · `{ name }` | `organization.update` | **200** · 403 · 404 · 409 (alteração concorrente) |

## Membros e papéis

| Método e rota | Permissão | Respostas |
|---|---|---|
| `GET .../{organizationId}/members` | `members.read` | **200** `MemberDetails[]` |
| `PATCH .../{organizationId}/members/{userId}` · `{ roleId }` | `members.update_role` | **200** `MemberDetails` · 403 (só proprietários mexem com proprietários) · 404 · 409 (último proprietário) |
| `DELETE .../{organizationId}/members/{userId}` | `members.remove`, ou o próprio usuário (sair) | **204** · 403 · 404 · 409 (último proprietário) |
| `GET .../{organizationId}/roles` | `members.read` | **200** `RoleDetails[]` (Proprietário, Administrador, Membro, Leitor e permissões) |

## Convites

| Método e rota | Permissão | Respostas |
|---|---|---|
| `POST .../{organizationId}/invitations` · `{ email, roleId }` | `members.invite` (convidar como Proprietário exige ser Proprietário) | **201** `InvitationDetails` e e-mail enviado · 403 · 404 (papel inexistente nesta organização) · 409 (já é membro ou já há convite pendente) |
| `GET .../{organizationId}/invitations` | `members.invite` | **200** `InvitationDetails[]` com status `Pendente`, `Aceito`, `Revogado` ou `Expirado` |
| `DELETE .../{organizationId}/invitations/{invitationId}` | `members.invite` | **204** · 404 · 409 (já aceito) |
| `POST /api/v1/invitations/accept` · `{ token }` | autenticado, com e-mail confirmado igual ao convidado | **200** `AcceptedInvitation` · 400 (inválido, expirado, revogado ou já usado) · 403 (outro e-mail) · 409 (já é membro) |

O token do convite chega por e-mail, em um link para a Web (`/convites/aceitar?token=...`). Ele vale 7 dias e é de uso único.

## Modelos

```json
// OrganizationSummary
{ "id": "0199...", "name": "Acme", "roleId": "0199...", "roleName": "Proprietário" }

// MemberDetails
{ "userId": "0199...", "displayName": "Ana", "email": "ana@exemplo.com", "roleId": "0199...", "roleName": "Membro", "joinedAt": "2026-11-11T12:00:00+00:00" }

// RoleDetails
{ "id": "0199...", "name": "Administrador", "systemRole": "Administrator", "permissions": ["members.invite", "members.read", "..."] }

// InvitationDetails
{ "id": "0199...", "email": "ana@exemplo.com", "roleId": "0199...", "roleName": "Membro", "status": "Pendente", "expiresAt": "...", "createdAt": "..." }
```
