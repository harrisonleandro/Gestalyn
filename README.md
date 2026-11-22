# Gestalyn

> **Gestalyn** é o nome comercial. No código, o nome de trabalho continua sendo **ProjectManager**: solution, projetos e namespaces `ProjectManager.*` ([ADR-005](docs/decisions/ADR-005-NOME-COMERCIAL-GESTALYN.md)).

Plataforma de gerenciamento de projetos, equipes, tarefas, permissões, documentação e atividades, em que **usuários humanos e agentes de inteligência artificial** participam do desenvolvimento dos mesmos projetos. O sistema será hospedado na nuvem, terá arquitetura **multi-tenant** e está sendo preparado para comercialização.

**Estado atual: Fase 10 (API e autenticação de agentes) concluída.** Pela Web e pela API, já é possível criar conta e entrar, criar organizações, convidar pessoas e gerenciar membros e papéis, e também gerenciar projetos (equipe, papéis, etiquetas, repositórios), fases e marcos, e tarefas com número por projeto, vários responsáveis, prioridade, prazo, etiquetas, comentários, dependências e anexos. Cada projeto tem o próprio workflow de estados, com transições validadas no backend, aprovação e histórico. Toda alteração fica registrada numa auditoria imutável, com autor e origem; cada projeto tem documentos com versões, decisões técnicas e dashboards com progresso e estatísticas calculados a partir dos dados reais. A Web traz todas essas telas, com quadro de tarefas por estado, permissões refletidas na interface e Markdown seguro ([docs/web/TELAS.md](docs/web/TELAS.md)). O isolamento multi-tenant é testado com usuários reais.

**Fase 10 (API e autenticação de agentes):** agentes de IA têm credenciais próprias (token `gsk_` exibido uma única vez, com expiração, escopos e revogação), acesso concedido por projeto, rotas próprias em `/api/v1/agents`, idempotência, auditoria de cada operação, rate limiting e relatórios de trabalho com níveis de evidência. Agentes nunca aprovam nem concluem tarefas. Veja [docs/security/AGENTES.md](docs/security/AGENTES.md).

A referência funcional e técnica é a [especificação mestre](docs/specifications/ESPECIFICACAO-MESTRE.md), que define as decisões aprovadas e o roadmap em 14 fases.

## Tecnologias

| Área | Tecnologia |
|---|---|
| Linguagem / plataforma | C# · .NET 10 LTS |
| API | ASP.NET Core Web API (controllers), OpenAPI nativo, Problem Details (RFC 9457), Health Checks, rate limiting |
| Identidade | ASP.NET Core Identity · JWT de curta duração com sessão revogável · refresh token com rotação · e-mail via SMTP (MailKit; Mailpit em desenvolvimento) |
| Interface | Blazor Web App (SSR com navegação aprimorada, BFF) · Bootstrap 5.3 em tema escuro · Markdig |
| CLI | `gestalyn` · System.CommandLine |
| Testes | xUnit v3 · `WebApplicationFactory` (Microsoft.AspNetCore.Mvc.Testing) |
| CI | GitHub Actions |
| Persistência | PostgreSQL 18 · Entity Framework Core 10 + Npgsql · migrations · Testcontainers nos testes |
| Multi-tenancy | Banco e schema compartilhados com isolamento lógico por `OrganizationId`: filtro de leitura, bloqueio de gravação e FKs compostas no banco ([ADR-003](docs/decisions/ADR-003-ISOLAMENTO-MULTI-TENANT.md)) |
| Planejado (fases futuras) | SignalR · servidor MCP · object storage compatível com S3 · integração com GitHub |

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
│   ├── ProjectManager.Domain/          Entidades e regras de negócio (organizações, projetos, tarefas)
│   ├── ProjectManager.Application/     Casos de uso, DTOs, validadores, interfaces de serviços
│   ├── ProjectManager.Infrastructure/  Persistência, integrações externas, implementações técnicas
│   ├── ProjectManager.API/             ASP.NET Core Web API
│   ├── ProjectManager.Web/             Blazor Web App
│   └── ProjectManager.CLI/             CLI gestalyn
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
- **Docker em execução** (Docker Desktop no Windows/macOS): PostgreSQL local e testes de integração.
- Opcional: certificado HTTPS de desenvolvimento confiável (`dotnet dev-certs https --trust`) para usar os perfis `https`.

## Ambiente local

1. Crie o arquivo `.env` a partir do exemplo e defina uma senha local. O `.env` não é versionado.

   ```bash
   cp .env.example .env
   ```

2. Suba o PostgreSQL 18 e o **Mailpit** (SMTP de teste), ambos expostos apenas em `127.0.0.1`:

   ```bash
   docker compose up -d
   ```

   Os e-mails enviados em desenvolvimento (confirmação de cadastro, redefinição de senha) aparecem em http://localhost:8025; nada é entregue de verdade.

3. Guarde a connection string da API em user-secrets, com a mesma senha do `.env`:

   ```bash
   dotnet user-secrets set "ConnectionStrings:ProjectManager" "Host=127.0.0.1;Port=5432;Database=projectmanager;Username=projectmanager;Password=<senha do .env>" --project src/ProjectManager.API
   ```

   Use `127.0.0.1` em vez de `localhost`: no Windows, `localhost` tenta IPv6 primeiro e atrasa a conexão em cerca de 2 s.

4. Gere a chave de assinatura dos tokens (mínimo de 32 bytes) e guarde em user-secrets:

   ```bash
   dotnet user-secrets set "Authentication:Jwt:SigningKey" "$(openssl rand -base64 48)" --project src/ProjectManager.API
   ```

5. Restaure as ferramentas locais e aplique as migrations:

   ```bash
   dotnet tool restore
   ```

   ```bash
   dotnet ef database update --project src/ProjectManager.Infrastructure --startup-project src/ProjectManager.API --connection "Host=127.0.0.1;Port=5432;Database=projectmanager;Username=projectmanager;Password=<senha do .env>"
   ```

As migrations nunca são aplicadas automaticamente pela aplicação ([ADR-002](docs/decisions/ADR-002-PERSISTENCIA-EF-CORE-POSTGRESQL.md)). Para criar uma migration depois de alterar o modelo:

```bash
dotnet ef migrations add <Nome> --project src/ProjectManager.Infrastructure --startup-project src/ProjectManager.API --output-dir Persistence/Migrations
```

## Como compilar

```bash
dotnet restore
```

```bash
dotnet build
```

O build trata avisos como erros e executa os analisadores .NET no modo `Recommended`.

## Como executar a API

Requer o banco local configurado (seção anterior). Sem `ConnectionStrings:ProjectManager`, a API não inicia.

```bash
dotnet run --project src/ProjectManager.API
```

| Recurso | URL (perfil `http`, padrão) |
|---|---|
| Health check | http://localhost:5100/health |
| Documento OpenAPI (somente Development) | http://localhost:5100/openapi/v1.json |

Perfil HTTPS: `dotnet run --project src/ProjectManager.API --launch-profile https` (https://localhost:7100).

O endpoint `/health` reporta somente as verificações de componentes que existem: `self` (processo da API) e `database` (conexão com o PostgreSQL). Com o banco indisponível, responde 503.

A autenticação fica em `/api/v1/auth` (cadastro, confirmação de e-mail, login, refresh, logout, `me` e recuperação de senha). Os contratos estão em [docs/api/AUTH.md](docs/api/AUTH.md) e o modelo de segurança em [docs/security/AUTENTICACAO.md](docs/security/AUTENTICACAO.md). Fora dos endpoints públicos de autenticação, todo controller exige `Authorization: Bearer`.

Organizações, membros, papéis e convites ficam em `/api/v1/organizations` e `/api/v1/invitations` ([docs/api/ORGANIZATIONS.md](docs/api/ORGANIZATIONS.md)). As permissões são verificadas no backend: quem não é membro recebe 404, e membro sem permissão recebe 403.

Projetos ficam em `/api/v1/organizations/{organizationId}/projects` (criar e listar) e `/api/v1/projects/{projectId}` (consultar, editar, arquivar, equipe e repositórios). Rotas por ID do projeto exigem o cabeçalho `X-Gestalyn-Organization` com o ID da organização ([docs/api/PROJECTS.md](docs/api/PROJECTS.md)). Fases, marcos e tarefas ficam em `/api/v1/projects/{projectId}/phases`, `/milestones` e `/tasks`, e cada tarefa em `/api/v1/tasks/{taskId}`, com comentários, dependências e anexos ([docs/api/TASKS.md](docs/api/TASKS.md)). O workflow fica em `/api/v1/projects/{projectId}/workflow` e `/api/v1/workflow/...`, e a tarefa muda de estado por `POST /api/v1/tasks/{taskId}/transitions` ([docs/api/WORKFLOW.md](docs/api/WORKFLOW.md)). As atividades ficam em `/api/v1/projects/{projectId}/activities` e `/api/v1/organizations/{organizationId}/activities` ([docs/api/ACTIVITY.md](docs/api/ACTIVITY.md)); documentos e decisões, em `/documents` e `/decisions` ([docs/api/DOCUMENTS.md](docs/api/DOCUMENTS.md)); e os dashboards, em `/dashboard`, `/progress` e `/statistics` ([docs/api/DASHBOARDS.md](docs/api/DASHBOARDS.md)).

Os anexos são gravados em disco local, no diretório `Storage:Attachments:RootPath`. Em Development, o diretório é `%LOCALAPPDATA%/Gestalyn/storage`; fora de Development, a configuração é obrigatória e a API não inicia sem ela.

## Como executar a Web

Inicie a API primeiro e, em outro terminal:

```bash
dotnet run --project src/ProjectManager.Web
```

Acesse http://localhost:5200. Depois de entrar, crie uma organização e um projeto: as telas de projetos, quadro, tarefas, workflow, fases e marcos, atividades, documentos, decisões e configurações ficam nas abas do projeto ([docs/web/TELAS.md](docs/web/TELAS.md)). A página **Status do sistema**, no rodapé do menu, consulta o `/health` da API. Os tokens da API ficam só no servidor da Web, dentro de um cookie HttpOnly; o navegador nunca fala com a API.

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

O executável compilado chama-se `gestalyn` (`src/ProjectManager.CLI/bin/<Configuração>/net10.0/gestalyn[.exe]`). Por enquanto a CLI oferece apenas `--help` e `--version`; os grupos de comandos (auth, organization, project, task, activity, phase, agent) serão adicionados quando tiverem implementação real.

## Como executar os testes

```bash
dotnet test
```

- **UnitTests**: regras de dependência entre projetos, invariantes do domínio e comportamento da CLI.
- **WebTests**: componentes da Web com bUnit (Markdown seguro, estados, permissões refletidas na tela, quadro), com a API substituída por respostas falsas.
- **IntegrationTests** (**requer Docker**): sobem PostgreSQL e Mailpit descartáveis com Testcontainers e aplicam as migrations. Cobrem persistência, isolamento multi-tenant, autenticação (fluxos completos com e-mail real, sessão, lockout, rate limiting, logs sem segredos) e a API hospedada em memória.

## Integração contínua

Branches: `main` (estável), `develop` (integração), `feature/*`, `fix/*` e `hotfix/*`.

O workflow [.github/workflows/ci.yml](.github/workflows/ci.yml) roda em push para `main` e `develop`, em pull requests e manualmente: configura o .NET pelo `global.json`, restaura, compila em Release e executa os testes. Não há publicação automática.

## Para agentes de IA

Leia [AGENTS.md](AGENTS.md) antes de alterar o repositório.
