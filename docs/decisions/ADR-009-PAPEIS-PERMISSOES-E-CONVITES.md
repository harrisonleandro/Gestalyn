# ADR-009: Papéis, permissões e convites

- **Status:** Aceito
- **Data:** 2026-11-11
- **Fase:** 04 (Organizações e permissões). Decisões D2, D3 e D5 aprovadas pelo responsável.

## Contexto

A especificação mestre prevê `Role`, `Permission`, `RolePermission`, papéis organizacionais, permissões granulares, autorização no backend e convites com aceitação e revogação. Papéis de projeto (Fase 05) e permissões de agentes (Fase 10) virão depois e precisam caber no mesmo modelo.

## Decisão

### Papéis e permissões (D2)

- **Catálogo de permissões no domínio** (`OrganizationPermissions`), persistido em `permissions` pela migration:

  | Permissão | Uso |
  |---|---|
  | `organization.read` | Consultar os dados da organização |
  | `organization.update` | Alterar os dados da organização |
  | `members.read` | Consultar membros e papéis |
  | `members.invite` | Convidar pessoas e gerenciar convites |
  | `members.update_role` | Alterar o papel de membros |
  | `members.remove` | Remover membros |

- **Papéis por organização** (`roles`), criados junto com a organização:

  | Papel | Permissões |
  |---|---|
  | Proprietário | Todas |
  | Administrador | Todas |
  | Membro | `organization.read` e `members.read` |
  | Leitor | `organization.read` e `members.read` |

  A diferença entre Membro e Leitor aparece com projetos e tarefas (Fases 05 e 06). `role_permissions` liga papéis e permissões. Papéis personalizados usam o mesmo modelo (sem `system_role`), mas ainda não têm endpoints.

- **Autorização por permissão, nunca pelo nome do papel:** `[OrganizationPermission("...")]` nos endpoints.
  - **404** para quem não é membro;
  - **403** para membro sem a permissão;
  - **409** para conflitos de regra;
  - todas as respostas em Problem Details.

- **Regras de proprietário:**
  - só proprietários promovem, rebaixam, removem ou convidam proprietários;
  - a organização sempre mantém ao menos um proprietário;
  - operações que reduzem proprietários usam a organização como trava otimista (`xmin`), para que duas operações simultâneas não deixem a organização sem dono.

- **Sair da organização:** qualquer membro pode remover a si mesmo, exceto o último proprietário.

### Convites (D3)

- O convite fica vinculado a um e-mail, normalizado em minúsculas.
- O token tem 256 bits e só o hash SHA-256 é guardado (`OpaqueTokens`, o mesmo componente dos refresh tokens).
- Validade de **7 dias**, uso único e revogável.
- Só pode ser aceito por um usuário autenticado **cujo e-mail confirmado seja o convidado**. Para qualquer outro usuário, a resposta é 403 e o convite continua válido para o destinatário.
- **Conflitos (409):** convidar quem já é membro, ou criar um segundo convite pendente para o mesmo e-mail (índice único parcial). Convites expirados são revogados automaticamente ao criar um novo.
- **Respostas:** convite inválido, revogado, expirado ou já usado recebe 400 com mensagem neutra.

### Web (D5)

Só a página `/convites/aceitar` (protegida), que é o destino do link do e-mail. Sem login, a pessoa vai para o login e volta à página depois de entrar. A gestão completa de organizações na interface fica para a Fase 09.

## Alternativas consideradas

| Alternativa | Motivo da rejeição |
|---|---|
| Papéis fixos no código, sem tabelas | Contraria as entidades da especificação e impede papéis personalizados |
| Verificar o nome do papel nos endpoints | Acopla o código aos papéis de sistema; papéis personalizados exigiriam mudanças |
| Convite por link aberto | Um link vazado daria acesso a terceiros |
| 403 também para não membros | Revela que a organização existe (enumeração) |

## Consequências

- Novas funcionalidades acrescentam permissões ao catálogo, com migration de dados, e as atribuem aos papéis de sistema. Organizações existentes precisarão de migração de dados para receber as permissões novas, o que deve ser previsto em cada fase.
- O mesmo modelo de permissões servirá de base para papéis de projeto (Fase 05) e escopos de agentes (Fase 10).
