# ADR-008: Organização ativa e leitura entre organizações

- **Status:** Aceito
- **Data:** 2026-11-11
- **Fase:** 04 (Organizações e permissões). Decisões D1 e D4 aprovadas pelo responsável.

## Contexto

O ADR-003 garante o isolamento a partir de uma "organização ativa" (`ITenantContext`), que até a Fase 03 ficava sempre vazia. A especificação mestre exige que a organização ativa seja validada pela identidade e pelas permissões do usuário autenticado, nunca pelo `OrganizationId` enviado pelo cliente, e prevê a alternância entre organizações. Ao mesmo tempo, algumas consultas precisam olhar além de uma única organização: "minhas organizações" e a aceitação de convite.

## Decisão

### Organização ativa (D1)

- Rotas de recursos da organização incluem o identificador: `/api/v1/organizations/{organizationId}/...`.
- O `ActiveOrganizationMiddleware` (API) lê `{organizationId}`, consulta a associação **do usuário do token** e só então chama `TenantContext.SetActiveMembership(...)`. A associação traz o papel e as permissões.
- **Sem associação, a organização ativa fica vazia:** os filtros não retornam dados e os endpoints respondem **404**, sem revelar que a organização existe.
- Trocar de organização é apenas chamar rotas de outra organização; não exige novo login nem novo token. Remover um membro tem efeito imediato.
- **Cabeçalho `X-Gestalyn-Organization`:** aprovado para rotas por ID de recurso (`/projects/{id}`), mas será implementado na Fase 05, junto com a primeira rota que precisar dele, para não criar código sem uso. Seguirá a mesma validação.
- `TenantContext.UseOrganization(id)` (interno) ativa uma organização sem associação **somente** em dois casos com prova própria: a criação da organização (o usuário é o criador) e a aceitação de convite (o token do convite prova o vínculo).

### Leitura entre organizações (D4)

- O `MembershipDirectory` é o **único** componente que usa `IgnoreQueryFilters()`, em três consultas, cada uma restrita a uma prova de vínculo:

  | Consulta | Restrição |
  |---|---|
  | `FindMembershipAsync` | Associação do `userId` do token a uma organização |
  | `ListOrganizationsAsync` | Organizações do `userId` do token |
  | `FindInvitationByTokenHashAsync` | Convite pelo hash do token recebido por e-mail |

- Nenhum outro código pode ignorar os filtros (regra no AGENTS.md).
- **Row-Level Security continua adiado para a Fase 13.** As três camadas do ADR-003 continuam valendo e os testes cross-tenant agora usam usuários reais.

## Alternativas consideradas

| Alternativa | Motivo da rejeição |
|---|---|
| Organização como claim do token | Trocar de organização exigiria novo token, e a remoção de um membro só valeria quando o token expirasse |
| Só cabeçalho, sem rota | Menos legível e diverge dos contratos da especificação (`/organizations/{id}/...`) |
| `IgnoreQueryFilters` espalhado pelos serviços | Cada uso seria uma possível falha de isolamento sem um ponto central de revisão |
| RLS agora | Exige a organização por conexão e políticas específicas para criação, "minhas organizações" e convites; o custo é alto sem ganho mensurável nesta fase |

## Consequências

- **Custo por requisição:** cada requisição a uma organização faz uma consulta de associação (por índice `organization_id, user_id`), além da verificação de sessão.
- **Evidência:** com a mutação "associação sem filtrar pelo usuário" no `MembershipDirectory`, 6 testes falham, inclusive os de isolamento (relatório da Fase 04).
