# Estrutura do projeto

> Estado: Fase 01. Pastas marcadas com *(futuro)* são convenções para as próximas fases e ainda não existem.

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

## `src/`

### ProjectManager.Domain

Núcleo do negócio, sem dependências. **Vazio na Fase 01**, sem entidades artificiais.

```
ProjectManager.Domain/
├── <Modulo>/            (futuro) Entidades, objetos de valor, enums e eventos de cada módulo
└── Common/              (futuro) Tipos base compartilhados (Entity, DomainEvent etc.)
```

### ProjectManager.Application

```
ProjectManager.Application/
├── DependencyInjection.cs   AddApplication(): ponto de composição da camada
└── <Modulo>/                (futuro) Casos de uso, DTOs, validadores, interfaces de serviços
```

### ProjectManager.Infrastructure

```
ProjectManager.Infrastructure/
├── DependencyInjection.cs   AddInfrastructure(): registra TimeProvider.System; futuramente EF Core, Identity etc.
├── Persistence/             (futuro) DbContext, configurações EF, migrações
└── Integrations/            (futuro) GitHub, provedores de IA
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

Executável `projectctl`.

```
ProjectManager.CLI/
├── Program.cs              Ponto de entrada (UTF-8, delega para CliApplication)
├── CliApplication.cs       Comando raiz, --help, --version, execução com saída injetável
└── Commands/               (futuro) auth/, organization/, project/, task/, activity/, phase/, agent/
```

## `tests/`

| Projeto | Referencia | Cobertura atual |
|---|---|---|
| `ProjectManager.UnitTests` | `CLI` | `Architecture/ProjectReferenceRulesTests`: regras de dependência entre projetos. `Cli/CliApplicationTests`: `--version`, `--help`/`-h`, execução sem argumentos e comando desconhecido. |
| `ProjectManager.IntegrationTests` | `API` | `HealthEndpointTests`, `OpenApiDocumentTests` (Development × Production), `ErrorHandlingTests` (404 e 500 em Problem Details, sem vazar detalhes internos). |

## `docs/`

| Pasta | Conteúdo |
|---|---|
| `architecture/` | `ARCHITECTURE.md`, `PROJECT_STRUCTURE.md` |
| `decisions/` | ADRs (`ADR-NNN-TITULO.md`) |
| `specifications/` | Especificação técnica v1.0 (pendente; veja o README da pasta) |
| `reports/` | Relatórios de execução de cada fase |

## Portas de desenvolvimento

| Aplicação | HTTP | HTTPS |
|---|---|---|
| API | 5100 | 7100 |
| Web | 5200 | 7200 |
