# Relatório final: Fase 04, Organizações e permissões

- **Data:** 2026-11-11
- **Ambiente:** Windows 11 Pro · .NET SDK 10.0.404 · Docker 29.8.2 · PostgreSQL 18 · Mailpit v1.29
- **Branch:** `feature/fase-04-organizacoes` (a partir de `feature/fase-03-identidade`; inclui a conclusão da Fase 03)
- **Estado:** **Completed.** Critérios verificados localmente e no CI; integrada em `develop` pelo PR #4 (merge `55926bc`).
- **Formato:** seção 17 da [especificação mestre](../specifications/ESPECIFICACAO-MESTRE.md)

## 1. Nome e número da fase

Fase 04: Organizações e permissões. A especificação da fase está em [FASE-04-ORGANIZACOES.md](../specifications/FASE-04-ORGANIZACOES.md).

## 2. Objetivo

Estrutura multiusuário e multi-organização: organizações, membros, convites, papéis e permissões validados no backend, e organização ativa resolvida pela identidade, com isolamento cross-tenant comprovado por testes com usuários reais.

## 3. Resumo das implementações

- **Domínio:**
  - catálogo `OrganizationPermissions` (6 permissões);
  - `Role` com papéis de sistema (Proprietário, Administrador, Membro, Leitor) e `RolePermission`;
  - `OrganizationMember`;
  - `Invitation` (vinculado ao e-mail, 7 dias, uso único, revogável).
- **Organização ativa (D1, ADR-008):**
  - o `ActiveOrganizationMiddleware` lê `{organizationId}` da rota e a ativa só com associação válida;
  - o `TenantContext` substitui o `UnresolvedTenantContext`.
- **Leitura entre organizações (D4):** o `MembershipDirectory` é o único `IgnoreQueryFilters`, restrito ao `userId` do token ou ao hash de um convite. RLS ficou para a Fase 13.
- **API:**
  - `/api/v1/organizations`: criar, listar as minhas, consultar, renomear, membros (listar, alterar papel, remover ou sair), papéis e convites (criar, listar, revogar);
  - `/api/v1/invitations/accept`;
  - `[OrganizationPermission]` com 404, 403 e 409 em Problem Details.
- **Regras (D2):**
  - autorização por permissão;
  - só proprietários mexem com proprietários;
  - a organização mantém ao menos um proprietário, com trava otimista por `xmin`.
- **Convites (D3):** e-mail real, aceitação só pelo e-mail confirmado convidado, conflitos para membro existente ou convite duplicado.
- **Web (D5):** página `/convites/aceitar`.
- **Migration** `AddOrganizationsAndPermissions`: FKs compostas `(organization_id, role_id)`, índices parciais e catálogo de permissões.
- **Documentação:** ADR-008, ADR-009, `docs/api/ORGANIZATIONS.md`, ARCHITECTURE (diagrama ER), PROJECT_STRUCTURE, README, AGENTS, especificação e status.

## 4. Arquivos criados

| Área | Arquivos |
|---|---|
| Domain | `Common/Timestamps.cs`, `Organizations/{OrganizationPermissions,Role,OrganizationMember,Invitation}.cs` |
| Application | `Common/Result.cs`, `Abstractions/MultiTenancy/IMembershipDirectory.cs`, `Organizations/{IOrganizationService,OrganizationContracts}.cs` |
| Infrastructure | `MultiTenancy/TenantContext.cs`, `Organizations/{MembershipDirectory,OrganizationService,OrganizationEmails,PermissionDefinition}.cs`, `Persistence/Configurations/OrganizationModuleConfigurations.cs`, migration `20261111202458_AddOrganizationsAndPermissions` |
| API | `Organizations/{ActiveOrganizationMiddleware,OrganizationPermissionAttribute}.cs`, `Common/ResultProblems.cs`, `Controllers/{OrganizationsController,InvitationsController}.cs` |
| Web | `Api/{ApiRequests,ApiInvitationClient}.cs`, `Components/Pages/Invitations/AcceptInvitation.razor` |
| Testes | `UnitTests/Domain/OrganizationMembershipTests.cs`; `IntegrationTests/Organizations/{OrganizationTestKit,OrganizationTests,InvitationTests,MemberPermissionTests,OrganizationIsolationTests}.cs` |
| Documentação | `docs/specifications/FASE-04-ORGANIZACOES.md`, `docs/decisions/ADR-008-*.md`, `ADR-009-*.md`, `docs/api/ORGANIZATIONS.md`, este relatório |

## 5. Arquivos modificados

| Arquivo | Alteração |
|---|---|
| `Application/Abstractions/MultiTenancy/ITenantContext.cs` | `Membership` (papel e permissões) e `ActiveMembership` |
| `Infrastructure/DependencyInjection.cs`, `Persistence/ProjectManagerDbContext.cs`, `DesignTimeDbContextFactory.cs` | `TenantContext`, módulo de organizações, novos `DbSet` |
| `Infrastructure/Identity/{RefreshTokens → Common/OpaqueTokens,SessionManager,UserSession}.cs` | Gerador de tokens compartilhado; helper de microssegundos |
| `MultiTenancy/UnresolvedTenantContext.cs` | **Removido**, substituído por `TenantContext` (D1) |
| `API/Program.cs` | Middleware de organização ativa |
| `Web/Api/ApiAccountClient.cs`, `Web/Program.cs` | Uso do `ApiRequests`; cliente de convites |
| `tests/.../TestTenantContext.cs`, `UnitTests/Authentication/TokenTests.cs` | `Membership`; `OpaqueTokens` |
| `README.md`, `AGENTS.md`, `docs/architecture/*`, `docs/PROJECT_STATUS.md`, `docs/specifications/README.md` | Documentação |

## 6. Dependências adicionadas

Nenhuma.

## 7. Justificativas técnicas

- **Organização na rota, não no token:** trocar de organização não exige novo login, e a remoção de um membro vale na hora (ADR-008).
- **404 para não membros:** evita a enumeração de organizações.
- **Autorização por permissão:** papéis personalizados (futuros) funcionam sem mudar código (ADR-009).
- **Trava otimista na organização:** impede que duas remoções ou rebaixamentos simultâneos deixem a organização sem proprietário, sem precisar de SQL bruto nem locks.
- **Índice único parcial nos convites:** o banco garante um único convite em aberto por e-mail e organização.
- **`Result` na camada Application:** erros de negócio esperados não usam exceções; a API os converte em 404, 403, 409 ou 400.

## 8. Testes executados

| Suíte | Testes | Verifica |
|---|---|---|
| `UnitTests/Domain/OrganizationMembershipTests` | 7 | Permissões dos papéis de sistema; papel de outra organização recusado; convite normaliza e-mail, expira em 7 dias, é de uso único e revogável; e-mail inválido |
| `OrganizationTests` | 4 | Criador Proprietário com 4 papéis; criar exige autenticação; usuário em duas organizações com papéis distintos; renomear exige permissão |
| `InvitationTests` | 5 | Convite por e-mail aceito pelo destinatário; outro e-mail recebe 403 (e o convite segue válido); uso único; revogado não é aceito; conflitos 409 |
| `MemberPermissionTests` | 4 | Matriz do papel Membro (8 casos); Administrador gerencia membros mas não proprietários; último proprietário (409) e saída com segundo proprietário; membro sai |
| `OrganizationIsolationTests` | 4 | Não membro recebe 404 em 8 endpoints; lista só as próprias organizações; papel de outra organização não é atribuível; proprietário de A não gerencia B |
| Fases anteriores | 71 | Arquitetura, domínio, CLI, tokens, persistência, isolamento por filtro, autenticação, health, OpenAPI, erros |

## 9. Resultado do build

```
dotnet restore                       → exit 0
dotnet build -c Release --no-restore → 0 Aviso(s), 0 Erro(s), exit 0
```

## 10. Resultado dos testes

```
Aprovado! – Com falha: 0, Aprovado: 35, Total: 35 - ProjectManager.UnitTests.dll (net10.0)
Aprovado! – Com falha: 0, Aprovado: 60, Total: 60 - ProjectManager.IntegrationTests.dll (net10.0)
```

O CI no GitHub ainda **não foi executado** para esta branch (§14).

## 11. Evidências

**Navegador (Web, API, PostgreSQL e Mailpit locais):**

| Passo | Resultado |
|---|---|
| Link do convite sem sessão | Redireciona ao login com `ReturnUrl` para o convite |
| Aceitar com a conta errada (proprietária) | "Este convite foi enviado para outro e-mail." |
| Aceitar com a conta convidada | "Você agora participa de Equipe Local como Membro." |
| Banco depois da aceitação | `organization_members`: convidado como Membro e proprietária como Proprietário; `invitations.accepted_at` preenchido |

**Migration local:** `AddOrganizationsAndPermissions` aplicada; `permissions` com as 6 permissões do catálogo.

**Mutação** (código restaurado depois): o `MembershipDirectory` passou a buscar a associação **sem filtrar pelo usuário do token**. Resultado: 6 testes falharam, entre eles `NonMember_GetsNotFoundOnEveryOrganizationEndpoint` e `OwnerOfOneOrganization_CannotManageMembersOfAnother`.

## 12. Problemas encontrados

1. **Analisadores:** CA1711 no nome `RolePermission` (sufixo reservado), CA1716 no tipo `Error` (palavra reservada) e IDE0060 no parâmetro de rota sem uso nas ações.
2. **Defeito:** a listagem de membros respondia 500, porque o EF Core não traduz ordenação nem filtro sobre um record projetado. Encontrado pelos testes.
3. **CI do PR #3 travado no GitHub:** os testes passaram, mas o job não encerrou e o GitHub recusa tanto o cancelamento ("já concluído") quanto a reexecução ("ainda em andamento").
4. **Merge do PR #3 bloqueado pelo controle de permissões** (merge sem pedido explícito), antes da autorização.
5. **Conflito de merge local** em `PROJECT_STATUS.md` e no índice de especificações, ao trazer a conclusão da Fase 03 para esta branch.

## 13. Problemas corrigidos

1. Supressão justificada para `RolePermission` (nome da especificação); `Error` renomeado para `ResultError`; parâmetros de rota sem uso removidos (o middleware lê a rota).
2. Filtro e ordenação antes da projeção; testes verdes.
3. Pendente do lado do GitHub. O merge foi adiado até o CI encerrar (§14).
4. O responsável autorizou o merge explicitamente.
5. Resolução manual mantendo ambos os estados (Fase 03 Completed, Fase 04 Planned na época).

## 14. Pendências

- **Merge do PR #3 (Fase 03):** autorizado; aguarda o encerramento da execução de CI travada (43095047963). Os testes dela passaram.
- **Push, CI e PR desta branch:** dependem de autorização.
- **Cabeçalho `X-Gestalyn-Organization`:** Fase 05.
- **Reavaliação de RLS:** Fase 13.
- **Papéis personalizados (endpoints):** quando houver necessidade.
- **Interface de organizações:** Fase 09.

## 15. Critérios atendidos

| Critério | Evidência |
|---|---|
| Usuários podem pertencer a organizações diferentes | `User_BelongsToSeveralOrganizationsWithDifferentRoles` |
| Organizações não acessam dados umas das outras | `OrganizationIsolationTests` (4) com usuários reais; filtros e FKs compostas; mutação detectada |
| Convites funcionam | `InvitationTests` (5) com e-mail real; aceitação no navegador |
| Permissões são validadas no backend | `MemberPermissionTests`; `[OrganizationPermission]` em cada endpoint |
| Usuários sem permissão recebem respostas apropriadas | 404, 403 e 409 em Problem Details, verificados por teste |
| Testes cross-tenant passam | 95/95 localmente |

## 16. Critérios não atendidos

Nenhum critério funcional está pendente. A confirmação no CI ainda falta, e por isso a fase está **In Review**.

## 17. Riscos identificados

| Risco | Impacto | Mitigação |
|---|---|---|
| Novo uso de `IgnoreQueryFilters` fora do `MembershipDirectory` | Vazamento entre organizações | Regra no AGENTS.md, ADR-008, revisão; RLS na Fase 13 |
| Permissões novas em fases futuras | Organizações existentes sem as novas permissões nos papéis de sistema | Cada fase que acrescentar permissões inclui migração de dados (ADR-009) |
| Consulta de associação a cada requisição | Latência | Índice `(organization_id, user_id)`; cache na Fase 13 se a medição justificar |
| Envio de e-mail síncrono no convite | Latência do SMTP na resposta | Fila quando houver Worker |

## 18. Estado do Git

- **Branch:** `feature/fase-04-organizacoes`, somente local.
- **Commits:**
  - `5b23eff` (plano);
  - merge da conclusão da Fase 03;
  - `ccad3a0` (status);
  - `10a900f` (backend);
  - `c347fd9` (Web);
  - commit de documentação de encerramento.
- **PR #3 (Fase 03):** aberto, CI travado no GitHub; merge autorizado. **`develop`:** `a8dae04`.

## 19. Próxima fase recomendada

1. **Antes de tudo:** merge do PR #3, depois push desta branch, CI e PR para `develop`.
2. **Fase 05, Projetos e equipes:**
   - projetos completos (responsáveis, datas, prioridade, arquivamento lógico);
   - membros e papéis de projeto (`ProjectMember`);
   - tags e repositórios associados;
   - cabeçalho `X-Gestalyn-Organization` para rotas `/projects/{id}`;
   - novas permissões de projeto no catálogo, com migração de dados para os papéis de sistema existentes.
