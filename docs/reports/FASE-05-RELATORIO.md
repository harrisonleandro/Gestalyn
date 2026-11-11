# Relatório final: Fase 05, Projetos e equipes

- **Data:** 2026-11-11
- **Ambiente:** Windows 11 Pro · .NET SDK 10.0.404 · Docker 29.8.2 · PostgreSQL 18 · Mailpit v1.29
- **Branch:** `feature/fase-05-projetos` (a partir de `feature/fase-04-organizacoes`)
- **Estado:** **Completed.** Critérios verificados localmente e no CI; integrada em `develop` pelo PR #5 (merge `6a32f58`).
- **Formato:** seção 17 da [especificação mestre](../specifications/ESPECIFICACAO-MESTRE.md)

## 1. Nome e número da fase

Fase 05: Projetos e equipes. A especificação da fase está em [FASE-05-PROJETOS.md](../specifications/FASE-05-PROJETOS.md).

## 2. Objetivo

Gerenciamento de projetos dentro da organização: criar, listar, consultar, editar e arquivar, com equipe própria, papéis de projeto, responsável, datas, prioridade, etiquetas e repositórios associados. Todo acesso é validado no backend pela organização ativa e pela participação na equipe.

## 3. Resumo das implementações

- **Domínio:**
  - `Project` completo: prioridade, início e prazo, arquivamento lógico somente leitura, etiquetas e repositórios;
  - `ProjectMember` (papel fixo e responsável);
  - `ProjectTag`;
  - `ProjectRepository` (URL normalizada e provedor);
  - `ProjectPermissions` e `ProjectRole`.
- **Permissões (D1):** `projects.create`, `projects.read_all` e `projects.manage_all` no catálogo da organização. Proprietário e Administrador recebem as três; Membro, `projects.create`.
- **Acesso efetivo (D1 e D2):** `ProjectAccessGrant.Compute` soma o papel na organização e o papel na equipe. É aplicado pelo `IProjectAccess` (rotas por ID) e pelo filtro da listagem. Projeto invisível responde 404.
- **Organização nas rotas por ID:** cabeçalho `X-Gestalyn-Organization` no `ActiveOrganizationMiddleware`, validado como a rota. `[ProjectPermission]` responde 400, 404 ou 403.
- **Equipe (D3):**
  - FK composta `(organization_id, user_id)` → `organization_members`, com cascata;
  - responsável único por índice parcial, trocado em transação;
  - adicionar, listar, alterar papel e remover.
- **Repositórios (D4):** só metadados. Apenas `https`, sem credenciais, consulta ou fragmento; até 20 por projeto.
- **Etiquetas (D5):** únicas sem diferenciar maiúsculas e minúsculas, até 20 por projeto; filtro `?tag=`.
- **API:**
  - `/api/v1/organizations/{id}/projects`: criar e listar, com paginação e filtros;
  - `/api/v1/projects/{id}`: consultar, editar, arquivar, equipe e repositórios;
  - enumerações pelo nome no JSON.
- **Migration** `AddProjectsAndTeams`:
  - colunas novas em `projects`;
  - tabelas `project_members`, `project_tags` e `project_repositories`;
  - chave alternativa em `organization_members`;
  - *check constraint* de datas;
  - migração de dados das permissões para os papéis de sistema existentes.
- **Documentação:** ADR-010, `docs/api/PROJECTS.md`, ARCHITECTURE (§3.4 com diagrama ER), PROJECT_STRUCTURE, README, AGENTS, ADR-009 (nota), especificação e status.

## 4. Arquivos criados

| Área | Arquivos |
|---|---|
| Domain | `Projects/{ProjectMember,ProjectTag,ProjectRepository,ProjectPermissions}.cs` |
| Application | `Common/PagedResult.cs`, `Projects/{IProjectService,ProjectContracts,ProjectAccess}.cs` |
| Infrastructure | `Projects/{ProjectService,ProjectAccessResolver}.cs`, migration `20261111210021_AddProjectsAndTeams` |
| API | `Controllers/ProjectsController.cs`, `Organizations/ProjectPermissionAttribute.cs` |
| Testes | `UnitTests/Domain/ProjectRulesTests.cs`; `IntegrationTests/Projects/{ProjectTestKit,ProjectTests,ProjectAccessTests,ProjectIsolationTests,ProjectMigrationTests}.cs` |
| Documentação | `docs/specifications/FASE-05-PROJETOS.md`, `docs/decisions/ADR-010-PROJETOS-E-EQUIPES.md`, `docs/api/PROJECTS.md`, este relatório |

## 5. Arquivos modificados

| Arquivo | Alteração |
|---|---|
| `Domain/Projects/Project.cs` | Prioridade, datas, arquivamento, etiquetas e repositórios; projeto arquivado somente leitura |
| `Domain/Organizations/{OrganizationPermissions,Role}.cs` | Três permissões de projeto; Membro passa a criar projetos |
| `Infrastructure/Persistence/Configurations/{ProjectConfiguration,OrganizationModuleConfigurations}.cs` | Mapeamento das novas entidades; chave alternativa `(organization_id, user_id)` em `organization_members` |
| `Infrastructure/Persistence/ProjectManagerDbContext.cs`, `DependencyInjection.cs` | Novos `DbSet`; registro de `IProjectService` e `IProjectAccess` |
| `API/Organizations/ActiveOrganizationMiddleware.cs` | Leitura do cabeçalho `X-Gestalyn-Organization` quando a rota não traz a organização |
| `API/Program.cs` | `JsonStringEnumConverter` (sem números) |
| `README.md`, `AGENTS.md`, `docs/architecture/*`, `docs/decisions/ADR-009-*.md`, `docs/specifications/README.md`, `docs/PROJECT_STATUS.md` | Documentação da fase |

## 6. Dependências adicionadas

Nenhuma. `Npgsql` (códigos de erro do PostgreSQL) e `System.Text.Json` já estavam disponíveis.

## 7. Justificativas técnicas

- **Papéis de projeto fixos com permissões no código (D2):** atendem F05-007 sem tabelas de papéis por organização. Como a autorização é por permissão, os papéis personalizados podem entrar depois só com uma migration.
- **FK composta com cascata para `organization_members` (D3):** o banco garante que a equipe só tem membros da organização e que quem sai da organização sai das equipes, sem código de limpeza.
- **Responsável como marcação na participação:** some junto com a participação. Uma coluna em `projects` exigiria uma FK composta que não pode ser anulada.
- **Um único cálculo de acesso:** `ProjectAccessGrant.Compute` é a única regra, e duas mutações confirmam que os testes a protegem.
- **`PATCH` com presença explícita:** os setters registram se `description`, `startDate` e `dueDate` vieram no corpo, para distinguir "limpar" (`null`) de "manter" (ausente).
- **SQL na migration:** é a única forma de levar o catálogo novo às organizações existentes. É idempotente, testado e justificado no ADR-010, como exige o AGENTS.md.

## 8. Testes executados

| Suíte | Novos nesta fase | Conteúdo |
|---|---|---|
| UnitTests (`ProjectRulesTests`) | 13 métodos, 24 casos | Prioridade padrão, datas, etiquetas (unificação, limites, substituição), normalização e recusa de URLs, duplicidade de repositório, arquivamento somente leitura, permissões por papel, herança de organização, cálculo de acesso e permissões dos papéis de sistema |
| IntegrationTests (`ProjectTests`) | 6 | Criação com dados completos e padrão, validações, `PATCH` parcial com `null`, arquivamento lógico, listagem com filtros e paginação, repositórios |
| IntegrationTests (`ProjectAccessTests`) | 6 | Visibilidade por equipe, matriz de papéis de projeto em todos os endpoints, permissão de criação, equipe só com membros e responsável único, saída da organização em cascata, cabeçalho de organização |
| IntegrationTests (`ProjectIsolationTests`) | 4 | 404 em todos os endpoints para dono de outra organização (com os dois cabeçalhos), filtro de leitura das novas tabelas, validação de gravação e FKs compostas recusando vínculos entre organizações |
| IntegrationTests (`ProjectMigrationTests`) | 1 | Banco migrado até a Fase 04 com uma organização antiga e depois até o fim: permissões novas nos papéis certos e prioridade `Medium` nos projetos antigos |

## 9. Resultado do build

`dotnet restore`: exit 0. `dotnet build -c Release`: **0 avisos, 0 erros**, com analisadores `Recommended` e avisos tratados como erros.

## 10. Resultado dos testes

| Suíte | Resultado |
|---|---|
| UnitTests | **59/59** |
| IntegrationTests (PostgreSQL 18 e Mailpit via Testcontainers) | **77/77** |
| Total | **136/136** |

## 11. Evidências

- **Mutação 1:** a listagem sem o filtro de equipe faz 2 testes falharem (`OrganizationMember_SeesOnlyProjectsOfTheirTeams_WhileAdministratorsSeeAll` e `ProjectCreation_RequiresThePermission`).
- **Mutação 2:** o `ProjectAccessResolver` tratando todo membro da organização como Observador faz o teste de visibilidade falhar. As duas mutações foram desfeitas e conferidas.
- **Execução real** (API em `http://localhost:5100`, migration aplicada no banco local, três usuários reais com e-mail confirmado pelo Mailpit):

  | Verificação | Resultado |
  |---|---|
  | Criar projeto | 201: prioridade `High`, etiquetas `Cliente,Web` (unificadas), `myRole=Manager` |
  | Membro fora da equipe lê o projeto | 404 |
  | Requisição sem cabeçalho de organização | 400 |
  | Intruso com o cabeçalho da própria organização ou da organização da dona | 404 nos dois casos |
  | Intruso adicionado à equipe | 404 |
  | Membro como Colaborador e responsável | 201; ele passa a ver só esse projeto (total 1) |
  | Colaborador edita | 200, com `dueDate: null` limpando o prazo |
  | Colaborador arquiva | 403 |
  | Repositório `https://GitHub.com/gestalyn/portal.git/` | Gravado como `https://github.com/gestalyn/portal` (GitHub) |
  | Repositório com credencial na URL | 400 |
  | Arquivar e depois editar | 204 e 409 |
  | Listagem padrão | Omite o arquivado; `includeArchived=true` o inclui |
  | Membro sai da organização | A equipe passa a ter só a dona |

  O banco confirmou datas, etiquetas, equipe e repositório. A organização criada na Fase 04 recebeu as permissões novas pela migration; o Leitor, como previsto, não.

## 12. Problemas encontrados

1. Na primeira execução real, o terceiro usuário recebeu 401 e o "intruso na equipe" recebeu 400. O roteiro passou do limite de 10 requisições de autenticação por minuto (Fase 03): o login do terceiro usuário foi recusado e o ID dele ficou vazio.
2. CA1861 (matrizes constantes como argumento) nos testes de integração.
3. No CI do GitHub, a execução do PR #3 terminou com falha na etapa "Publicar resultados dos testes": o serviço de artefatos respondeu HTML em vez de JSON nas 5 tentativas. Restauração, build e testes passaram. Uma execução manual terminou com `startup_failure`, e o CI do PR #4 continua na fila.

## 13. Problemas corrigidos

1. Pausas no roteiro para respeitar a janela do rate limiting. A segunda execução teve todos os resultados esperados. O rate limiting funcionou como projetado.
2. Listas em vez de matrizes nos corpos de teste.
3. A falha do CI não estava no código nem no workflow: o PR #3 passou na 2ª tentativa. A execução presa do PR #4 foi substituída por uma execução manual sobre o mesmo commit, também aprovada.

## 14. Pendências

- Restauração de projetos arquivados e saída voluntária de uma equipe: não previstas; avaliar na Fase 09, com a interface.
- Interface Web de projetos (Fase 09) e comandos `gestalyn project` (Fase 11).

## 15. Critérios atendidos

| Critério | Evidência |
|---|---|
| Projetos funcionais | `ProjectTests` (6) e execução real |
| Membros associados corretamente | FK composta com cascata, `Team_AcceptsOnlyOrganizationMembers_AndKeepsASingleResponsible`, `LeavingTheOrganization_RemovesThePersonFromProjectTeams`, `CompositeForeignKeys_RejectLinksAcrossOrganizations` |
| Permissões aplicadas | `ProjectRoles_GrantTheirPermissions_InEveryEndpoint`, `ProjectCreation_RequiresThePermission`, visibilidade e mutações |
| Organização validada | `OrganizationHeader_IsRequired_AndValidatedAgainstTheMembership`, `OwnerOfAnotherOrganization_GetsNotFoundOnEveryProjectEndpoint` |
| Arquivamento lógico | `Archive_IsLogical_ReadOnly_AndHiddenFromTheDefaultList` (linha conferida no banco) |
| Testes de acesso e integridade | `ProjectIsolationTests` (4), `ProjectMigrationTests` e mutações |

## 16. Critérios não atendidos

Nenhum. A definição de conclusão também foi cumprida: CI aprovado ([run 43107035853](https://github.com/harrisonleandro/Gestalyn/actions/runs/43107035853)) e integração por PR (PR #5, merge `6a32f58`).

## 17. Riscos identificados

| Risco | Impacto | Mitigação |
|---|---|---|
| Regra de acesso a projeto duplicada em fases futuras (tarefas, documentos) | Visibilidade divergente | `ProjectAccessGrant.Compute` como regra única, registrada no AGENTS.md e no ADR-010 |
| Cascata de `organization_members` para `project_members` | A remoção da organização apaga as participações sem histórico | Auditoria na Fase 08 |
| Cálculo de acesso a cada requisição por ID | Uma consulta a mais | Consulta por chave primária com índice `(project_id, user_id)`; medir na Fase 13 |
| Instabilidade do serviço de artefatos do GitHub | CI falha mesmo com testes aprovados | Avaliar `continue-on-error` na publicação dos resultados, com aprovação do responsável |

## 18. Estado do Git

- **Branch:** `feature/fase-05-projetos`, enviada e incorporada pelo [PR #5](https://github.com/harrisonleandro/Gestalyn/pull/5).
- **Commits:**
  - `7af13d3` (especificação);
  - `c30da85` (projetos, equipes, testes e migration);
  - commit de documentação de encerramento.
- **PR #3 (Fase 03):** CI aprovado na 2ª tentativa; merge `647573d`.
- **PR #4 (Fase 04):** CI aprovado (execução manual sobre `b7b170e`); merge `55926bc`.
- **PR #5 (Fase 05):** CI aprovado sobre `8659a93`; merge `6a32f58`.
- **`develop`:** `6a32f58`.

## 19. Próxima fase recomendada

1. **Antes de tudo:**
   - push desta branch, CI e PR para `develop`.
2. **Fase 06, Fases, marcos e tarefas:**
   - `ProjectPhase` completa e `Milestone`;
   - tarefas com o mesmo cálculo de acesso ao projeto e o cabeçalho `X-Gestalyn-Organization`;
   - etiquetas de tarefas;
   - permissões de tarefa por papel de projeto.
