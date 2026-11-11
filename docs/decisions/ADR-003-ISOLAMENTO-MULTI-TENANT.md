# ADR-003: Isolamento multi-tenant em camadas

- **Status:** Aceito
- **Data:** 2026-11-10
- **Fase:** 02 (Persistência e modelo de domínio)

## Contexto

A especificação mestre (seção 2.3) define banco e schema compartilhados, `OrganizationId` nas entidades da organização, validação no backend, relacionamentos que impeçam associações entre organizações e testes automatizados de isolamento. Ela proíbe confiar apenas no `OrganizationId` enviado pelo cliente ou pelo agente. Row-Level Security (RLS) é opcional, após avaliação técnica.

## Decisão

O isolamento é garantido em três camadas independentes.

1. **Leitura: filtro global de consulta.** Toda entidade `IOrganizationOwned` recebe automaticamente um filtro `OrganizationId == organização ativa`. A própria `Organization` só é visível quando é a ativa. A organização ativa vem de `ITenantContext` (camada Application), resolvido no backend. **Sem organização ativa, nenhuma consulta retorna dados de organizações** (falha segura).
2. **Gravação: validação antes do `SaveChanges`.** Inserções, alterações e exclusões de entidades `IOrganizationOwned` exigem organização ativa igual à da entidade. Alterar o `OrganizationId` de uma entidade existente é proibido. Criar uma nova `Organization` é permitido; alterá-la ou excluí-la exige que seja a ativa. Violações lançam `CrossTenantAccessException`.
3. **Banco: chaves estrangeiras compostas.** Entidades filhas referenciam o pai por `(organization_id, <pai>_id)` → `(organization_id, id)`, com chave alternativa única no pai. **Mesmo SQL escrito à mão não consegue ligar dados de organizações diferentes.**

Regras complementares:

- `ITenantContext` padrão (`UnresolvedTenantContext`) não tem organização ativa. A resolução a partir da identidade autenticada e do pertencimento validado entra na Fase 04.
- Toda nova entidade pertencente a organização deve implementar `IOrganizationOwned`, para receber o filtro automaticamente, e usar FK composta para seus pais. O teste `Model_EveryOrganizationOwnedEntityHasOrganizationQueryFilter` protege a primeira regra.

## Row-Level Security: avaliação

RLS adicionaria uma quarta barreira no banco, cobrindo também SQL bruto e `IgnoreQueryFilters`. **Não foi adotado nesta fase** porque:

- exige definir a organização ativa por conexão (`SET app.organization_id`), o que se complica com pool de conexões e operações sem organização ativa (migrations, criação de organizações);
- sem usuários e sessões (Fases 03 e 04), a política seria testada de forma artificial.

A decisão será reavaliada na Fase 04, quando a organização ativa for resolvida pela identidade, ou na Fase 13 (hardening).

## Alternativas consideradas

| Alternativa | Motivo da rejeição |
|---|---|
| Apenas filtro na aplicação | Um único descuido (SQL bruto, `IgnoreQueryFilters`, bug no filtro) vazaria dados; sem garantia no banco |
| Schema ou banco por organização | Contraria a decisão aprovada (banco e schema compartilhados) e aumenta o custo operacional |
| RLS já na Fase 02 | Complexidade sem identidade para testá-la de forma realista |

## Consequências

- Código de aplicação nunca filtra por organização manualmente; o filtro é automático.
- Operações administrativas entre organizações (ex.: suporte) exigirão um caminho explícito e auditado. Não existem por enquanto.
- Evidência: 8 testes de isolamento, 3 deles sensíveis à remoção dos filtros, 4 à remoção da validação de gravação, e a FK composta verificada por SQL direto (veja o relatório da Fase 02).
