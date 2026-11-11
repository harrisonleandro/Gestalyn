# Fase 05: Projetos e equipes

- **Estado:** In Review. Implementada e validada localmente (testes, mutação e execução real da API); faltam o CI no GitHub e a integração em `develop` (as Fases 03 e 04 já estão integradas). Decisões D1 a D5 aprovadas pelo responsável em 2026-11-11.
- **Referência:** [Especificação mestre](ESPECIFICACAO-MESTRE.md), seção 9 (Fase 05), seções 4.2 e 4.3 (modelo funcional) e 5.3 (contratos)
- **Branch:** `feature/fase-05-projetos` (a partir de `feature/fase-04-organizacoes`; os PRs #3 e #4 estão abertos para `develop`)
- **Pré-requisito para implementar:** decisões aprovadas. A integração em `develop` depende das Fases 03 e 04 integradas, com o CI aprovado.

## Objetivo

Implementar o gerenciamento de projetos dentro da organização: criar, listar, consultar, editar e arquivar projetos, com equipe própria, papéis de projeto, responsável, datas, prioridade, etiquetas e repositórios associados. Todo acesso é validado no backend pela organização ativa e pela participação no projeto.

## Escopo

1. **Projetos:** criar, listar (com filtros e paginação), consultar, editar e arquivar logicamente (`/api/v1/organizations/{organizationId}/projects` e `/api/v1/projects/{id}`).
2. **Organização em rotas por ID de recurso:** cabeçalho `X-Gestalyn-Organization`, aprovado no [ADR-008](../decisions/ADR-008-ORGANIZACAO-ATIVA.md) e implementado agora, com a mesma validação de associação da rota.
3. **Visibilidade e acesso** (D1): quem pode ver e administrar cada projeto.
4. **Equipe do projeto** (`ProjectMember`, D2 e D3): adicionar, listar, alterar o papel e remover membros; papéis de projeto; responsável.
5. **Datas e prioridade:** início, prazo e prioridade.
6. **Etiquetas** (`ProjectTag`, D5) e **repositórios associados** (`ProjectRepository`, D4).
7. **Permissões novas** no catálogo e migração de dados para os papéis de sistema das organizações existentes.
8. **Testes:** fluxos, matriz de permissões (papel na organização × papel no projeto), cabeçalho de organização, arquivamento e isolamento cross-tenant das novas entidades.
9. **Documentação:** ADR-010, `docs/api/PROJECTS.md` e atualização de ARCHITECTURE, README e AGENTS.md.

## Fora de escopo

- Fases do projeto (`ProjectPhase` completa) e marcos (`Milestone`): Fase 06.
- Etiquetas de tarefas: Fase 06, junto com as tarefas.
- Progresso, atividades, dashboards e estatísticas do projeto: Fases 07 e 08.
- Auditoria e histórico de alterações: Fase 08.
- Interface Web de projetos: Fase 09.
- Acesso de agentes a projetos (`AgentProjectAccess`): Fase 10.
- Comandos `gestalyn project ...`: Fase 11.
- Integração real com o GitHub (leitura de commits, PRs, webhooks): fora do MVP desta fase (veja D4).
- Exclusão física de projetos e restauração de projetos arquivados (veja as decisões de baixo risco).

## Decisões (D1 a D5 aprovadas)

| ID | Proposta | Alternativas |
|---|---|---|
| **D1** Visibilidade e acesso | Três permissões novas no catálogo da organização: **`projects.create`** (criar projetos), **`projects.read_all`** (ver todos os projetos) e **`projects.manage_all`** (administrar todos os projetos). Proprietário e Administrador recebem as três; Membro recebe `projects.create`; Leitor, nenhuma. **Quem não tem `projects.read_all` só vê os projetos em que está na equipe.** Projeto que o usuário não pode ver responde **404**, como o não membro da organização. Quem cria o projeto entra na equipe como Gerente. | (a) Todo membro da organização vê todos os projetos: simples, mas impede projetos restritos dentro da organização. (b) Só a equipe vê o projeto, inclusive para Proprietário e Administrador: a organização perderia o controle sobre os próprios projetos. |
| **D2** Papéis de projeto | Três **papéis fixos de projeto**, definidos no código: **Gerente** (tudo no projeto), **Colaborador** (ler e editar os dados do projeto, etiquetas e repositórios) e **Observador** (somente leitura). Cada papel concede **permissões de projeto** do catálogo: `project.read`, `project.update`, `project.archive`, `project.members.manage`. A autorização continua **por permissão, nunca pelo nome do papel** (ADR-009). `projects.manage_all` concede todas as permissões de projeto, e `projects.read_all` concede `project.read`. Persistência: coluna `project_role` em `project_members`. | (a) Reaproveitar a tabela `roles` com um escopo "projeto": permite papéis de projeto personalizados, mas exige papéis de sistema por organização, migração de dados e FKs a mais sem critério da fase que o peça. A troca futura é uma migration, porque a autorização já é por permissão. (b) Sem papéis de projeto, só a participação: contraria F05-007. |
| **D3** Equipe e responsável | Só **membros da organização** entram na equipe do projeto. O banco garante isso com uma FK composta `(organization_id, user_id)` → `organization_members (organization_id, user_id)` **com exclusão em cascata**: quem sai ou é removido da organização sai das equipes dos projetos. O **responsável** é um membro da equipe marcado como tal, com no máximo um por projeto (índice único parcial), e sai junto com a participação. Endpoints `PATCH` e `DELETE /projects/{id}/members/{userId}` complementam os da especificação, porque "gerenciar membros" exige alterar o papel e remover. | (a) Responsável como coluna em `projects`: a FK para o membro da organização não pode ser anulada com segurança numa chave composta (a coluna `organization_id` não pode ficar nula), e um responsável removido da organização ficaria órfão. (b) Vários responsáveis por projeto: basta retirar o índice único; ficou um só para manter claro quem responde pelo projeto. |
| **D4** Repositórios associados | Só **metadados**: URL `https` validada, nome de exibição opcional e provedor inferido do host (**GitHub**, **GitLab**, **Bitbucket** ou **Outro**). Única por projeto (URL normalizada, sem `.git` e sem barra final). Nenhuma chamada ao provedor e nenhuma credencial nesta fase. | (a) Integração com a API do GitHub agora: exige credenciais, OAuth app e tratamento de limites, sem critério da fase que o peça (planejado para fases futuras). (b) Só repositórios do GitHub: restringe sem necessidade, porque nada aqui depende do provedor. |
| **D5** Etiquetas | Etiquetas **do projeto**, em texto livre (até 50 caracteres), únicas por projeto sem diferenciar maiúsculas e minúsculas, com no máximo 20 por projeto. A listagem de projetos aceita o filtro `?tag=`. Enviadas como uma lista completa no `PATCH` do projeto (substitui o conjunto). | Catálogo de etiquetas da organização, com cor, ligado aos projetos: melhor para padronizar, mas depende da interface da Fase 09 para fazer sentido. Pode evoluir a partir deste modelo. |

Decisões de baixo risco já tomadas (serão registradas no ADR-010):

- **Cabeçalho `X-Gestalyn-Organization`** (ADR-008): obrigatório em `/projects/{id}`; sem o cabeçalho ou com valor inválido, **400**; organização da qual o usuário não é membro, ou projeto de outra organização, **404**.
- **Arquivamento:** `DELETE /projects/{id}` grava `ArchivedAt` e quem arquivou; o projeto arquivado fica **somente leitura** (alterações respondem **409**) e sai da listagem padrão (`?includeArchived=true` o inclui). Arquivar de novo responde **204** sem mudar nada. Não há restauração nesta fase; ela pode entrar na Fase 09, junto com a interface.
- **Dados do projeto:** nome (até 200), descrição (até 4.000), prioridade **Baixa, Média, Alta ou Crítica** (padrão Média), data de início e prazo como datas sem hora (`DateOnly`), com prazo igual ou posterior ao início.
- **Listagem:** paginada (`page`, `pageSize` com padrão 50 e máximo 100), ordenada por nome, com filtros `tag`, `priority` e `includeArchived`. Resposta `{ items, page, pageSize, totalCount }`.
- **Concorrência:** o `xmin` protege as alterações do projeto e da equipe; um conflito responde 409.
- **Migração de dados:** a migration acrescenta as permissões novas aos papéis de sistema das organizações existentes (Proprietário, Administrador e Membro). É SQL sobre dados de organizações, por isso fica justificada no ADR-010, como exige o AGENTS.md.

## Contratos previstos (`/api/v1`)

| Método e rota | Permissão |
|---|---|
| `POST /organizations/{organizationId}/projects` | `projects.create` (o criador vira Gerente) |
| `GET /organizations/{organizationId}/projects` | membro da organização; lista só os projetos visíveis (D1) |
| `GET /projects/{id}` | `project.read` |
| `PATCH /projects/{id}` (dados, datas, prioridade, etiquetas) | `project.update` |
| `DELETE /projects/{id}` (arquivar) | `project.archive` |
| `GET /projects/{id}/members` | `project.read` |
| `POST /projects/{id}/members` (`{ userId, role }`) | `project.members.manage` |
| `PATCH /projects/{id}/members/{userId}` (papel, responsável) | `project.members.manage` |
| `DELETE /projects/{id}/members/{userId}` | `project.members.manage` |
| `GET /projects/{id}/repositories` | `project.read` |
| `POST /projects/{id}/repositories` | `project.update` |
| `DELETE /projects/{id}/repositories/{repositoryId}` | `project.update` |

As rotas `/projects/{id}` exigem o cabeçalho `X-Gestalyn-Organization`. Não membro da organização ou do projeto (sem `projects.read_all`) recebe 404; com acesso de leitura, mas sem a permissão, 403.

## Dependências

| Item | Estado |
|---|---|
| Fase 04 (organizações, papéis e permissões) | In Review, PR #4 aberto; o CI do GitHub está na fila |
| Fase 03 | Completed; merge do PR #3 autorizado, aguardando o CI |
| Docker (PostgreSQL e Mailpit) | Instalado |
| Pacotes novos | Nenhum previsto |

## Tarefas

| ID | Tarefa | Subtarefas |
|---|---|---|
| F05-001 | Criar projetos | `POST`; o criador vira Gerente na mesma transação; `projects.create` |
| F05-002 | Listar projetos | Visibilidade (D1), paginação e filtros |
| F05-003 | Consultar detalhes | Cabeçalho `X-Gestalyn-Organization`; resolução do acesso ao projeto; `GET /projects/{id}` |
| F05-004 | Editar projetos | `PATCH` parcial; concorrência; bloqueio de projeto arquivado |
| F05-005 | Arquivar projetos | `DELETE` lógico; somente leitura; fora da listagem padrão |
| F05-006 | Gerenciar membros | `ProjectMember` com FK composta para o membro da organização (D3); adicionar, listar, alterar e remover |
| F05-007 | Implementar papéis de projeto | Gerente, Colaborador e Observador; permissões de projeto; atributo `[ProjectPermission]` (D2) |
| F05-008 | Implementar etiquetas | `ProjectTag`; normalização e limite; filtro na listagem (D5) |
| F05-009 | Implementar repositórios associados | `ProjectRepository`; validação e normalização da URL; provedor (D4) |
| F05-010 | Implementar responsáveis | Responsável único por projeto entre os membros da equipe (D3) |
| F05-011 | Implementar datas e prioridades | Início, prazo e prioridade, com validação |
| F05-012 | Criar testes | Domínio, fluxos, matriz de permissões, cabeçalho, arquivamento e isolamento cross-tenant |

## Arquivos previstos

```
src/ProjectManager.Domain/Projects/            Project (completo), ProjectMember, ProjectRole, ProjectPermissions,
                                               ProjectTag, ProjectRepository, ProjectPriority
src/ProjectManager.Domain/Organizations/       OrganizationPermissions (projects.create, read_all, manage_all)
src/ProjectManager.Application/Projects/       IProjectService, contratos, IProjectAccess
src/ProjectManager.Infrastructure/Projects/    ProjectService, ProjectAccess (permissões efetivas no projeto)
src/ProjectManager.Infrastructure/Persistence/Configurations/  Mapeamento das novas entidades
src/ProjectManager.Infrastructure/Persistence/Migrations/      Migration de projetos, equipe, etiquetas e repositórios
src/ProjectManager.API/Controllers/            ProjectsController
src/ProjectManager.API/Organizations/          ActiveOrganizationMiddleware (cabeçalho), ProjectPermissionAttribute
tests/ProjectManager.UnitTests/Domain/         Regras de projeto, equipe, etiquetas e repositórios
tests/ProjectManager.IntegrationTests/Projects/  Fluxos, permissões, cabeçalho, arquivamento e isolamento
docs/decisions/ADR-010-PROJETOS-E-EQUIPES.md
docs/api/PROJECTS.md
```

## Critérios de aceitação

| Critério (especificação mestre) | Como será verificado |
|---|---|
| Projetos funcionais | Criar, listar, consultar, editar e arquivar pela API, com datas, prioridade, etiquetas, repositórios e responsável |
| Membros associados corretamente | Só membros da organização entram na equipe (aplicação e FK no banco); quem sai da organização sai das equipes; responsável único |
| Permissões aplicadas | Matriz por papel na organização e no projeto em cada endpoint: 404 sem acesso, 403 sem permissão, 409 em conflito |
| Organização validada | Cabeçalho ausente (400), de outra organização (404) e projeto de outra organização com cabeçalho válido (404) |
| Arquivamento lógico | Projeto arquivado continua no banco, fora da listagem padrão, consultável e somente leitura |
| Testes de acesso e integridade | Leitura e gravação cross-tenant das novas entidades; FKs compostas recusam vínculos entre organizações; mutação na verificação de equipe detectada |

## Estratégia de testes

- **Unitários:** datas (prazo antes do início), prioridade, normalização e limite de etiquetas, validação e normalização de URL e provedor, projeto arquivado somente leitura, permissões de cada papel de projeto.
- **Integração** (PostgreSQL via Testcontainers): fluxos completos pela API com usuários reais; visibilidade por papel; matriz de permissões; cabeçalho de organização; remoção da organização propagada às equipes; isolamento cross-tenant de `projects`, `project_members`, `project_tags` e `project_repositories`; migração das permissões novas para organizações já existentes.
- **Mutação:** retirar a verificação de participação na equipe (D1) deve fazer os testes de visibilidade e isolamento falharem.
- **Execução real:** API em execução, chamadas reais com o cabeçalho e conferência no banco. Não há interface Web nesta fase.

## Riscos

| Risco | Mitigação |
|---|---|
| Projeto restrito visível para quem não está na equipe | Um único componente calcula o acesso efetivo (`ProjectAccess`); testes de visibilidade e mutação |
| Confiar no cabeçalho de organização enviado pelo cliente | O valor só vale depois de validar a associação, como a rota; o projeto é lido pelo filtro da organização ativa |
| Organizações existentes sem as permissões novas | Migração de dados testada sobre uma organização criada antes da migration |
| Equipe com usuário que saiu da organização | FK composta com cascata no banco e teste |
| CI do GitHub travado (execução do PR #3 parada em "in_progress" e `workflow_dispatch` com `startup_failure`) | Atrasa a integração das Fases 03 e 04, não o desenvolvimento local desta fase. As actions usadas existem nas tags referenciadas; a causa provável está do lado do GitHub. Se persistir, o responsável cancela a execução pela interface do GitHub. |

## Definição de conclusão

Todos os critérios verificados com evidência (local e CI), mutação da verificação de equipe detectada, ADR-010 registrado, `docs/api/PROJECTS.md`, README, AGENTS.md, ARCHITECTURE, `PROJECT_STATUS.md` e relatório da Fase 05 atualizados, e alterações integradas por PR depois das Fases 03 e 04.

## Execução e ajustes em relação ao plano

| ID | Estado | Evidência |
|---|---|---|
| F05-001 | Completed | `POST /organizations/{id}/projects`; criador como Gerente; `Create_MakesTheCreatorManager_WithDatesPriorityAndTags`, `ProjectCreation_RequiresThePermission` |
| F05-002 | Completed | Visibilidade por equipe, paginação e filtros; `List_FiltersByTagAndPriority_AndPaginatesByName`, `OrganizationMember_SeesOnlyProjectsOfTheirTeams_WhileAdministratorsSeeAll` |
| F05-003 | Completed | Cabeçalho `X-Gestalyn-Organization` no `ActiveOrganizationMiddleware`; `[ProjectPermission]`; `OrganizationHeader_IsRequired_AndValidatedAgainstTheMembership` |
| F05-004 | Completed | `PATCH` parcial (`null` limpa, ausência mantém); `Update_ChangesOnlyTheSentFields_AndNullClearsOptionalValues` |
| F05-005 | Completed | Arquivamento lógico, somente leitura, fora da listagem padrão; `Archive_IsLogical_ReadOnly_AndHiddenFromTheDefaultList` |
| F05-006 | Completed | `ProjectMember` com FK composta e cascata; `Team_AcceptsOnlyOrganizationMembers_AndKeepsASingleResponsible`, `LeavingTheOrganization_RemovesThePersonFromProjectTeams` |
| F05-007 | Completed | Gerente, Colaborador e Observador por permissão; `ProjectRoles_GrantTheirPermissions_InEveryEndpoint` |
| F05-008 | Completed | `ProjectTag` com unificação sem diferenciar maiúsculas e minúsculas e limite de 20; filtro `?tag=` |
| F05-009 | Completed | `ProjectRepository` com URL normalizada e provedor; `Repositories_AreNormalized_Unique_AndValidated` |
| F05-010 | Completed | Responsável único (índice parcial; troca em transação) |
| F05-011 | Completed | Prioridade, início e prazo, com *check constraint* no banco |
| F05-012 | Completed | 13 unitários novos (24 casos) e 17 de integração (fluxos, permissões, isolamento, FKs e migração); 2 mutações detectadas |
| CI | **In Review** | Depende do push de `feature/fase-05-projetos`, autorizado pelo responsável |

Ajustes feitos durante a implementação, dentro das decisões aprovadas:

1. **Chave alternativa em `organization_members`:** a unicidade `(organization_id, user_id)` passou de índice único a chave alternativa, porque é o alvo da FK composta das equipes. O efeito sobre os dados é o mesmo.
2. **Troca de responsável em transação:** o responsável anterior é desmarcado com `ExecuteUpdate` antes de gravar o novo, porque o índice único parcial não admite dois responsáveis nem por um instante.
3. **Enumerações pelo nome no JSON:** `JsonStringEnumConverter` na API, sem aceitar números, para `priority`, `role` e `provider`.
4. **Valor padrão da prioridade na migration:** a coluna `priority` foi criada com o padrão `Medium`, para os projetos existentes. É o único ajuste manual no arquivo gerado, coberto pelo `ProjectMigrationTests`.
5. **URL de repositório sem credenciais, consulta ou fragmento:** é uma restrição a mais que a D4, para não gravar tokens colados na URL.
6. **Rate limiting na execução real:** o roteiro com três usuários passou do limite de 10 requisições de autenticação por minuto e precisou de pausas. É o comportamento esperado da Fase 03, não um defeito.
