# Fase 04: Organizações e permissões

- **Estado:** Completed. Validada localmente e no CI, integrada em `develop` pelo PR #4 (merge `55926bc`). Decisões D1 a D5 aprovadas pelo responsável em 2026-11-11.
- **Referência:** [Especificação mestre](ESPECIFICACAO-MESTRE.md), seção 9 (Fase 04), seções 4.1 e 4.2 (modelo funcional), 5.2 (contratos) e 2.3 (multi-tenancy)
- **Branch:** `feature/fase-04-organizacoes` (a partir de `feature/fase-03-identidade`; o PR #3 da Fase 03 está aberto para `develop`)
- **Pré-requisito para implementar:** Fase 03 integrada em `develop`, com o CI aprovado

## Objetivo

Tornar a plataforma multiusuário e multi-organização: criar organizações, gerenciar membros e convites, aplicar papéis e permissões no backend e resolver a organização ativa a partir da identidade autenticada. O isolamento entre organizações precisa ser comprovado por testes com usuários reais.

## Escopo

1. **Organizações:** criar (o criador vira proprietário), listar as do usuário, consultar e editar (`/api/v1/organizations`).
2. **Organização ativa** (D1): resolvida a cada requisição a partir da identidade autenticada e da associação ativa, substituindo o `UnresolvedTenantContext`.
3. **Membros** (`OrganizationMember`): listar, alterar papel e remover, com a regra de que sempre há ao menos um proprietário. O membro também pode sair da organização.
4. **Convites** (`Invitation`, D3): criar (com envio de e-mail), listar pendentes, revogar e aceitar; vinculados ao e-mail convidado.
5. **Papéis e permissões** (`Role`, `Permission`, `RolePermission`, D2): catálogo de permissões, papéis de sistema por organização e autorização por permissão no backend.
6. **Leitura entre organizações restrita** (D4): só para "minhas associações", documentada em ADR.
7. **Web mínima** (D5): página de aceitação de convite (destino do link do e-mail).
8. **Testes:** organização, membros, convites, autorização (positivos e negativos) e isolamento cross-tenant com usuários reais.
9. **Documentação:** ADRs de organização ativa e de autorização, `docs/api/ORGANIZATIONS.md` e atualização de `docs/security`.

## Fora de escopo

- `ProjectMember` e papéis de projeto (Fase 05).
- Papéis personalizados criados pelo usuário: o modelo de dados os suporta, mas não haverá endpoints nesta fase.
- Exclusão de organização e transferência de cobrança (não há cobrança no MVP).
- Interface completa de organizações (lista, troca, membros e convites na Web): Fase 09.
- Permissões de agentes (Fase 10).
- Row-Level Security: continua adiado (veja D4).

## Decisões (D1 a D5 aprovadas)

| ID | Proposta | Alternativas |
|---|---|---|
| **D1** Organização ativa | Rotas de recursos da organização incluem o ID (`/organizations/{organizationId}/...`), e esse valor só vira organização ativa **depois de validar que o usuário autenticado é membro ativo**. Para rotas por ID de recurso (`/projects/{id}`, Fase 05+), o cliente informa a organização no cabeçalho `X-Gestalyn-Organization`, validado da mesma forma. Sem associação válida, a organização ativa fica vazia e nada é lido. Trocar de organização não exige novo login. | (a) Organização como claim do token: trocar exigiria novo token e a revogação de associações só valeria quando o token expirasse. (b) Só cabeçalho, sem rota: menos legível e diverge dos contratos da especificação (`/organizations/{id}/...`). |
| **D2** Papéis e permissões | **Catálogo de permissões definido no código** e persistido em `permissions` (via migration): `organization.read`, `organization.update`, `members.read`, `members.invite`, `members.update_role`, `members.remove`. Tabela `roles` **por organização**, com papéis de sistema criados com a organização: **Proprietário** (tudo), **Administrador** (tudo exceto promover ou rebaixar proprietários), **Membro** (leitura) e **Leitor** (leitura). Tabela `role_permissions` liga papéis e permissões. A autorização é feita por **permissão**, nunca pelo nome do papel, para que papéis personalizados funcionem sem mudar código. | (a) Papéis fixos no código, sem tabelas: simples, mas contraria as entidades da especificação e impede papéis personalizados. (b) Papéis personalizados já com endpoints: amplia o escopo sem critério da fase que o exija. |
| **D3** Convites | O convite fica vinculado a um **e-mail**, com token de uso único (só o hash é guardado), expira em **7 dias** e pode ser revogado. É aceito por um usuário autenticado **cujo e-mail confirmado seja igual ao do convite**. Quem não tem conta se cadastra com esse e-mail e depois aceita. Só pode haver um convite pendente por e-mail e organização; convidar alguém que já é membro retorna conflito. | Convite por link aberto, aceito por qualquer usuário logado: o link vazado daria acesso a terceiros. |
| **D4** Leitura entre organizações | Um único componente (`MembershipDirectory`) pode ler associações **sem o filtro de organização**, e **sempre restrito ao `userId` do token**. Ele serve para listar "minhas organizações" e para validar a organização ativa. É a exceção ao uso de `IgnoreQueryFilters`, registrada em ADR e coberta por teste. **RLS continua adiado** para a Fase 13: as três camadas do ADR-003 continuam valendo e os testes cross-tenant passam a usar usuários reais. | RLS agora: exige a organização por conexão e políticas para os caminhos "sem organização" (criação, minhas associações), com custo alto sem ganho mensurável nesta fase. |
| **D5** Interface Web | **Somente a página de aceitação de convite** (`/convites/aceitar`), que é o destino do link do e-mail. Exige login; depois do login, volta para a página. Lista, troca de organização e gestão de membros na Web ficam para a Fase 09. | (a) Nenhuma página Web: o link do e-mail apontaria para uma página inexistente. (b) Interface completa agora: duplica a Fase 09. |

Decisões de baixo risco já tomadas (serão registradas em ADR):

- **Criar organização:** qualquer usuário autenticado pode criar uma organização e vira Proprietário.
- **Status das respostas:**
  - **404** quando o usuário não é membro da organização, para não revelar que ela existe;
  - **403** quando é membro, mas não tem a permissão;
  - **409** em conflitos (último proprietário, convite duplicado, usuário já membro).
- **Remoção de membros:** é física, ou seja, a linha é apagada e não marcada como inativa. O histórico e a auditoria ficam para a Fase 08.
- **Concorrência:** o `xmin` protege as alterações de papel.

## Contratos previstos (`/api/v1`)

| Método e rota | Permissão |
|---|---|
| `POST /organizations` | autenticado |
| `GET /organizations` (minhas organizações, com meu papel) | autenticado |
| `GET /organizations/{id}` | `organization.read` |
| `PATCH /organizations/{id}` | `organization.update` |
| `GET /organizations/{id}/members` | `members.read` |
| `PATCH /organizations/{id}/members/{userId}` (alterar papel) | `members.update_role` |
| `DELETE /organizations/{id}/members/{userId}` | `members.remove` (ou o próprio usuário, para sair) |
| `GET /organizations/{id}/roles` | `members.read` |
| `POST /organizations/{id}/invitations` | `members.invite` |
| `GET /organizations/{id}/invitations` | `members.invite` |
| `DELETE /organizations/{id}/invitations/{invitationId}` (revogar) | `members.invite` |
| `POST /invitations/accept` (`{ token }`) | autenticado, com e-mail confirmado igual ao do convite |

## Dependências

| Item | Estado |
|---|---|
| Fase 03 (identidade, sessão, e-mail) integrada em `develop` | PR #3 aberto, CI em execução |
| Docker (PostgreSQL e Mailpit) | Instalado; reiniciado nesta sessão |
| Pacotes novos | Nenhum previsto |

## Tarefas

| ID | Tarefa | Subtarefas |
|---|---|---|
| F04-001 | Criar organizações | `POST /organizations`; papéis de sistema e associação do criador como Proprietário numa só transação |
| F04-002 | Listar organizações do usuário | `MembershipDirectory` (D4); `GET /organizations` com o papel do usuário |
| F04-003 | Selecionar organização ativa | Resolução por rota ou cabeçalho com validação de associação (D1), substituindo o `UnresolvedTenantContext` |
| F04-004 | Implementar membros | Entidade `OrganizationMember` (`IOrganizationOwned`, FK composta para o papel); listagem |
| F04-005 | Implementar convites | Entidade `Invitation`; criação com e-mail, listagem, revogação, expiração |
| F04-006 | Implementar aceitação de convites | `POST /invitations/accept`; validação de e-mail confirmado; página Web (D5) |
| F04-007 | Implementar papéis | Entidade `Role` por organização; papéis de sistema |
| F04-008 | Implementar permissões | Catálogo `Permission`, `RolePermission`, migration com os dados |
| F04-009 | Implementar autorização | Requisito de permissão nos endpoints; 404 para não membros e 403 para membros sem permissão |
| F04-010 | Implementar remoção de membros | `DELETE`; proteção do último proprietário; sair da organização |
| F04-011 | Implementar alteração de papéis | `PATCH`; regras de proprietário; concorrência |
| F04-012 | Criar testes de isolamento | Usuários reais em organizações diferentes; leitura, gravação, convites e membros cross-tenant |

## Arquivos previstos

```
src/ProjectManager.Domain/Organizations/       OrganizationMember, Invitation, Role, Permission (catálogo), regras
src/ProjectManager.Application/Organizations/  Contratos, IOrganizationService, IMembershipDirectory, Permissions
src/ProjectManager.Infrastructure/Organizations/  Serviços, MembershipDirectory, configurações EF
src/ProjectManager.Infrastructure/MultiTenancy/   Resolução da organização ativa (substitui UnresolvedTenantContext)
src/ProjectManager.Infrastructure/Persistence/Migrations/  Migration de membros, convites, papéis e permissões
src/ProjectManager.API/Controllers/            OrganizationsController, InvitationsController
src/ProjectManager.API/Authorization/          Requisito de permissão e resolução de organização
src/ProjectManager.Web/Components/Pages/Invitations/AcceptInvitation.razor
tests/ProjectManager.UnitTests/Domain/         Regras de membros, convites e papéis
tests/ProjectManager.IntegrationTests/Organizations/  Fluxos, autorização e isolamento
docs/decisions/ADR-008-ORGANIZACAO-ATIVA.md, ADR-009-AUTORIZACAO-POR-PERMISSAO.md
docs/api/ORGANIZATIONS.md
```

## Critérios de aceitação

| Critério (especificação mestre) | Como será verificado |
|---|---|
| Usuários podem pertencer a organizações diferentes | Um usuário em duas organizações com papéis distintos; `GET /organizations` lista ambas |
| Organizações não acessam dados umas das outras | Membro da organização A recebe 404 em todos os endpoints da organização B; os filtros e as FKs continuam valendo |
| Convites funcionam | Convite enviado por e-mail (Mailpit), aceito pelo e-mail correto, recusado para outro e-mail, expirado ou revogado, e de uso único |
| Permissões são validadas no backend | Cada endpoint testado com papel que tem e que não tem a permissão |
| Usuários sem permissão recebem respostas apropriadas | 404 para não membro, 403 para membro sem permissão, 409 para conflitos, sempre em Problem Details |
| Testes cross-tenant passam | Suíte de isolamento com usuários reais, local e no CI; mutação na validação de associação detectada |

## Estratégia de testes

- **Unitários:** regras de domínio (último proprietário, expiração e uso único do convite, permissões dos papéis de sistema).
- **Integração** (PostgreSQL e Mailpit via Testcontainers): fluxos completos pela API com usuários reais; matriz de permissões por papel; isolamento cross-tenant.
- **Mutação:** remover a validação de associação na resolução da organização ativa deve fazer os testes de isolamento falharem.
- **Web:** aceitação de convite pelo navegador.

## Riscos

| Risco | Mitigação |
|---|---|
| A exceção de leitura entre organizações (D4) virar porta de vazamento | Componente único, sempre filtrado pelo `userId` do token; teste específico; regra no AGENTS.md |
| Perder o último proprietário | Regra de domínio, verificação transacional e teste |
| Convite aceito por quem não é o destinatário | Exige e-mail confirmado igual ao do convite; teste negativo |
| Ataque por enumeração de organizações | 404 para não membros |
| Explosão de combinações de permissões | Catálogo pequeno e testes de matriz por papel |

## Definição de conclusão

Todos os critérios verificados com evidência (local e CI), mutação da validação de associação detectada, ADRs registrados, documentação da API e de segurança, README, AGENTS.md, `PROJECT_STATUS.md` e relatório da Fase 04 atualizados, e alterações integradas por PR.


## Execução e ajustes em relação ao plano

| ID | Estado | Evidência |
|---|---|---|
| F04-001 | Completed | `POST /organizations`: criador Proprietário, 4 papéis de sistema; `Create_MakesTheCreatorOwnerWithSystemRoles` |
| F04-002 | Completed | `GET /organizations` via `MembershipDirectory`; `User_BelongsToSeveralOrganizationsWithDifferentRoles`, `OrganizationList_ShowsOnlyTheUsersOwnOrganizations` |
| F04-003 | Completed | `ActiveOrganizationMiddleware` + `TenantContext` (substitui `UnresolvedTenantContext`); mutação detectada |
| F04-004 | Completed | `OrganizationMember` com FK composta para o papel; listagem de membros |
| F04-005 | Completed | Convite com e-mail real (Mailpit), listagem e revogação; conflitos 409 |
| F04-006 | Completed | `POST /invitations/accept`; página `/convites/aceitar` validada no navegador (e-mail errado recusado, destinatário aceito) |
| F04-007 | Completed | `Role` por organização; papel de sistema único por tipo (índice parcial) |
| F04-008 | Completed | Catálogo de 6 permissões gravado pela migration; `role_permissions` com FK para o catálogo |
| F04-009 | Completed | `[OrganizationPermission]`: 404 não membro, 403 sem permissão; `Member_CanReadButNotManage` |
| F04-010 | Completed | Remoção e saída; último proprietário protegido (409) |
| F04-011 | Completed | Alteração de papel com regras de proprietário e trava otimista; `Administrator_ManagesMembersButNotOwners` |
| F04-012 | Completed | `OrganizationIsolationTests` (4) com usuários reais; mutação no `MembershipDirectory` derruba 6 testes |
| CI | Completed | [Run 43106437450](https://github.com/harrisonleandro/Gestalyn/actions/runs/43106437450) (manual, sobre `b7b170e`, porque a execução do PR ficou presa na fila do GitHub): success; merge do PR #4 em `develop` (`55926bc`) |

Ajustes feitos durante a implementação, dentro das decisões aprovadas:

1. **Cabeçalho `X-Gestalyn-Organization` (D1) adiado para a Fase 05:** nenhuma rota desta fase é por ID de recurso; implementá-lo agora criaria código sem uso. A rota `{organizationId}` cobre todos os endpoints desta fase.
2. **O `MembershipDirectory` também localiza o convite pelo hash do token:** a aceitação precisa encontrar o convite antes de haver organização ativa. Foi mantido no mesmo componente único (D4), documentado no ADR-008.
3. **Consulta de membros:** a ordenação e o filtro foram movidos para antes da projeção, porque o EF Core não traduz acesso a propriedades de um record projetado. O defeito foi encontrado pelos testes (500 na listagem de membros).
4. **`RefreshTokens` renomeado para `OpaqueTokens` (`Infrastructure/Common`):** o mesmo gerador serve aos refresh tokens e aos convites.
5. **`ApiRequests` na Web:** o envio e a leitura de Problem Details foram extraídos do `ApiAccountClient` e são compartilhados com o cliente de convites.