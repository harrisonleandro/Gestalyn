# Fase 02: Persistência e modelo de domínio

- **Estado:** Planned. Aguarda aprovação das decisões D1 a D4.
- **Referência:** [Especificação mestre](ESPECIFICACAO-MESTRE.md), seção 9, Fase 02; seções 2.3 (multi-tenancy) e 6.5 (concorrência)
- **Branch:** `feature/fase-02-persistencia` (a partir de `develop`)

## Objetivo

Construir a base de dados PostgreSQL e o modelo de domínio inicial, com isolamento multi-tenant garantido no backend e no banco, migrations aplicáveis, auditoria básica de persistência e testes automatizados de persistência e de isolamento.

## Escopo

1. Blocos de construção do domínio:
   - entidade base com identificador UUID;
   - contrato de entidade pertencente a uma organização (`OrganizationId`);
   - campos de auditoria (`CreatedAt`, `UpdatedAt`, em UTC).
2. Entidades mínimas para materializar e testar o isolamento (decisão **D1**):
   - `Organization`: raiz do tenant.
   - `Project`: entidade pertencente à organização, com dados básicos (nome, chave e descrição).
   - `ProjectPhase`: filha de `Project`. Prova que o banco impede associações entre organizações diferentes.
3. EF Core + Npgsql: `DbContext`, configurações por entidade, convenção `snake_case`, índices, chaves e restrições.
4. Mecanismo de isolamento (decisão **D2**):
   - filtro global de consulta por organização;
   - validação na gravação;
   - chaves estrangeiras compostas `(organization_id, id)`.
5. Auditoria básica: `CreatedAt` e `UpdatedAt` preenchidos automaticamente via `TimeProvider`.
6. Controle de concorrência otimista com o `xmin` do PostgreSQL.
7. Migrations no projeto Infrastructure, com `dotnet-ef` como ferramenta local da solução.
8. PostgreSQL local via Docker Compose, com a senha fora do repositório (decisão **D3**).
9. Health check do banco no `/health`.
10. Testes de persistência e de isolamento contra PostgreSQL real com Testcontainers, local e no CI (decisão **D4**).
11. ADRs de persistência, isolamento e estratégia de testes.

## Fora de escopo

- `User`, ASP.NET Core Identity, autenticação (Fase 03).
- `OrganizationMember`, `Invitation`, papéis e permissões (Fase 04).
- Casos de uso, endpoints `/api/v1` e telas: nenhuma entidade é exposta pela API nesta fase.
- Autor das alterações (`CreatedBy`/`UpdatedBy`): depende de identidade; entra na Fase 03/08.
- Demais entidades da seção 4 da especificação mestre (`Milestone`, `ProjectTag`, `Task` etc.): entram em suas fases.
- Row-Level Security do PostgreSQL: avaliada e registrada em ADR, mas não implementada nesta fase.
- Banco gerenciado em nuvem (Fase 14).

## Decisões que exigem aprovação

| ID | Decisão proposta | Alternativas |
|---|---|---|
| **D1** | Modelar só `Organization`, `Project` (mínimo) e `ProjectPhase` (mínimo). É o menor conjunto que exercita isolamento por consulta e por relacionamento. Os campos completos entram nas Fases 05 e 06. | (a) Só `Organization` + `Project`: não testa relacionamentos filhos entre organizações. (b) Antecipar `User`/`OrganizationMember`: acopla a fase à Identity (Fase 03). |
| **D2** | Isolamento em três camadas. (1) Filtro global por `OrganizationId` vindo de `ITenantContext` (sem organização ativa, nenhum dado é retornado). (2) Interceptor que rejeita gravação de entidade de outra organização ou sem organização ativa. (3) FKs compostas `(organization_id, project_id)` → `projects (organization_id, id)` no banco. | Somente filtro na aplicação (sem garantia no banco); RLS já nesta fase (maior complexidade e acoplamento à conexão; fica como avaliação). |
| **D3** | `docker-compose.yml` com PostgreSQL 18; a senha vem de `.env` (não versionado, com `.env.example` versionado); a connection string de desenvolvimento fica em `dotnet user-secrets`. | Senha fixa de desenvolvimento versionada: mais simples, mas contraria a regra de nunca versionar connection strings com credenciais. |
| **D4** | Testes com **Testcontainers.PostgreSql** (PostgreSQL real e descartável) no projeto IntegrationTests. Exigem Docker local e no CI (`ubuntu-latest` já possui). | SQLite/InMemory: não validam FKs compostas, `xmin`, índices nem SQL do PostgreSQL; não servem para provar isolamento. |

Decisões de baixo risco já tomadas (serão registradas em ADR):

- **UUID v7:** gerado no domínio com `Guid.CreateVersion7()`, ordenável no tempo, melhor para índices.
- **`snake_case`:** convenção via `EFCore.NamingConventions`.
- **`xmin` como token de concorrência.**
- **Migrations nunca aplicadas automaticamente na inicialização em Production:** aplicação explícita, com política detalhada na Fase 14.

## Dependências

| Item | Uso | Estado |
|---|---|---|
| Docker Desktop **em execução** | PostgreSQL local e Testcontainers | Instalado (29.8.2), **daemon parado** |
| Microsoft.EntityFrameworkCore 10.0.15 | ORM | Disponível |
| Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 | Provedor PostgreSQL | Disponível |
| Microsoft.EntityFrameworkCore.Design 10.0.15 + `dotnet-ef` 10.0.15 | Migrations | Disponível |
| EFCore.NamingConventions 10.0.1 | `snake_case` | Disponível |
| Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore 10.0.15 | Health check do banco | Disponível |
| Testcontainers.PostgreSql 4.16.0 | Testes com PostgreSQL real | Disponível |

## Tarefas

| ID | Tarefa | Subtarefas |
|---|---|---|
| F02-001 | Projetar entidades | Documentar o modelo das 3 entidades, campos e invariantes; diagrama ER em ARCHITECTURE.md |
| F02-002 | Definir identificadores | Entidade base com `Guid` v7; `OrganizationId` obrigatório nas entidades da organização |
| F02-003 | Definir relacionamentos | `Organization 1–N Project 1–N ProjectPhase`; FKs compostas com `OrganizationId` |
| F02-004 | Implementar entidades | Construtores e métodos que garantem invariantes (nome obrigatório, limites de tamanho, chave do projeto em formato válido) |
| F02-005 | Configurações EF Core | `IEntityTypeConfiguration` por entidade; chaves alternativas; `snake_case` |
| F02-006 | Configurar PostgreSQL | `docker-compose.yml`, `.env.example`, user-secrets, Options com validação da connection string |
| F02-007 | Criar DbContext | `ProjectManagerDbContext` com filtros globais por organização; `ITenantContext` na Application |
| F02-008 | Migrations | `dotnet-ef` local; migration inicial; teste de modelo sem alterações pendentes |
| F02-009 | Auditoria de persistência | Interceptor `CreatedAt`/`UpdatedAt` (UTC via `TimeProvider`) |
| F02-010 | Índices | Únicos: `organizations.slug`, `(organization_id, key)` em projetos, `(project_id, position)` em fases; índices das FKs |
| F02-011 | Restrições de integridade | NOT NULL, tamanhos, FKs compostas, `ON DELETE RESTRICT`, `xmin` |
| F02-012 | Testar persistência | Gravação, leitura, auditoria, unicidade, concorrência otimista, migrations aplicáveis em banco vazio |
| F02-013 | Testar isolamento | Leitura filtrada entre organizações; gravação bloqueada sem organização ativa ou de outra organização; FK composta rejeita fase ligada a projeto de outra organização mesmo contornando a aplicação (SQL direto) |
| — | Health check e documentação | `/health` com verificação `database`; ADRs 002–004; README; PROJECT_STATUS; relatório |

## Arquivos previstos

```
docker-compose.yml, .env.example, .config/dotnet-tools.json
src/ProjectManager.Domain/Common/            Entity, IOrganizationOwned, IAuditable
src/ProjectManager.Domain/Organizations/     Organization
src/ProjectManager.Domain/Projects/          Project, ProjectPhase
src/ProjectManager.Application/Abstractions/ ITenantContext
src/ProjectManager.Infrastructure/Persistence/
    ProjectManagerDbContext.cs, DatabaseOptions.cs
    Configurations/*.cs, Interceptors/*.cs, Migrations/*
src/ProjectManager.API/                      Health check do banco, connection string por configuração
tests/ProjectManager.UnitTests/Domain/       Invariantes das entidades
tests/ProjectManager.IntegrationTests/Persistence/  Testes com PostgreSQL (Testcontainers)
docs/decisions/ADR-002-PERSISTENCIA-EF-CORE-POSTGRESQL.md
docs/decisions/ADR-003-ISOLAMENTO-MULTI-TENANT.md
docs/decisions/ADR-004-TESTES-COM-POSTGRESQL.md
```

## Critérios de aceitação

| Critério (especificação mestre) | Como será verificado |
|---|---|
| Banco funcional | API conecta ao PostgreSQL local; `/health` com `database` = Healthy; 503 com o banco parado |
| Migrations aplicáveis | Migration aplicada em banco vazio (teste e `dotnet ef database update`); teste de modelo sem mudanças pendentes |
| Relacionamentos consistentes | Testes de gravação e leitura dos relacionamentos; FKs verificadas no esquema |
| Índices adequados | Unicidades testadas; índices conferidos no esquema gerado |
| Restrições de integridade | Testes de violação (NOT NULL, unicidade, FK, concorrência) |
| Testes de persistência | Suíte `Persistence` aprovada local e no CI |
| Testes de isolamento | Suíte de isolamento aprovada local e no CI |
| Nenhum acesso cross-tenant permitido | Leitura e gravação cross-tenant bloqueadas pela aplicação **e** pelo banco |

## Estratégia de testes

- **Unitários:** invariantes de domínio (sem banco).
- **Integração com Testcontainers:** um contêiner PostgreSQL por execução da suíte, com migrations aplicadas e dados isolados por teste (organizações com IDs próprios).
- **API:** `WebApplicationFactory` apontando para o contêiner; o `/health` reporta `self` e `database`.
- **Mutação manual:** remover temporariamente o filtro de organização ou a FK composta e confirmar que os testes de isolamento falham.
- **CI:** os mesmos testes no `ubuntu-latest`, com Docker nativo.

## Riscos

| Risco | Mitigação |
|---|---|
| Docker parado localmente impede os testes de integração | Mensagem clara de pré-requisito; README documenta; CI sempre com Docker |
| Filtro global contornado (`IgnoreQueryFilters`, SQL bruto) | FKs compostas no banco; revisão; teste com SQL direto; RLS avaliado em ADR |
| Tempo de CI maior com contêiner | Um contêiner compartilhado por suíte |
| Antecipar campos das Fases 05 e 06 | Entidades mínimas; campos completos nas fases próprias |
| UUID v7 expõe o instante de criação | Aceitável para IDs internos; registrado no ADR |

## Definição de conclusão

Todos os critérios de aceitação verificados com evidência (local e CI), testes de isolamento comprovadamente sensíveis por mutação, ADRs 002 a 004 registrados, documentação, `PROJECT_STATUS.md` e relatório da Fase 02 atualizados, e alterações versionadas em `feature/fase-02-persistencia`.
