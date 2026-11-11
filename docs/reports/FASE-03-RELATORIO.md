# Relatório final: Fase 03, Identidade e autenticação

- **Data:** 2026-11-11
- **Ambiente:** Windows 11 Pro · .NET SDK 10.0.404 · Docker 29.8.2 · PostgreSQL 18 · Mailpit v1.29
- **Branch:** `feature/fase-03-identidade` (a partir de `develop`, `a8dae04`)
- **Estado:** **In Review.** Critérios verificados localmente (testes, navegador e mutação); a execução no CI do GitHub depende do push.
- **Formato:** seção 17 da [especificação mestre](../specifications/ESPECIFICACAO-MESTRE.md)

## 1. Nome e número da fase

Fase 03: Identidade e autenticação. A especificação da fase está em [FASE-03-IDENTIDADE.md](../specifications/FASE-03-IDENTIDADE.md).

## 2. Objetivo

Permitir cadastro, autenticação, sessão, logout e recuperação de senha pela API e pela Web, com proteção contra abuso e testes de segurança.

## 3. Resumo das implementações

- **Identidade (D3):** ASP.NET Core Identity com `ApplicationUser` (UUID v7) no `ProjectManagerDbContext`, tabelas em `snake_case` e e-mail único também no banco.
- **Tokens e sessão (D1):**
  - Access token JWT HS256 de 15 min com `sid`; refresh token opaco de uso único, guardado só como hash.
  - Sessão no servidor com expiração absoluta de 30 dias.
  - Logout e redefinição de senha revogam a sessão imediatamente; reuso de refresh token revoga a sessão, com janela de 30 s para concorrência.
- **API:** `/api/v1/auth` (register, confirm-email, resend-confirmation, login, refresh, logout, me, forgot-password, reset-password), `AuthorizeFilter` global, rate limiting por IP e Problem Details em todas as falhas.
- **Web (D2):** BFF com cookie HttpOnly guardando os tokens no servidor e renovação automática coordenada. Páginas de cadastro, login, confirmação, reenvio, "esqueci a senha", redefinição e perfil protegido; logout por POST com antiforgery.
- **E-mail (D4):** SMTP com MailKit; Mailpit no `docker-compose.yml` e nos testes.
- **Confirmação (D5):** obrigatória antes do login.
- **Proteções:**
  - respostas neutras (login, cadastro e recuperação) e hash fictício no login;
  - lockout de 5 falhas por 15 min;
  - senha entre 12 e 128 caracteres;
  - logs sem segredos.
- **Documentação:** ADR-006, ADR-007, `docs/api/AUTH.md`, `docs/security/AUTENTICACAO.md`, README, AGENTS.md, ARCHITECTURE.md, PROJECT_STRUCTURE.md, especificação e status.

## 4. Arquivos criados

| Área | Arquivos |
|---|---|
| Application | `Abstractions/Authentication/{IAuthenticationService,AuthResult,AuthContracts,AuthClaimTypes}.cs`, `Abstractions/Email/IEmailSender.cs` |
| Infrastructure | `Identity/{ApplicationUser,UserSession,UserRefreshToken,RefreshTokens,JwtOptions,AccessTokenIssuer,SessionManager,AccountLinkOptions,AccountEmails,PortugueseIdentityErrorDescriber,AuthenticationService}.cs`, `Email/{EmailOptions,SmtpEmailSender}.cs`, `Persistence/Configurations/IdentityConfigurations.cs`, migration `20261111011700_AddIdentityAndSessions` |
| API | `Authentication/{ApiAuthentication,AuthRateLimiting}.cs`, `Controllers/AuthController.cs` |
| Web | `Api/{ApiAccountClient,ApiAccountModels}.cs`, `Authentication/{AccountSession,TokenRefreshCookieEvents,TokenRefreshCoordinator,AccountEndpoints,ReturnUrls}.cs`, `Components/Pages/Account/*` (7 páginas e formulários), `Components/Shared/ApiErrors.razor` |
| Testes | `UnitTests/Authentication/TokenTests.cs`; `IntegrationTests/Authentication/{AuthTestClient,RegistrationAndLoginTests,SessionTests,PasswordRecoveryTests,AuthSecurityTests}.cs`; `IntegrationTests/Infrastructure/{MailpitFixture,TestLogCollector}.cs` |
| Documentação | `docs/specifications/FASE-03-IDENTIDADE.md`, `docs/decisions/ADR-006-*.md`, `ADR-007-*.md`, `docs/api/AUTH.md`, `docs/security/AUTENTICACAO.md`, este relatório |

## 5. Arquivos modificados

| Arquivo | Alteração |
|---|---|
| `Directory.Packages.props` | Pacotes de identidade e e-mail |
| `docker-compose.yml` | Serviço Mailpit |
| `src/ProjectManager.Infrastructure/{DependencyInjection.cs,*.csproj}` | Identity, opções validadas, e-mail; `FrameworkReference` do ASP.NET Core; `InternalsVisibleTo` dos testes unitários |
| `src/ProjectManager.Infrastructure/Persistence/ProjectManagerDbContext.cs` | Base `IdentityUserContext`; sessões e refresh tokens |
| `src/ProjectManager.API/{Program.cs,*.csproj,appsettings*.json}` | Autenticação, autorização padrão, rate limiting, configuração |
| `src/ProjectManager.Web/{Program.cs,Components/*,wwwroot/app.css}` | Cookie BFF, rotas com autorização, menu e cabeçalho, estilos de conta |
| `tests/ProjectManager.IntegrationTests/{Infrastructure/ApiFactory.cs,Api/ThrowingTestController.cs}` | Configuração de autenticação e e-mail; controller de teste anônimo |
| `README.md`, `AGENTS.md`, `docs/architecture/*`, `docs/PROJECT_STATUS.md`, `docs/specifications/README.md` | Documentação da fase |

## 6. Dependências adicionadas

| Pacote ou ferramenta | Versão | Projeto |
|---|---|---|
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.15 | Infrastructure |
| Microsoft.IdentityModel.JsonWebTokens | 8.19.2 | Infrastructure |
| MailKit | 4.18.2 | Infrastructure |
| Microsoft.AspNetCore.App (FrameworkReference) | 10.0 | Infrastructure |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.15 | API |
| Imagem `axllent/mailpit` | v1.29 | Compose e testes |

## 7. Justificativas técnicas

- **Identity EF Core:** decisão obrigatória da especificação. Usa `IdentityUserContext`, sem tabelas de papéis.
- **JsonWebTokens 8.19.2:** mesma versão que o JwtBearer 10.0.15 usa internamente, para não misturar versões do IdentityModel, uma causa conhecida de falhas em runtime.
- **MailKit:** cliente SMTP recomendado; o `System.Net.Mail.SmtpClient` é desaconselhado pela Microsoft.
- **FrameworkReference na Infrastructure:** os token providers e o Data Protection do Identity fazem parte do framework ASP.NET Core.
- **`UserManager` em vez de `SignInManager`:** permite verificar a senha antes de revelar bloqueio ou e-mail não confirmado (ADR-006).
- **Sessão no servidor:** único modo de cumprir "logout invalida a sessão" com access tokens JWT.

## 8. Testes executados

| Suíte | Testes | Verifica |
|---|---|---|
| `UnitTests/Authentication/TokenTests` | 5 | Refresh tokens (256 bits, Base64Url, únicos), hash, JWT com `sub`/`sid`/validade/HS256, chave mínima |
| `RegistrationAndLoginTests` | 6 | Fluxo completo com e-mail real; 403 antes da confirmação em `problem+json`; senha só como hash; e-mail existente com resposta igual e aviso ao titular; senha curta (400); token de confirmação inválido; `/me` sem token (401) |
| `SessionTests` | 7 | Logout imediato; rotação; reuso revoga a sessão; janela de concorrência não revoga; tokens expirado, forjado e adulterado recusados |
| `PasswordRecoveryTests` | 4 | Redefinição troca a senha e revoga sessões; token de uso único; e-mail desconhecido não recebe nada; token inválido (400) |
| `AuthSecurityTests` | 4 | Respostas idênticas; lockout; rate limiting (429 em problem+json); logs sem senhas nem tokens |
| Fases anteriores | 45 | Arquitetura, domínio, CLI, persistência, isolamento, health, OpenAPI, erros |

## 9. Resultado do build

```
dotnet restore                       → exit 0
dotnet build -c Release --no-restore → 0 Aviso(s), 0 Erro(s), exit 0
```

## 10. Resultado dos testes

```
Aprovado! – Com falha: 0, Aprovado: 28, Total: 28 - ProjectManager.UnitTests.dll (net10.0)
Aprovado! – Com falha: 0, Aprovado: 43, Total: 43 - ProjectManager.IntegrationTests.dll (net10.0)
```

O CI no GitHub Actions ainda **não foi executado** para esta branch (veja §14).

## 11. Evidências

**Navegador (Web real, API real, PostgreSQL e Mailpit locais):**

| Passo | Resultado |
|---|---|
| Cadastro | "Enviamos um e-mail para ..." e e-mail recebido no Mailpit |
| Login antes da confirmação | "Confirme o e-mail pelo link enviado ou solicite um novo." e link de reenvio |
| Link de confirmação | "E-mail confirmado. Sua conta está ativa." |
| `/conta/perfil` sem sessão | Redireciona para `/conta/entrar?ReturnUrl=%2Fconta%2Fperfil` |
| Login | Volta ao perfil com nome, e-mail (confirmado) e data vindos de `GET /me`; menu com Perfil e Sair; nome no cabeçalho |
| Tokens no navegador | `document.cookie` vazio (cookie HttpOnly) e HTML sem JWT |
| Logout | Volta ao início; `user_sessions.revocation_reason = Logout`; o perfil volta a exigir login |
| Esqueci a senha e redefinição | E-mail recebido; senha redefinida; login com a senha antiga: 401; com a nova: 200 |
| Renovação automática | Com access token de 65 s, a navegação seguinte renovou sozinha (a sessão passou a ter 2 refresh tokens, 1 consumido) e o perfil carregou sem novo login |

**Migration:** tabelas `users`, `user_claims`, `user_logins`, `user_tokens`, `user_sessions` e `user_refresh_tokens`; índices únicos `ix_users_normalized_email`, `ix_users_normalized_user_name` e `ix_user_refresh_tokens_token_hash`. Aplicada no banco local e no contêiner de testes.

**Mutações** (código restaurado depois de cada uma):

| Mutação | Resultado |
|---|---|
| Remover a verificação de sessão ativa (JwtBearer `OnTokenValidated`) | 3 testes falharam: logout, reuso e redefinição de senha |
| Recolocar `[Produces("application/json")]` no `AuthController` | O teste do fluxo de cadastro falhou: esperado `application/problem+json`, recebido `application/json` |

## 12. Problemas encontrados

1. Analisadores no código novo: CA1822 (`TokenType`), CA1000 (estáticos em tipo genérico) e CA1725 (nome de parâmetro diferente do Identity).
2. Os índices do Identity vieram com nomes fora do padrão e o de e-mail não era único no banco.
3. `dotnet ef migrations remove` tentou conectar ao banco pela factory de design-time, sem senha.
4. **Defeito:** a expiração da sessão devolvida no login (precisão de 100 ns) diferia da lida do banco (microssegundos).
5. **Defeito de projeto:** requisições paralelas do navegador poderiam reapresentar o mesmo refresh token, e a detecção de reuso revogaria a sessão de um usuário legítimo.
6. **Defeito:** o `[Produces("application/json")]` fazia os Problem Details saírem como `application/json`, e a Web mostrava uma mensagem genérica. Nenhum teste existente cobria isso.
7. Com a API em execução, o build não conseguia sobrescrever o executável, e um teste rodou sobre binários antigos. O resultado foi descartado.
8. O JSON do Mailpit escapa `&` como `&`, o que truncava o link extraído no shell.
9. Erros de sintaxe do `sed` com `#` e `|` nos padrões ao editar documentação.

## 13. Problemas corrigidos

1. `TokenType` virou parâmetro do record; as fábricas foram movidas para o tipo não genérico; o parâmetro foi renomeado para `builder`.
2. Índices únicos com nomes padronizados, configurados explicitamente.
3. Arquivos de migration ainda não aplicados foram removidos, o snapshot restaurado pelo Git e a migration gerada de novo.
4. `UserSession` trunca a expiração para microssegundos.
5. Janela de tolerância configurável de 30 s na API e renovação única por token na Web, com teste novo.
6. `[Produces]` removido; a Web também aceita `application/json`; teste reforçado e confirmado por mutação.
7. Servidores parados antes do build; teste executado de novo com os binários atualizados.
8. Link lido com um parser de JSON (Node).
9. Edições feitas com a ferramenta de edição.

## 14. Pendências

- **CI no GitHub:** push de `feature/fase-03-identidade`, execução do CI (Testcontainers com PostgreSQL e Mailpit) e PR para `develop`. Depende de autorização.
- **Fase 14:**
  - repositório compartilhado de chaves do Data Protection, necessário com mais de uma instância da Web ou da API;
  - forwarded headers para o rate limiting atrás de proxy;
  - provedor SMTP de produção.
- **Fase 09:** provedor de token com escopo de circuito para componentes Interactive Server que chamem a API.
- **Fase 08:** autor das alterações na auditoria.
- **Fase 13:** cache da validação de sessão; reavaliação de 2FA e passkeys.

## 15. Critérios atendidos

| Critério | Evidência |
|---|---|
| Usuário consegue se cadastrar | `Register_ConfirmEmail_Login_Me_CompletesTheFlow`; cadastro no navegador |
| Usuário consegue autenticar | Login pela API (200 com tokens) e pela Web |
| Sessões funcionam corretamente | Rotação, reuso, janela de concorrência, expiração e renovação automática na Web |
| Logout invalida a sessão | `Logout_InvalidatesAccessAndRefreshTokensImmediately`; sessão revogada no banco; mutação detectada |
| Rotas protegidas exigem autenticação | `/me` sem token: 401; `/conta/perfil` redireciona para o login |
| Recuperação de senha funciona com provedor configurado | SMTP real (Mailpit) nos testes e no navegador; senha antiga 401, nova 200 |
| Testes de segurança passam | Lockout, rate limiting, respostas neutras, tokens forjados, adulterados e expirados, logs sem segredos |
| Não armazenar senhas em texto puro | `Register_StoresOnlyThePasswordHash` (PBKDF2 do Identity) |

## 16. Critérios não atendidos

Nenhum critério funcional está pendente. A **confirmação no CI** ainda falta, e por isso a fase está **In Review**, não Completed.

## 17. Riscos identificados

| Risco | Impacto | Mitigação |
|---|---|---|
| Data Protection com chaves locais | Com mais de uma instância, cookies e links de e-mail não seriam decifrados em outra instância | Repositório de chaves compartilhado na Fase 14 (registrado no ADR-006) |
| Rate limiting por IP atrás de proxy | Todos os clientes compartilhariam o IP do proxy | Forwarded headers na Fase 14 |
| Consulta de sessão a cada requisição | Carga no banco | Consulta por chave primária; cache curto na Fase 13 se a medição justificar |
| Envio de e-mail síncrono | SMTP lento atrasa cadastro e recuperação | Worker com fila, quando houver necessidade concreta |
| Interactive Server com tokens | Componentes interativos não têm `HttpContext` | Chamadas autenticadas só em SSR nesta fase; solução na Fase 09 |

## 18. Estado do Git

- **Branch:** `feature/fase-03-identidade`, somente local.
- **Commits:** `50b45a3` (plano), `03e01fd` (API e infraestrutura), `e70352c` (Web e correções) e o commit de documentação de encerramento.
- **`develop`:** `a8dae04` (Fases 01 e 02 e a marca integradas). **`main`:** `c6bd32a`.
- **Fora do Git:** `.env` e user-secrets (connection string e chave JWT).

## 19. Próxima fase recomendada

1. **Antes de tudo:** push, CI aprovado e PR para `develop`. Só então a Fase 03 passa para Completed.
2. **Fase 04, Organizações e permissões:**
   - organizações e membros, com o usuário confirmado como base;
   - convites identificados pelo e-mail confirmado;
   - papéis e permissões granulares;
   - resolução da organização ativa pela identidade autenticada, substituindo o `UnresolvedTenantContext`;
   - reavaliação do RLS (ADR-003);
   - testes cross-tenant com usuários reais.
