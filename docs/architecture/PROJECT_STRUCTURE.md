# Estrutura do projeto

> Estado: Fase 05. Pastas marcadas com *(futuro)* são convenções para as próximas fases e ainda não existem.

## Raiz

| Arquivo | Finalidade |
|---|---|
| `ProjectManager.sln` | Solução (formato `.sln` clássico), com pastas de solução `src` e `tests`. |
| `global.json` | Fixa o SDK .NET 10.0.404 (`rollForward: latestFeature`). Também usado pelo CI. |
| `Directory.Build.props` | Configuração comum: `net10.0`, nullable, implicit usings, analisadores `Recommended`, avisos como erros, versão do produto (`VersionPrefix`). |
| `Directory.Packages.props` | Central Package Management: única fonte das versões NuGet. |
| `.editorconfig` | Estilo e convenções de nomenclatura C#, aplicados no build (`EnforceCodeStyleInBuild`). |
| `.gitattributes` / `.gitignore` | Normalização de fim de linha e exclusões (bin/obj, segredos locais etc.). |
| `README.md` | Visão geral e instruções de uso. |
| `AGENTS.md` | Regras permanentes para agentes de IA. |
| `.github/workflows/ci.yml` | CI: restore, build (Release) e testes. |
| `.claude/launch.json` | Configuração para iniciar API e Web pelo Claude Code (ferramenta de desenvolvimento). |
| `dotnet-tools.json` | Ferramentas locais da solução (`dotnet-ef` 10.0.15), restauradas com `dotnet tool restore`. |
| `docker-compose.yml` | PostgreSQL 18 e Mailpit (SMTP de teste, interface em :8025) de desenvolvimento, expostos só em `127.0.0.1`. |
| `.env.example` | Modelo do `.env` local (senha do PostgreSQL). O `.env` real não é versionado. |

## `src/`

### ProjectManager.Domain

Núcleo do negócio, sem dependências de outros projetos nem de frameworks.

```
ProjectManager.Domain/
├── Common/
│   ├── Entity.cs                 Base com Id UUID v7
│   ├── IOrganizationOwned.cs     Entidade pertencente a uma organização
│   ├── IAuditable.cs             CreatedAt/UpdatedAt (UTC)
│   └── Guard.cs                  Validações de invariantes (interno)
├── Organizations/                Organization (raiz do tenant), Role e RolePermission (papéis de sistema),
│                                 OrganizationMember, Invitation, OrganizationPermissions (catálogo)
├── Projects/                     Project (arquivamento, datas, prioridade, etiquetas, repositórios), ProjectMember,
│                                 ProjectTag, ProjectRepository, ProjectPermissions e ProjectRole (papéis fixos)
├── Projects/ProjectPhase.cs      Fase do projeto (mínima; completa na Fase 06)
└── <Modulo>/                     (futuro) Demais entidades por fase
```

### ProjectManager.Application

```
ProjectManager.Application/
├── DependencyInjection.cs        AddApplication(): ponto de composição da camada
├── Abstractions/MultiTenancy/
│   ├── ITenantContext.cs         Organização ativa (resolvida no backend)
│   └── CrossTenantAccessException.cs
├── Abstractions/Authentication/  IAuthenticationService, contratos com validação, AuthResult, AuthClaimTypes
├── Abstractions/Email/           IEmailSender, EmailMessage, EmailDeliveryException
├── Common/Result.cs              Result/ResultError (NotFound, Forbidden, Conflict, Validation)
├── Organizations/                IOrganizationService e contratos (requisições e respostas)
├── Projects/                     IProjectService, contratos, IProjectAccess e ProjectAccessGrant (acesso efetivo)
├── Common/PagedResult.cs         Página de listagem com total
└── <Modulo>/                     (futuro) Casos de uso, DTOs, validadores, interfaces de serviços
```

### ProjectManager.Infrastructure

```
ProjectManager.Infrastructure/
├── DependencyInjection.cs        AddInfrastructure(): TimeProvider, ITenantContext padrão, DbContext, health check "database", Identity, e-mail
├── Identity/                     ApplicationUser, UserSession, UserRefreshToken, SessionManager, AccessTokenIssuer,
│                                 AuthenticationService, AccountEmails, JwtOptions, AccountLinkOptions, mensagens em pt-BR
├── Email/                        SmtpEmailSender (MailKit), EmailOptions
├── Organizations/                OrganizationService, MembershipDirectory (único IgnoreQueryFilters), OrganizationEmails,
│                                 PermissionDefinition (catálogo persistido)
├── Projects/                     ProjectService, ProjectAccessResolver
├── Common/OpaqueTokens.cs        Tokens opacos de 256 bits e hash SHA-256 (sessões e convites)
├── MultiTenancy/
│   └── TenantContext.cs          Organização ativa por requisição (vazia até validar a associação)
├── Persistence/
│   ├── ProjectManagerDbContext.cs Filtros por organização; validação antes de gravar
│   ├── ProjectManagerDatabase.cs  Configuração única do provedor (Npgsql + snake_case)
│   ├── DatabaseOptions.cs         ConnectionStrings:ProjectManager (validada na inicialização)
│   ├── DesignTimeDbContextFactory.cs Para dotnet ef
│   ├── ChangeTracking/            TenantIsolationRules, AuditingRules
│   ├── Configurations/            Mapeamento por entidade (chaves, FKs compostas, índices, xmin)
│   └── Migrations/                Migrations do EF Core (código gerado)
└── Integrations/                  (futuro) GitHub, provedores de IA
```

### ProjectManager.API

```
ProjectManager.API/
├── Program.cs                          Composição e pipeline HTTP
├── ErrorHandling/
│   └── GlobalExceptionHandler.cs       Exceções → Problem Details 500 (ou 499 em cancelamento)
├── Health/
│   ├── HealthEndpoints.cs              Registro da verificação "self" ("database" vem da Infrastructure)
│   └── HealthCheckResponseWriter.cs    Resposta JSON do /health
├── Authentication/
│   ├── ApiAuthentication.cs            JwtBearer + verificação de sessão ativa; claims sub/sid
│   └── AuthRateLimiting.cs             Rate limiting por IP em /api/v1/auth (429 Problem Details)
├── Controllers/
│   ├── AuthController.cs               /api/v1/auth/*
│   ├── OrganizationsController.cs      /api/v1/organizations/* (organizações, membros, papéis, convites)
│   ├── InvitationsController.cs        /api/v1/invitations/accept
│   └── ProjectsController.cs           /api/v1/organizations/{id}/projects e /api/v1/projects/*
├── Organizations/                      ActiveOrganizationMiddleware (rota ou cabeçalho X-Gestalyn-Organization),
│                                       OrganizationPermissionAttribute, ProjectPermissionAttribute
├── Common/ResultProblems.cs            Result → Problem Details (404/403/409/400)
├── Properties/launchSettings.json      Perfis http (5100) e https (7100)
├── appsettings.json                    Produção/base: logs JSON, emissor e público do JWT, limites
├── appsettings.Development.json        Logs simples, Mailpit e URL da Web para os links de e-mail
└── ProjectManager.API.http             Requisições de exemplo
```

### ProjectManager.Web

```
ProjectManager.Web/
├── Program.cs                          Razor Components, ApiOptions, HttpClient tipado
├── AppInfo.cs                          Versão da aplicação
├── Api/
│   ├── ApiOptions.cs                   Seção "Api" (BaseUrl, TimeoutSeconds), validada na inicialização
│   ├── ApiHealthClient.cs              Cliente HTTP do /health
│   ├── ApiHealthResult.cs              Resultado da consulta
│   └── ApiAccountClient.cs (+ modelos) Cliente HTTP de /api/v1/auth
├── Authentication/                     BFF: AccountSession (cookie com tokens), TokenRefreshCookieEvents,
│                                       TokenRefreshCoordinator, AccountEndpoints (logout), ReturnUrls
├── Components/
│   ├── App.razor / Routes.razor        Documento raiz (pt-BR, tema escuro) e roteamento com autorização
│   ├── Layout/                         MainLayout (usuário logado), NavMenu (ações de conta), ReconnectModal
│   ├── Pages/                          Home (/), Status (/status), Error, NotFound
│   ├── Pages/Account/                  /conta/cadastro, entrar, confirmar-email, reenviar-confirmacao,
│   ├── Pages/Invitations/              /convites/aceitar (protegida)
│   │                                   esqueci-a-senha, redefinir-senha, perfil (protegida)
│   └── Shared/                         EnvironmentBadge, ApiStatusCard (Interactive Server), ApiErrors
├── wwwroot/                            app.css (tokens do tema), Bootstrap 5.3, favicon
├── Properties/launchSettings.json      Perfis http (5200) e https (7200)
└── appsettings*.json                   Logs; Api:BaseUrl em Development
```

### ProjectManager.CLI

Executável `gestalyn` (AssemblyName; o projeto continua `ProjectManager.CLI`).

```
ProjectManager.CLI/
├── Program.cs              Ponto de entrada (UTF-8, delega para CliApplication)
├── CliApplication.cs       Comando raiz, --help, --version, execução com saída injetável
└── Commands/               (futuro) auth/, organization/, project/, task/, activity/, phase/, agent/
```

## `tests/`

| Projeto | Referencia | Cobertura atual |
|---|---|---|
| `ProjectManager.UnitTests` | `CLI`, `Domain`, `Infrastructure` | `Architecture/ProjectReferenceRulesTests`: regras de dependência entre projetos. `Domain/*Tests`: invariantes das entidades. `Authentication/TokenTests`: refresh tokens, JWT e validação da chave. `Cli/CliApplicationTests`: `--version`, `--help`/`-h`, execução sem argumentos e comando desconhecido. |
| `ProjectManager.IntegrationTests` | `API`, `Infrastructure` | **Requer Docker.** `Infrastructure/PostgresDatabaseFixture` e `MailpitFixture` sobem PostgreSQL 18 e Mailpit (Testcontainers). `Authentication/*` cobre cadastro, login, sessão, recuperação de senha e proteções. `Persistence/PersistenceTests` e `Persistence/TenantIsolationTests` cobrem persistência e isolamento. `Api/*` cobre health check (com e sem banco), OpenAPI por ambiente e Problem Details. |

## `docs/`

| Pasta | Conteúdo |
|---|---|
| `architecture/` | `ARCHITECTURE.md`, `PROJECT_STRUCTURE.md` |
| `api/` | Contratos da API por área (`AUTH.md`, `ORGANIZATIONS.md`, `PROJECTS.md`) |
| `security/` | Modelo de segurança (`AUTENTICACAO.md`) |
| `decisions/` | ADRs (`ADR-NNN-TITULO.md`) |
| `specifications/` | Especificação mestre (fonte de verdade) e especificação de cada fase (`FASE-NN-*.md`) |
| `reports/` | Relatórios de execução de cada fase (`FASE-NN-RELATORIO.md`) |
| `PROJECT_STATUS.md` | Controle de progresso: fase atual, estado de cada fase, tarefas, bloqueios e última validação |

Os itens previstos na seção 3 da especificação mestre são criados **quando houver conteúdo real**, porque o Git não versiona pastas vazias e a especificação proíbe projetos vazios:

| Item | Fase em que deve surgir |
|---|---|
| `src/ProjectManager.MCP` | 12 |
| `src/ProjectManager.Worker` | Quando houver processamento em segundo plano |
| `tests/ProjectManager.ArchitectureTests` | 13 (até lá, as regras ficam em `UnitTests/Architecture`) |
| `tests/ProjectManager.EndToEndTests` | 13 |
| `docs/api` | Criada na Fase 03 (`AUTH.md`) |
| `docs/security` | Criada na Fase 03 (`AUTENTICACAO.md`) |
| `docs/deployment`, `docs/operations`, `infrastructure/` | 14 |
| `scripts/` | Quando houver um script útil. Na Fase 02, o `docker-compose.yml` e os comandos do README foram suficientes. |

## Branches

Fluxo da seção 13 da especificação mestre: `main` (estável, protegida por CI), `develop` (integração), `feature/*`, `fix/*` e `hotfix/*`. O CI roda em push para `main` e `develop` e em pull requests.

## Portas de desenvolvimento

| Aplicação | HTTP | HTTPS |
|---|---|---|
| API | 5100 | 7100 |
| Web | 5200 | 7200 |
