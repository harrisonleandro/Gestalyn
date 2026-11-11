# API: autenticação (`/api/v1/auth`)

Contratos da Fase 03. Os endpoints de confirmação, reenvio e recuperação de senha complementam a lista da especificação mestre (seção 5.1), conforme previsto em "ajustar os contratos quando necessário". O documento OpenAPI completo fica em `/openapi/v1.json` (Development).

- **Formato:** JSON (camelCase), datas em UTC (ISO 8601).
- **Erros:** Problem Details (`application/problem+json`, RFC 9457) com `traceId`.
- **Rate limiting:** todos os endpoints abaixo têm limite por IP. Ao exceder, a resposta é **429**.
- **Autenticação:** endpoints marcados com 🔒 exigem `Authorization: Bearer <accessToken>`.

| Método e rota | Corpo | Respostas |
|---|---|---|
| `POST /register` | `{ email, password, displayName }` | **202** (também para e-mail já cadastrado, que recebe um aviso por e-mail) · **400** validação |
| `POST /confirm-email` | `{ userId, token }` (do link do e-mail) | **204** · **400** link inválido ou expirado |
| `POST /resend-confirmation` | `{ email }` | **202** sempre |
| `POST /login` | `{ email, password }` | **200** tokens · **401** credenciais inválidas ou conta bloqueada (mensagem neutra) · **403** e-mail não confirmado (só com senha correta) |
| `POST /refresh` | `{ refreshToken }` | **200** novos tokens (o refresh token anterior deixa de valer) · **401** |
| 🔒 `POST /logout` | — | **204** (sessão revogada: o access token e o refresh token deixam de valer imediatamente) |
| 🔒 `GET /me` | — | **200** perfil · **401** |
| `POST /forgot-password` | `{ email }` | **202** sempre |
| `POST /reset-password` | `{ email, token, newPassword }` | **204** (todas as sessões revogadas) · **400** link inválido ou validação |

## Tokens (`200` de `login` e `refresh`)

```json
{
  "accessToken": "eyJ...",
  "accessTokenExpiresAt": "2026-11-11T12:15:00+00:00",
  "refreshToken": "q8T0...",
  "refreshTokenExpiresAt": "2026-12-11T12:00:00+00:00",
  "tokenType": "Bearer"
}
```

- **Access token:** JWT de 15 minutos com `sub` (usuário) e `sid` (sessão).
- **Refresh token:** uso único. Guarde o novo token a cada renovação. Reapresentar um token já trocado (fora da janela de 30 s para requisições concorrentes) **encerra a sessão**.
- **`refreshTokenExpiresAt`:** expiração absoluta da sessão; a renovação não a estende.

## Perfil (`200` de `GET /me`)

```json
{ "id": "0199...", "email": "pessoa@exemplo.com", "displayName": "Pessoa", "emailConfirmed": true, "createdAt": "2026-11-11T12:00:00+00:00" }
```

## Regras de entrada

| Campo | Regra |
|---|---|
| `email` | Formato válido, até 256 caracteres; único |
| `password` / `newPassword` | Entre 12 e 128 caracteres, sem regras de composição |
| `displayName` | Obrigatório, até 100 caracteres |
