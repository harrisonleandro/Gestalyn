# ADR-006: Autenticação, sessão e tokens

- **Status:** Aceito
- **Data:** 2026-11-11
- **Fase:** 03 (Identidade e autenticação). Decisões D1, D2, D3 e D5 aprovadas pelo responsável.

## Contexto

A especificação mestre exige ASP.NET Core Identity, autenticação segura, tokens com expiração, revogação de credenciais, rate limiting, autorização no backend e que credenciais completas nunca sejam enviadas à interface Web. Ela define os endpoints `/api/v1/auth/{register,login,refresh,logout,me}` e o critério "logout invalida a sessão". A mesma API servirá a Web, a CLI (Fase 11) e, com credenciais próprias, os agentes (Fase 10).

## Decisão

### Identidade (D3)

- **ASP.NET Core Identity** com `ApplicationUser : IdentityUser<Guid>` (UUID v7) na Infrastructure, usando `IdentityUserContext` no `ProjectManagerDbContext`. Não há tabelas de papéis do Identity: papéis e permissões são por organização e projeto (Fase 04).
- Tabelas `users`, `user_claims`, `user_logins` e `user_tokens`, com **e-mail único também no banco**.
- O Domain não depende do Identity.

### Tokens e sessão da API (D1)

| Elemento | Decisão |
|---|---|
| Access token | JWT HS256, 15 min, claims `sub` (usuário), `sid` (sessão) e `jti` |
| Refresh token | 256 bits aleatórios (Base64Url). No banco fica **somente o hash SHA-256**. Uso único, com rotação a cada renovação. |
| Sessão | Tabela `user_sessions`, com expiração **absoluta** de 30 dias (a renovação não estende) |
| Validação | Além de assinatura, emissor, público e validade, **cada requisição confere se a sessão está ativa**. O logout revoga a sessão e o mesmo access token passa a receber 401 imediatamente. |
| Reuso | Reapresentar um refresh token já trocado **revoga a sessão inteira** (indício de roubo). ~~Exceção: dentro de uma janela de 30 s, responde só 401, sem revogar.~~ **Substituído pelo [ADR-012](ADR-012-ROTACAO-IDEMPOTENTE-DE-REFRESH-TOKEN.md):** dentro de 10 s, e só se o sucessor ainda não foi usado, a troca é repetida e devolve o mesmo sucessor. Qualquer outro reuso revoga a sessão. |
| Concorrência | Dois usos simultâneos do mesmo refresh token: o `xmin` garante que só um vence |
| Chave | Mínimo de 256 bits, validado na inicialização; nunca versionada (user-secrets ou variável de ambiente) |

### Sessão da Web (D2)

- A Web funciona como **BFF**. Os formulários são SSR estáticos com antiforgery, e a Web chama a API.
- Os tokens ficam no **cookie de autenticação da Web**, criptografado pelo Data Protection, `HttpOnly`, `SameSite=Lax` e `Secure` fora de Development. O navegador nunca recebe tokens: verificado com `document.cookie` vazio e HTML sem JWT.
- A cada navegação, se o access token expira em menos de 1 min, a Web o renova. Requisições paralelas compartilham uma única renovação (`TokenRefreshCoordinator`). Se a API recusa o refresh token, a sessão da Web é encerrada.
- O logout é feito por POST com antiforgery: revoga a sessão na API e apaga o cookie.

### Confirmação de e-mail (D5)

O login só é permitido com e-mail confirmado. Os links de confirmação e de redefinição usam tokens do Data Protection válidos por 2 horas.

### Proteções

- **Mensagens neutras:** o login responde a mesma coisa para e-mail inexistente, senha errada e conta bloqueada. O cadastro de um e-mail existente responde igual a um cadastro novo e avisa o titular por e-mail. `forgot-password` responde sempre 202. Um hash fictício iguala o custo de tempo quando o e-mail não existe.
- **E-mail não confirmado:** só é informado **depois** de verificar a senha. Por isso o `UserManager` é usado diretamente, e não o `SignInManager`.
- **Lockout:** 5 falhas bloqueiam a conta por 15 min.
- **Rate limiting:** janela fixa por IP nos endpoints `/auth/*` (padrão: 10 por minuto), com 429 em Problem Details.
- **Senha:** mínimo de 12 caracteres, máximo de 128, sem regras de composição (NIST SP 800-63B); hash PBKDF2 do Identity.
- **Redefinição de senha:** revoga todas as sessões e remove o bloqueio.
- **Segurança por padrão:** controllers exigem autenticação; endpoints públicos usam `[AllowAnonymous]` explicitamente.
- **Logs:** nunca registram senhas, tokens ou e-mails (verificado por teste).

## Alternativas consideradas

| Alternativa | Motivo da rejeição |
|---|---|
| `MapIdentityApi` (bearer tokens nativos) | Access token não revogável, contrato diferente da especificação e pouco controle sobre a rotação |
| JWT sem sessão no servidor | O logout não invalidaria o access token até expirar |
| Cookies de autenticação na API | Não servem para CLI nem para agentes |
| Tokens no navegador (localStorage) | Expostos a XSS; contraria a seção 7 da especificação |
| `SignInManager` | Informa bloqueio ou e-mail não confirmado antes de verificar a senha, revelando contas existentes |

## Consequências

- A validação de sessão faz uma consulta por chave primária a cada requisição autenticada. Um cache curto pode ser avaliado na Fase 13.
- **Multi-instância da Web:** o coordenador de renovação é por processo. ~~A janela de tolerância da API cobre o restante.~~ (Incorreto: a instância que perdia a corrida recebia 401 e desconectava o usuário. Corrigido pelo [ADR-012](ADR-012-ROTACAO-IDEMPOTENTE-DE-REFRESH-TOKEN.md), em que as outras instâncias recebem o mesmo sucessor.) O Data Protection precisará de um repositório de chaves compartilhado (Fase 14).
- **Proxy reverso:** o rate limiting por IP exige forwarded headers configurados (Fase 14).
- **Interatividade:** componentes Interactive Server que chamem a API em nome do usuário precisarão de um provedor de token com escopo de circuito (Fase 09). Por enquanto, todas as chamadas autenticadas ocorrem em SSR.

## Revisão de 2026-11-14

A regra de reuso foi substituída pelo [ADR-012](ADR-012-ROTACAO-IDEMPOTENTE-DE-REFRESH-TOKEN.md), depois que a auditoria técnica mostrou que a janela de 30 s com 401 sem revogar permitia que um invasor que usasse o token primeiro mantivesse a sessão, enquanto a vítima era desconectada. As demais decisões deste ADR continuam válidas.

## Revisão de 2026-11-15 (Fase 10)

O [ADR-016](ADR-016-AGENTES.md) acrescenta a autenticação de agentes, sem mudar a de usuários. Três pontos tocam este ADR:

- **Esquemas e políticas:**
  - o JWT continua sendo o esquema padrão;
  - o esquema `AgentToken` é adicional;
  - o `AuthorizeFilter` global sem política dá lugar às políticas explícitas `User` (JWT com `sid`) e `Agent`. Um token de agente não é aceito em rotas de usuário.
- **Credenciais completas na Web:** este ADR repete a regra da seção 7 da especificação mestre. O responsável aprovou uma única exceção: o token de uma credencial de agente recém-criada aparece uma vez, na resposta da criação, com `Cache-Control: no-store`, sem ser guardado nem registrado. Os access e refresh tokens dos usuários continuam sem chegar ao navegador.
- **Hash rápido:** as credenciais de agentes usam o mesmo critério dos refresh tokens. São 256 bits aleatórios, e no banco fica só o SHA-256.
