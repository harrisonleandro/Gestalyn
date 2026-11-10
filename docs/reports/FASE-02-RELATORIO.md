# Relatório final: Fase 02, Persistência e modelo de domínio

- **Data:** 2026-11-10
- **Ambiente:** Windows 11 Pro · .NET SDK 10.0.404 · Docker 29.8.2 · PostgreSQL 18 (`postgres:18-alpine`)
- **Branch:** `feature/fase-02-persistencia` (a partir de `develop`)
- **Estado:** **In Review.** Todos os critérios foram verificados localmente; a execução no CI do GitHub depende do push.
- **Formato:** seção 17 da [especificação mestre](../specifications/ESPECIFICACAO-MESTRE.md)

## 1. Nome e número da fase

Fase 02: Persistência e modelo de domínio. A especificação da fase está em [FASE-02-PERSISTENCIA.md](../specifications/FASE-02-PERSISTENCIA.md).

## 2. Objetivo

Construir a base de dados e o modelo de domínio: PostgreSQL, EF Core, migrations, auditoria básica, índices, restrições de integridade e testes de persistência e de isolamento multi-tenant, sem permitir acesso cross-tenant.

## 3. Resumo das implementações

- **Domínio:**
  - `Entity` com UUID v7; contratos `IOrganizationOwned` e `IAuditable`.
  - Entidades mínimas `Organization`, `Project` e `ProjectPhase`, com invariantes (decisão D1).
- **Application:** `ITenantContext` (organização ativa) e `CrossTenantAccessException`.
- **Infrastructure:**
  - `ProjectManagerDbContext` (Npgsql, `snake_case`) e configurações por entidade.
  - Isolamento em três camadas (D2): filtro global, validação antes da gravação e FK composta.
  - Auditoria `CreatedAt`/`UpdatedAt` via `TimeProvider`; concorrência otimista via `xmin`.
  - Migration `InitialCreate`.
  - Health check `database`; connection string obrigatória; `UnresolvedTenantContext` como padrão seguro.
- **Ambiente local (D3):**
  - `docker-compose.yml` (PostgreSQL 18, exposto em `127.0.0.1`), `.env.example` e user-secrets.
  - `dotnet-ef` como ferramenta local.
- **Testes (D4):** PostgreSQL real com Testcontainers, com migrations aplicadas. São 7 testes de persistência, 8 de isolamento e 3 de health check; os testes de API passaram a usar o banco real.
- **Documentação:**
  - ADR-002 (persistência), ADR-003 (isolamento) e ADR-004 (testes).
  - Diagrama ER em ARCHITECTURE.md.
  - README, AGENTS.md, PROJECT_STRUCTURE.md, a especificação da fase e PROJECT_STATUS.md.

## 4. Arquivos criados

| Área | Arquivos |
|---|---|
| Raiz | `docker-compose.yml`, `.env.example`, `dotnet-tools.json` |
| Domain | `Common/Entity.cs`, `Common/IAuditable.cs`, `Common/IOrganizationOwned.cs`, `Common/Guard.cs`, `Organizations/Organization.cs`, `Projects/Project.cs`, `Projects/ProjectPhase.cs` |
| Application | `Abstractions/MultiTenancy/ITenantContext.cs`, `Abstractions/MultiTenancy/CrossTenantAccessException.cs` |
| Infrastructure | `MultiTenancy/UnresolvedTenantContext.cs`, `Persistence/ProjectManagerDbContext.cs`, `Persistence/ProjectManagerDatabase.cs`, `Persistence/DatabaseOptions.cs`, `Persistence/DesignTimeDbContextFactory.cs`, `Persistence/ChangeTracking/{TenantIsolationRules,AuditingRules}.cs`, `Persistence/Configurations/{EntityConfigurationExtensions,OrganizationConfiguration,ProjectConfiguration,ProjectPhaseConfiguration}.cs`, `Persistence/Migrations/*` (3 arquivos gerados) |
| UnitTests | `Domain/OrganizationTests.cs`, `Domain/ProjectTests.cs` |
| IntegrationTests | `Infrastructure/{PostgresDatabaseFixture,TestTenantContext,ManualTimeProvider,TestData}.cs`, `Persistence/{PersistenceTests,TenantIsolationTests}.cs` |
| Documentação | `docs/specifications/FASE-02-PERSISTENCIA.md`, `docs/decisions/ADR-002-*.md`, `ADR-003-*.md`, `ADR-004-*.md`, este relatório |

## 5. Arquivos modificados

| Arquivo | Alteração |
|---|---|
| `Directory.Packages.props` | Pacotes de persistência e Testcontainers |
| `.editorconfig` | `generated_code = true` para `src/**/Migrations/*.cs` |
| `src/ProjectManager.Infrastructure/DependencyInjection.cs` e `.csproj` | Registro de DbContext, opções, `ITenantContext` e health check |
| `src/ProjectManager.API/ProjectManager.API.csproj` | `Microsoft.EntityFrameworkCore.Design` (somente ferramentas, `PrivateAssets=all`) e `UserSecretsId` |
| `tests/ProjectManager.UnitTests/*.csproj` | Referência ao Domain |
| `tests/ProjectManager.IntegrationTests/*.csproj`, `Infrastructure/ApiFactory.cs`, `Api/*Tests.cs` | Testes de API passaram a usar o PostgreSQL do Testcontainers; health check com `database` |
| `README.md`, `AGENTS.md`, `docs/architecture/*`, `docs/PROJECT_STATUS.md`, `docs/specifications/README.md` | Documentação da fase |

Nenhum arquivo foi excluído. Nenhum código de produção da Fase 01 foi alterado fora da composição da Infrastructure.

## 6. Dependências adicionadas

| Pacote ou ferramenta | Versão | Projeto |
|---|---|---|
| Microsoft.EntityFrameworkCore | 10.0.15 | Infrastructure |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 | Infrastructure |
| EFCore.NamingConventions | 10.0.1 | Infrastructure |
| Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore | 10.0.15 | Infrastructure |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.15 | Infrastructure |
| Microsoft.EntityFrameworkCore.Design | 10.0.15 | API (somente design-time) |
| Testcontainers.PostgreSql | 4.16.0 | IntegrationTests |
| dotnet-ef (ferramenta local) | 10.0.15 | Solução |
| Imagem Docker `postgres:18-alpine` | 18 | Ambiente local e testes |

## 7. Justificativas técnicas

- **EF Core + Npgsql:** decisões obrigatórias da especificação. As versões são alinhadas ao .NET 10; as linhas 11.x, do .NET 11, foram evitadas.
- **EFCore.NamingConventions:** `snake_case` evita aspas em todo SQL no PostgreSQL. É o pacote mantido pelo mesmo ecossistema do Npgsql.
- **Health check de EF Core:** a verificação `database` usa `CanConnectAsync` do próprio contexto e não exige pacote de terceiros.
- **Configuration.Abstractions:** permite ler `ConnectionStrings:ProjectManager` sem acoplar a Infrastructure ao ASP.NET Core.
- **Design só no API, como `PrivateAssets`:** é exigido pelo `dotnet ef` no projeto de inicialização e não é distribuído com a aplicação.
- **Testcontainers:** necessário para provar FKs compostas, `xmin`, `CHECK` e índices no PostgreSQL real (ADR-004).
- **Validação de gravação no `SaveChanges`:** o contexto já conhece a organização ativa e o relógio; um interceptor separado seria uma abstração sem ganho.
- **UUID v7, `xmin` e migrations explícitas:** veja o ADR-002.

## 8. Testes executados

| Suíte | Testes | Verifica |
|---|---|---|
| `UnitTests/Domain` | 11 | Nome obrigatório e limites, normalização, UUID v7, organização obrigatória, posições sequenciais, fase herda a organização |
| `IntegrationTests/Persistence/PersistenceTests` | 7 | Migrations aplicadas e modelo sem alterações pendentes; ida e volta com fases ordenadas; auditoria; concorrência `xmin`; `RESTRICT` (23001); índice único (23505); `CHECK` (23514) |
| `IntegrationTests/Persistence/TenantIsolationTests` | 8 | Leitura só da organização ativa; nada sem organização ativa; inserção, alteração e exclusão cross-tenant bloqueadas; FK composta rejeita SQL direto (23503); toda entidade de organização tem filtro |
| `IntegrationTests/Api/HealthEndpointTests` | 3 | 200 com banco; apenas `self` e `database`; 503 sem banco e sem vazar senha ou host |
| Demais (Fase 01) | 16 | Arquitetura, CLI, OpenAPI, Problem Details, agora com o banco real |

## 9. Resultado do build

```
dotnet restore                       → exit 0
dotnet build -c Release --no-restore → 0 Aviso(s), 0 Erro(s), exit 0
```

## 10. Resultado dos testes

```
Aprovado! – Com falha: 0, Aprovado: 23, Total: 23 - ProjectManager.UnitTests.dll (net10.0)
Aprovado! – Com falha: 0, Aprovado: 22, Total: 22 - ProjectManager.IntegrationTests.dll (net10.0)
```

O CI no GitHub Actions ainda **não foi executado** para esta branch (veja §14).

## 11. Evidências

**Migration gerada** (`dotnet ef migrations script`), trecho relevante:

```sql
CONSTRAINT ak_projects_organization_id_id UNIQUE (organization_id, id),
CONSTRAINT fk_projects_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id) ON DELETE RESTRICT
CONSTRAINT ck_project_phases_position_positive CHECK (position > 0),
CONSTRAINT fk_project_phases_projects_organization_id_project_id FOREIGN KEY (organization_id, project_id) REFERENCES projects (organization_id, id) ON DELETE RESTRICT
CREATE UNIQUE INDEX ix_project_phases_project_id_position ON project_phases (project_id, position);
```

`xmin` não gera coluna física; é a coluna de sistema do PostgreSQL.

**Banco local:** `dotnet ef database update` aplicou `20261110233818_InitialCreate`, e `\dt` listou `organizations`, `projects`, `project_phases` e `__EFMigrationsHistory`.

**API real contra o PostgreSQL local:**

| Situação | `/health` |
|---|---|
| Banco no ar | 200, `database` Healthy (cerca de 25 ms com `127.0.0.1`) |
| Banco parado (`docker compose stop`) | 503, `database` Unhealthy em 0,12 s; `self` Healthy |
| Banco religado | 200 novamente, sem reiniciar a API |
| Sem connection string (Production) | A API não inicia: `OptionsValidationException: A connection string ...` |

**Testes de mutação** (código restaurado depois de cada um):

| Mutação | Resultado |
|---|---|
| Remover os filtros globais por organização | 3 testes de isolamento falharam (leituras e cobertura do modelo) |
| Remover a validação de gravação | 4 testes de isolamento falharam (inserção, inserção sem contexto, alteração, exclusão) |
| Remover a FK composta no banco (transação desfeita no final) | Com a FK: `ERROR 23503 ... violates foreign key constraint`. Sem a FK: `INSERT 0 1`, aceito. Após o `ROLLBACK`, a constraint permanece e não há dados residuais. |

## 12. Problemas encontrados

1. O Docker Desktop estava instalado, mas parado.
2. Um script de shell com heredocs falhou por causa de aspas; nenhum arquivo foi criado.
3. IDE0051 no construtor privado de `ProjectPhase`, usado só pelo EF Core.
4. `HasColumnName` não foi encontrado: faltava o namespace relacional do EF Core.
5. O código gerado das migrations violava analisadores (IDE0161, CA1861), e o build falhou com avisos tratados como erros.
6. `migrations script --no-build` usou um assembly anterior à migration e gerou um script vazio.
7. A expectativa de erro estava errada: `ON DELETE RESTRICT` gera `23001`, não `23503`.
8. O health check levava de 2 a 3 s com `Host=localhost` no Windows (tentativa IPv6 em um contêiner que só escuta em IPv4).

## 13. Problemas corrigidos

1. Docker Desktop iniciado.
2. Arquivos escritos diretamente, sem heredoc.
3. Supressão pontual com `Justification`.
4. `using Microsoft.EntityFrameworkCore`.
5. `generated_code = true` apenas para `src/**/Migrations/*.cs`.
6. Script gerado de novo, com build.
7. O teste agora verifica `RestrictViolation` (23001), com comentário explicando.
8. Connection string local com `127.0.0.1`, documentada no README.

## 14. Pendências

- **CI no GitHub:** a branch precisa ser enviada para confirmar que os testes com Testcontainers passam no `ubuntu-latest`. Isso exige autorização de push.
- **Integração em `develop`** (PR ou merge), com autorização.
- **Autor das alterações** (`CreatedBy`/`UpdatedBy`): depende de identidade (Fases 03 e 08).
- **Resolução da organização ativa pela identidade e reavaliação de RLS:** Fase 04.
- **Itens herdados:** nome comercial; `AllowedHosts`; escapes não ASCII no JSON do `/health` (o `+` da versão aparece como `+`).

## 15. Critérios atendidos

| Critério | Evidência |
|---|---|
| Banco funcional | API conectada ao PostgreSQL local; `/health` com `database` Healthy, e 503 com o banco parado |
| Migrations aplicáveis | Aplicada em banco vazio local e no contêiner; `Migrations_AreFullyAppliedAndMatchTheModel` |
| Relacionamentos consistentes | `Project_WithPhases_RoundTripsInPositionOrder`; FKs na migration |
| Índices adequados | Índice único `(project_id, position)`, chave alternativa `(organization_id, id)` e índice da FK composta; teste de unicidade |
| Restrições de integridade | `RESTRICT`, `UNIQUE`, `CHECK`, `NOT NULL` e `xmin` testados |
| Testes de persistência | 7/7 (local) |
| Testes de isolamento | 8/8 (local), sensíveis às mutações |
| Nenhum acesso cross-tenant permitido | Bloqueado na leitura e na gravação (aplicação) e no relacionamento (banco) |

## 16. Critérios não atendidos

Nenhum critério funcional está pendente. A **confirmação no CI** de que os testes de persistência e de isolamento passam fora desta máquina ainda está pendente, e por isso a fase está **In Review**, não Completed.

## 17. Riscos identificados

| Risco | Impacto | Mitigação |
|---|---|---|
| Testes de integração dependem de Docker | Sem Docker local, a suíte de integração não roda | Documentado no README, AGENTS.md e ADR-004; o CI tem Docker |
| `IgnoreQueryFilters` ou SQL bruto contornam o filtro | Leitura cross-tenant | FKs compostas no banco; regra no AGENTS.md; RLS reavaliado na Fase 04 ou 13 |
| Novas entidades sem `IOrganizationOwned` ou sem FK composta | Lacuna de isolamento | Teste de cobertura de filtros; regra no AGENTS.md; testes cross-tenant obrigatórios |
| Conflitos de `xmin` por manutenção do PostgreSQL | Raras exceções de concorrência legítimas | Tratar como conflito na API (ETag/412) quando houver endpoints |
| Imagem `postgres:18-alpine` sem patch fixo | Variação entre execuções | Fixar o digest na Fase 13 ou 14, se houver instabilidade |

## 18. Estado do Git

- **Branch:** `feature/fase-02-persistencia`, somente local, criada a partir de `develop` (`fe5d1aa`).
- **Commits da fase:**
  - `6a01965` planejamento;
  - `5805a32` domínio;
  - `73689b5` persistência;
  - `f803464` Docker Compose;
  - mais o commit de documentação de encerramento.
- **`develop` e `main`:** sem alterações (`fe5d1aa` e `c6bd32a`).
- **Arquivos locais fora do Git:** `.env` (ignorado) e user-secrets da API (fora do repositório).

## 19. Próxima fase recomendada

1. **Antes de tudo:** push da branch, CI aprovado e integração em `develop`. Só então a Fase 02 passa para Completed.
2. **Fase 03, Identidade e autenticação:**
   - ADR da estratégia de sessão da Web e de autenticação da API (cookies × tokens) e da recuperação de senha.
   - ASP.NET Core Identity com chave `Guid` no mesmo `DbContext`.
   - Autor das alterações na auditoria.
   - Proteção contra tentativas abusivas (rate limiting).
