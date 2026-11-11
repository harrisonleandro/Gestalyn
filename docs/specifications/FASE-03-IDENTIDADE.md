# Fase 03: Identidade e autenticação

- **Estado:** Planned. Aguarda aprovação das decisões D1 a D5.
- **Referência:** [Especificação mestre](ESPECIFICACAO-MESTRE.md), seção 9 (Fase 03), seção 5.1 (contratos `/api/v1/auth/*`) e seção 7 (segurança)
- **Branch:** `feature/fase-03-identidade` (a partir de `develop`, `a8dae04`)

## Objetivo

Permitir que usuários se cadastrem, autentiquem, mantenham sessão, encerrem a sessão e recuperem a senha, tanto pela API (base para CLI e agentes) quanto pela interface Web, com proteção contra abuso e testes de segurança.

## Escopo

1. **ASP.NET Core Identity** com chave `Guid` (UUID v7) no `ProjectManagerDbContext` existente: tabela de usuários e tabelas auxiliares do Identity. Usuários são globais, não pertencem a uma organização.
2. **Endpoints da API** (seção 5.1 da especificação mestre), sob `/api/v1/auth`:
   - `POST /register`, `POST /login`, `POST /refresh`, `POST /logout`, `GET /me`;
   - `POST /forgot-password` e `POST /reset-password` (recuperação de senha);
   - `POST /confirm-email`, se D5 for aprovada.
3. **Modelo de sessão e tokens** (D1): access token JWT de curta duração, refresh token opaco com rotação, e sessão no servidor revogável. O logout invalida a sessão imediatamente.
4. **Interface Web** (D2): páginas mínimas e funcionais de cadastro, login, logout, "esqueci a senha", redefinição de senha e perfil (rota protegida). A sessão da Web fica em cookie seguro, e a Web chama a API em nome do usuário.
5. **Recuperação de senha com e-mail real** (D4): abstração de envio de e-mail, implementação SMTP e **Mailpit** (servidor SMTP de teste) no ambiente local e nos testes.
6. **Proteção contra abuso:**
   - bloqueio da conta após falhas consecutivas (lockout do Identity);
   - rate limiting por IP nos endpoints de autenticação;
   - respostas que não revelam se um e-mail está cadastrado.
7. **Política de senha:** mínimo de 12 caracteres, sem regras de composição obrigatórias (alinhado ao NIST SP 800-63B); hash PBKDF2 padrão do Identity.
8. **Testes:** autenticação, sessão, logout, refresh com rotação e detecção de reuso, rotas protegidas, recuperação de senha via Mailpit, lockout e rate limiting.
9. **Documentação:** ADR de autenticação e sessão, ADR de e-mail, `docs/security/AUTENTICACAO.md` e `docs/api/AUTH.md`.

## Fora de escopo

- Organizações, membros, convites, papéis e permissões (Fase 04).
- Autor das alterações (`CreatedBy`/`UpdatedBy`) na auditoria (Fase 08).
- Autenticação de agentes e credenciais de agentes (Fase 10).
- Login pela CLI (Fase 11).
- Login social, autenticação em dois fatores (2FA) e passkeys (poderão ser avaliados depois).
- Provedor de e-mail de produção (Fase 14).
- Interface completa e design system (Fase 09); nesta fase, só as telas mínimas de autenticação.

## Decisões que exigem aprovação

| ID | Proposta | Alternativas |
|---|---|---|
| **D1** Tokens da API | **Access token JWT** assinado (15 min) com o identificador da sessão (`sid`); **refresh token opaco** (30 dias), guardado só como hash, com rotação a cada uso e **detecção de reuso**: reutilizar um token já trocado revoga a sessão inteira. A tabela `user_sessions` permite revogar a sessão; o access token só é aceito se a sessão estiver ativa, então **o logout vale imediatamente**. Serve a Web, a CLI (Fase 11) e, com ajustes, os agentes (Fase 10). | (a) Bearer tokens nativos do Identity (`MapIdentityApi`): access token não revogável, contrato diferente do da especificação e pouco controle. (b) Só cookies na API: não serve CLI nem agentes. (c) JWT sem sessão no servidor: logout não invalida o token até ele expirar. |
| **D2** Sessão da Web | A Web funciona como **BFF (backend-for-frontend)**. O login acontece em um formulário da Web, que chama a API. Os tokens ficam **somente no servidor**, criptografados dentro do cookie de autenticação da Web (HttpOnly, Secure, SameSite=Lax), e nunca chegam ao navegador. A Web renova o access token com o refresh token quando ele expira, e o logout revoga a sessão na API e apaga o cookie. | Tokens no navegador (localStorage): expostos a XSS e contrários à seção 7 da especificação ("nunca enviar credenciais completas para a interface web"). Web acessando o banco diretamente: viola a regra Web → API somente por HTTP. |
| **D3** Usuário e domínio | `ApplicationUser : IdentityUser<Guid>` na **Infrastructure**, com `DisplayName`. O Domain não depende do Identity; quando houver regras de domínio sobre usuários (Fase 04), elas usarão o `UserId`. Tabelas em `snake_case`: `users`, `user_claims`, `user_logins`, `user_tokens` (sem tabelas de papéis do Identity, porque os papéis são por organização e projeto, na Fase 04). | Entidade `User` no Domain duplicada com o Identity: sincronização sem benefício agora. |
| **D4** E-mail | Interface `IEmailSender` (Application) e implementação **SMTP com MailKit** (Infrastructure). Em desenvolvimento e nos testes, o **Mailpit** (contêiner) recebe os e-mails, inclusive os de redefinição de senha, e os testes leem a mensagem pela API HTTP do Mailpit. O provedor de produção (SMTP de um serviço ou API) é escolhido na Fase 14, sem mudar o código. | Remetente que só registra no log: não cumpre o critério "recuperação de senha funciona com provedor configurado". `System.Net.Mail.SmtpClient`: a própria Microsoft o desaconselha para uso novo. |
| **D5** Confirmação de e-mail | **Exigir confirmação de e-mail antes do primeiro login.** Os convites da Fase 04 dependem do e-mail como identidade; sem confirmação, alguém poderia se cadastrar com o e-mail de outra pessoa e receber convites destinados a ela. Usa a mesma infraestrutura de e-mail da D4. | Não exigir agora: mais simples, mas abre o risco descrito e exigiria migração de contas depois. |

Decisões de baixo risco já tomadas (serão registradas em ADR):

- **Assinatura dos tokens:** HMAC-SHA256 com chave de pelo menos 256 bits fornecida por configuração (user-secrets em desenvolvimento), nunca versionada.
- **Lockout:** 5 falhas bloqueiam a conta por 15 minutos.
- **Rate limiting:** janela fixa por IP nos endpoints `/auth/*` (limites registrados no ADR), com resposta 429 em Problem Details.
- **Mensagens neutras:** `login` responde "credenciais inválidas" para qualquer falha; `forgot-password` responde sempre 202.
- **Datas e IDs:** os mesmos padrões da Fase 02 (UTC, UUID v7).

## Dependências

| Item | Uso | Estado |
|---|---|---|
| Microsoft.AspNetCore.Identity.EntityFrameworkCore 10.0.15 | Identity com EF Core | Disponível |
| Microsoft.AspNetCore.Authentication.JwtBearer 10.0.15 | Validação de JWT na API | Disponível |
| Microsoft.IdentityModel.JsonWebTokens 8.x | Emissão de JWT | Disponível |
| MailKit 4.18.x | Envio SMTP | Disponível |
| Imagem `axllent/mailpit` | SMTP de teste (local e testes) | Disponível |
| Docker em execução | PostgreSQL e Mailpit | Em execução |
| Fase 02 integrada em `develop` | `DbContext`, migrations, testes com PostgreSQL | Concluída |

## Tarefas

| ID | Tarefa | Subtarefas |
|---|---|---|
| F03-001 | Configurar ASP.NET Core Identity | `ApplicationUser`, `IdentityUserContext` no `DbContext`, opções de senha e lockout, migration |
| F03-002 | Criar cadastro | `POST /api/v1/auth/register` (validação, e-mail de confirmação se D5); página de cadastro na Web |
| F03-003 | Criar login | `POST /api/v1/auth/login` (tokens, sessão); página de login na Web |
| F03-004 | Criar logout | `POST /api/v1/auth/logout` (revoga a sessão); logout na Web (apaga o cookie e revoga na API) |
| F03-005 | Sessão e autenticação da Web | Cookie BFF, `AuthenticationStateProvider`, renovação automática, página de perfil protegida |
| F03-006 | Autenticação da API | JwtBearer + validação da sessão ativa; `GET /api/v1/auth/me`; política padrão autenticada nos endpoints de negócio |
| F03-007 | Recuperação de senha | `forgot-password` e `reset-password`; e-mail via SMTP (Mailpit); páginas na Web; redefinir a senha revoga as sessões existentes |
| F03-008 | Validação de credenciais | Política de senha, validação de entrada com Problem Details (400), e-mail normalizado |
| F03-009 | Expiração e renovação | Access token de 15 min, refresh com rotação, detecção de reuso, expiração absoluta da sessão |
| F03-010 | Proteção contra abuso | Lockout, rate limiting por IP, mensagens neutras |
| F03-011 | Testes de autenticação | Integração (API, PostgreSQL e Mailpit reais), unitários de tokens, testes de segurança |

## Arquivos previstos

```
src/ProjectManager.Application/Abstractions/Authentication/  Contratos e DTOs de autenticação, ICurrentUser
src/ProjectManager.Application/Abstractions/Email/            IEmailSender
src/ProjectManager.Infrastructure/Identity/                   ApplicationUser, UserSession, TokenService, AuthenticationService
src/ProjectManager.Infrastructure/Email/                      SmtpEmailSender (MailKit), opções
src/ProjectManager.Infrastructure/Persistence/Migrations/     Migration de Identity e sessões
src/ProjectManager.API/Controllers/AuthController.cs          /api/v1/auth/*
src/ProjectManager.API/Authentication/                        JwtBearer, validação da sessão, rate limiting
src/ProjectManager.Web/Authentication/                        Cookie BFF, AuthenticationStateProvider, handler de token
src/ProjectManager.Web/Components/Pages/Account/              Cadastro, login, logout, esqueci a senha, redefinição, confirmação, perfil
docker-compose.yml                                            Serviço Mailpit
tests/ProjectManager.UnitTests/Authentication/                Tokens, hash de refresh token, regras
tests/ProjectManager.IntegrationTests/Authentication/         Fluxos completos com PostgreSQL e Mailpit
docs/decisions/ADR-006-AUTENTICACAO-E-SESSAO.md
docs/decisions/ADR-007-ENVIO-DE-EMAIL.md
docs/security/AUTENTICACAO.md, docs/api/AUTH.md
```

## Critérios de aceitação

| Critério (especificação mestre) | Como será verificado |
|---|---|
| Usuário consegue se cadastrar | Teste de integração e Web real; senha só como hash no banco |
| Usuário consegue autenticar | Login retorna tokens; `GET /me` com o access token; login pela Web |
| Sessões funcionam corretamente | Refresh com rotação; reuso detectado revoga a sessão; expiração; Web renova sem novo login |
| Logout invalida a sessão | Após o logout, o mesmo access token recebe 401 imediatamente e o refresh token é rejeitado |
| Rotas protegidas exigem autenticação | `GET /me` sem token: 401; página de perfil na Web redireciona para o login |
| Recuperação de senha funciona com provedor configurado | E-mail capturado no Mailpit; link usado; senha nova funciona e a antiga não; sessões antigas revogadas |
| Testes de segurança passam | Lockout, rate limiting (429), mensagens neutras, token adulterado, expirado ou de sessão revogada recusados, senha nunca em logs |

## Estratégia de testes

- **Unitários:** emissão e validação de JWT, hash do refresh token e regras de rotação.
- **Integração** (`WebApplicationFactory` + PostgreSQL + Mailpit via Testcontainers): todos os fluxos da API, inclusive os negativos.
- **Web:** execução real no navegador do app (cadastro, confirmação, login, perfil, logout, recuperação de senha).
- **Mutação:** remover a verificação de sessão ativa deve fazer o teste de logout falhar.
- **CI:** os mesmos testes no `ubuntu-latest`.

## Riscos

| Risco | Mitigação |
|---|---|
| Complexidade de tokens no Blazor Interactive Server (circuito sem `HttpContext`) | Tokens obtidos no servidor pelo cookie; componentes interativos usam um serviço com escopo de circuito; padrão documentado em ADR |
| Vazamento de tokens ou senhas em logs | Testes que verificam a ausência nos logs; mensagens de log sem dados sensíveis |
| Chave de assinatura fraca ou versionada | Validação de tamanho mínimo na inicialização; user-secrets em desenvolvimento |
| Verificar a sessão a cada requisição custa uma consulta ao banco | Consulta por chave primária; cache curto avaliado na Fase 13, se necessário |
| Testes com Mailpit mais lentos | Um contêiner por suíte, como o PostgreSQL |

## Definição de conclusão

Todos os critérios verificados com evidência (local e CI), mutação do logout detectada, ADRs 006 e 007 registrados, `docs/security` e `docs/api` criados, README, AGENTS.md, `PROJECT_STATUS.md` e relatório da Fase 03 atualizados, alterações versionadas e integradas por PR.
