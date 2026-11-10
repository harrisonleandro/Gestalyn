# ProjectManager

> Nome de trabalho. O nome comercial ainda não foi definido.

Plataforma de gerenciamento de projetos, equipes, tarefas, permissões, documentação e atividades, em que **usuários humanos e agentes de inteligência artificial** participam do desenvolvimento dos mesmos projetos. O sistema será hospedado na nuvem, terá arquitetura **multi-tenant** e está sendo preparado para comercialização.

**Estado atual: Fase 01 (fundação técnica) concluída.** A solução compila, é testada e possui API, interface Web e CLI funcionais no nível de infraestrutura. Nenhuma funcionalidade de negócio (organizações, projetos, tarefas, autenticação, permissões) foi implementada ainda. O progresso de todas as fases está em [docs/PROJECT_STATUS.md](docs/PROJECT_STATUS.md).

A referência funcional e técnica é a [especificação mestre](docs/specifications/ESPECIFICACAO-MESTRE.md), que define as decisões aprovadas e o roadmap em 14 fases.

## Tecnologias

| Área | Tecnologia |
|---|---|
| Linguagem / plataforma | C# · .NET 10 LTS |
| API | ASP.NET Core Web API (controllers), OpenAPI nativo, Problem Details (RFC 9457), Health Checks |
| Interface | Blazor Web App (Interactive Server) · Bootstrap 5.3 em tema escuro |
| CLI | `projectctl` · System.CommandLine |
| Testes | xUnit v3 · `WebApplicationFactory` (Microsoft.AspNetCore.Mvc.Testing) |
| CI | GitHub Actions |
| Planejado (fases futuras) | PostgreSQL + Npgsql · Entity Framework Core · ASP.NET Core Identity · SignalR · servidor MCP · object storage compatível com S3 · integração com GitHub |
| Multi-tenancy | Banco e schema compartilhados com isolamento lógico por `OrganizationId` (a partir da Fase 02) |

## Arquitetura

Monólito modular em camadas. Detalhes em [docs/architecture/ARCHITECTURE.md](docs/architecture/ARCHITECTURE.md) e na decisão [ADR-001](docs/decisions/ADR-001-MODULAR-MONOLITH.md).

```
Application → Domain
Infrastructure → Application + Domain
API → Application + Infrastructure
Web → API (somente HTTP)
CLI → API (somente HTTP)
MCP → Application ou contratos próprios (Fase 12)
Worker → Application + Infrastructure (quando necessário)
```

As regras de dependência são verificadas automaticamente por testes (`ProjectReferenceRulesTests`).

## Estrutura

```
ProjectManager/
├── src/
│   ├── ProjectManager.Domain/          Entidades, regras de negócio, eventos de domínio (vazio na Fase 01)
│   ├── ProjectManager.Application/     Casos de uso, DTOs, validadores, interfaces de serviços
│   ├── ProjectManager.Infrastructure/  Persistência, integrações externas, implementações técnicas
│   ├── ProjectManager.API/             ASP.NET Core Web API
│   ├── ProjectManager.Web/             Blazor Web App
│   └── ProjectManager.CLI/             CLI projectctl
├── tests/
│   ├── ProjectManager.UnitTests/
│   └── ProjectManager.IntegrationTests/
├── docs/
│   ├── architecture/                   Arquitetura e estrutura do projeto
│   ├── decisions/                      Architecture Decision Records (ADR)
│   ├── reports/                        Relatórios de execução das fases
│   ├── specifications/                 Especificação mestre e especificação de cada fase
│   └── PROJECT_STATUS.md               Controle de progresso das fases
└── .github/workflows/                  CI
```

Descrição completa em [docs/architecture/PROJECT_STRUCTURE.md](docs/architecture/PROJECT_STRUCTURE.md).

## Pré-requisitos

- [.NET SDK 10.0.404](https://dotnet.microsoft.com/download/dotnet/10.0) ou superior na linha 10.0 (fixado em `global.json` com `rollForward: latestFeature`).
- Git.
- Opcional: certificado HTTPS de desenvolvimento confiável (`dotnet dev-certs https --trust`) para usar os perfis `https`.

Nenhum banco de dados ou serviço externo é necessário nesta fase.

## Como compilar

```bash
dotnet restore
```

```bash
dotnet build
```

O build trata avisos como erros e executa os analisadores .NET no modo `Recommended`.

## Como executar a API

```bash
dotnet run --project src/ProjectManager.API
```

| Recurso | URL (perfil `http`, padrão) |
|---|---|
| Health check | http://localhost:5100/health |
| Documento OpenAPI (somente Development) | http://localhost:5100/openapi/v1.json |

Perfil HTTPS: `dotnet run --project src/ProjectManager.API --launch-profile https` (https://localhost:7100).

O endpoint `/health` reporta somente as verificações de componentes que existem. Atualmente apenas `self` (processo da API); banco de dados e serviços externos serão incluídos quando forem configurados.

## Como executar a Web

Inicie a API primeiro e, em outro terminal:

```bash
dotnet run --project src/ProjectManager.Web
```

Acesse http://localhost:5200. A página **Status do sistema** consulta o `/health` da API por HTTP.

A URL da API é configurada em `Api:BaseUrl` (em Development: `appsettings.Development.json`). Fora de Development, a configuração é obrigatória e a aplicação não inicia sem ela. Exemplo por variável de ambiente:

```bash
Api__BaseUrl=https://api.exemplo.com/ dotnet run --project src/ProjectManager.Web
```

## Como executar a CLI

```bash
dotnet run --project src/ProjectManager.CLI -- --help
```

```bash
dotnet run --project src/ProjectManager.CLI -- --version
```

O executável compilado chama-se `projectctl` (`src/ProjectManager.CLI/bin/<Configuração>/net10.0/projectctl[.exe]`). Na Fase 01 a CLI oferece apenas `--help` e `--version`; os grupos de comandos (auth, organization, project, task, activity, phase, agent) serão adicionados quando tiverem implementação real.

## Como executar os testes

```bash
dotnet test
```

- **UnitTests**: regras de dependência entre projetos e comportamento da CLI.
- **IntegrationTests**: API hospedada em memória: health check, OpenAPI por ambiente e respostas de erro padronizadas.

## Integração contínua

Branches: `main` (estável), `develop` (integração), `feature/*`, `fix/*` e `hotfix/*`.

O workflow [.github/workflows/ci.yml](.github/workflows/ci.yml) roda em push para `main` e `develop`, em pull requests e manualmente: configura o .NET pelo `global.json`, restaura, compila em Release e executa os testes. Não há publicação automática.

## Para agentes de IA

Leia [AGENTS.md](AGENTS.md) antes de alterar o repositório.
