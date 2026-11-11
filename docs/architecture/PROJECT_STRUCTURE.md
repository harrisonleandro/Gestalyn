# Estrutura do projeto

> Estado: Fase 02. Pastas marcadas com *(futuro)* são convenções para as próximas fases e ainda não existem.

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
| `docker-compose.yml` | PostgreSQL 18 de desenvolvimento, exposto só em `127.0.0.1`. |
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
├── Organizations/Organization.cs Raiz do tenant
├── Projects/Project.cs           Projeto (mínimo; completo na Fase 05)
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
└── <Modulo>/                     (futuro) Casos de uso, DTOs, validadores, interfaces de serviços
```

### ProjectManager.Infrastructure

```
ProjectManager.Infrastructure/
├── DependencyInjection.cs        AddInfrastructure(): TimeProvider, ITenantContext padrão, DbContext, health check "database"
├── MultiTenancy/
│   └── UnresolvedTenantContext.cs Sem organização ativa (padrão até a Fase 04)
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
│   ├── HealthEndpoints.cs              Registro das verificações (somente "self" na Fase 01)
│   └── HealthCheckResponseWriter.cs    Resposta JSON do /health
├── Controllers/                        (futuro) Controllers por módulo
├── Properties/launchSettings.json      Perfis http (5100) e https (7100)
├── appsettings.json                    Produção/base: logs JSON
├── appsettings.Development.json        Logs simples
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
│   └── ApiHealthResult.cs              Resultado da consulta
├── Components/
│   ├── App.razor / Routes.razor        Documento raiz (pt-BR, tema escuro) e roteamento
│   ├── Layout/                         MainLayout, NavMenu, ReconnectModal
│   ├── Pages/                          Home (/), Status (/status), Error, NotFound
│   └── Shared/                         EnvironmentBadge, ApiStatusCard (Interactive Server)
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
| `ProjectManager.UnitTests` | `CLI`, `Domain` | `Architecture/ProjectReferenceRulesTests`: regras de dependência entre projetos. `Domain/*Tests`: invariantes das entidades. `Cli/CliApplicationTests`: `--version`, `--help`/`-h`, execução sem argumentos e comando desconhecido. |
| `ProjectManager.IntegrationTests` | `API`, `Infrastructure` | **Requer Docker.** `Infrastructure/PostgresDatabaseFixture` sobe o PostgreSQL 18 (Testcontainers) e aplica as migrations. `Persistence/PersistenceTests` e `Persistence/TenantIsolationTests` cobrem persistência e isolamento. `Api/*` cobre health check (com e sem banco), OpenAPI por ambiente e Problem Details. |

## `docs/`

| Pasta | Conteúdo |
|---|---|
| `architecture/` | `ARCHITECTURE.md`, `PROJECT_STRUCTURE.md` |
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
| `docs/api` | 03/04, com os primeiros endpoints `/api/v1` |
| `docs/security` | 03, com a estratégia de autenticação |
| `docs/deployment`, `docs/operations`, `infrastructure/` | 14 |
| `scripts/` | Quando houver um script útil. Na Fase 02, o `docker-compose.yml` e os comandos do README foram suficientes. |

## Branches

Fluxo da seção 13 da especificação mestre: `main` (estável, protegida por CI), `develop` (integração), `feature/*`, `fix/*` e `hotfix/*`. O CI roda em push para `main` e `develop` e em pull requests.

## Portas de desenvolvimento

| Aplicação | HTTP | HTTPS |
|---|---|---|
| API | 5100 | 7100 |
| Web | 5200 | 7200 |
