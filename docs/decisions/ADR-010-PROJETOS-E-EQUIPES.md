# ADR-010: Projetos, equipes e acesso a projetos

- **Status:** Aceito
- **Data:** 2026-11-11
- **Fase:** 05 (Projetos e equipes). Decisões D1 a D5 aprovadas pelo responsável.

## Contexto

A especificação mestre prevê projetos com responsáveis, membros, datas, prioridades, etiquetas e repositórios, além de papéis específicos de projeto e restrições de acesso por organização e por projeto (seções 4.2 e 4.3). Os contratos usam rotas por ID do projeto (`/projects/{id}`), sem a organização na rota. O [ADR-008](ADR-008-ORGANIZACAO-ATIVA.md) aprovou o cabeçalho `X-Gestalyn-Organization` para esse caso, e o [ADR-009](ADR-009-PAPEIS-PERMISSOES-E-CONVITES.md) fixou que a autorização é sempre por permissão.

## Decisão

### Visibilidade e acesso (D1)

Três permissões novas no catálogo da organização:

| Permissão | Uso | Papéis de sistema |
|---|---|---|
| `projects.create` | Criar projetos | Proprietário, Administrador, Membro |
| `projects.read_all` | Ver todos os projetos da organização | Proprietário, Administrador |
| `projects.manage_all` | Administrar todos os projetos da organização | Proprietário, Administrador |

- Quem não tem `projects.read_all` só vê os projetos em cuja equipe está, na listagem e nas rotas por ID.
- Projeto invisível responde **404**, como o não membro da organização; com acesso de leitura, mas sem a permissão, **403**.
- Quem cria o projeto entra na equipe como Gerente.
- Um único componente decide o acesso: `ProjectAccessGrant.Compute`, chamado pelo `IProjectAccess` (rotas por ID) e espelhado pelo filtro da listagem. A mutação dessa verificação derruba os testes de visibilidade.

### Papéis de projeto (D2)

Papéis fixos, definidos no código (`ProjectRole`) e gravados em `project_members.role`:

| Papel | Permissões de projeto |
|---|---|
| Gerente (`Manager`) | `project.read`, `project.update`, `project.archive`, `project.members.manage` |
| Colaborador (`Contributor`) | `project.read`, `project.update` |
| Observador (`Viewer`) | `project.read` |

As permissões efetivas são a soma do papel na organização e do papel na equipe: `projects.manage_all` concede todas as permissões de projeto, e `projects.read_all` concede `project.read`. Os endpoints usam `[ProjectPermission("...")]`, nunca o nome do papel. Para ter papéis de projeto personalizados no futuro, basta uma migration: o código já autoriza por permissão.

### Equipe e responsável (D3)

- `project_members` tem uma FK composta `(organization_id, user_id)` → `organization_members (organization_id, user_id)`, **com exclusão em cascata**. Por isso, só membros da organização entram na equipe, e quem sai da organização, ou é removido dela, sai também das equipes. Para servir de alvo da FK, a unicidade `(organization_id, user_id)` de `organization_members` passou de índice único a chave alternativa.
- O responsável é um membro da equipe com `is_responsible`. Há no máximo um por projeto, garantido por índice único parcial (`ix_project_members_responsible`).
- Para trocar o responsável, o anterior é desmarcado na mesma transação, antes de gravar o novo (`ExecuteUpdate`), porque o índice não admite dois responsáveis nem por um instante. Uma corrida entre duas trocas viola o índice e responde 409.
- `PATCH` e `DELETE /projects/{id}/members/{userId}` complementam os contratos da especificação, porque gerenciar membros (F05-006) exige alterar o papel e remover.

### Repositórios (D4)

Só metadados: URL `https` normalizada, nome opcional e provedor inferido do host (GitHub, GitLab, Bitbucket ou Outro).

- A URL é recusada se tiver credenciais, parâmetros de consulta ou fragmento, para que nada sensível seja gravado por engano.
- O host vai para minúsculas e o caminho perde o `.git` e a barra final.
- Cada URL é única no projeto, com no máximo 20 repositórios por projeto.
- Não há chamadas ao provedor nem credenciais.

### Etiquetas (D5)

`project_tags` tem chave `(project_id, normalized_name)`:

- texto livre de até 50 caracteres;
- únicas sem diferenciar maiúsculas e minúsculas, mantendo a primeira grafia informada;
- no máximo 20 por projeto;
- enviadas como conjunto completo no `PATCH`;
- filtro `?tag=` na listagem.

### Organização nas rotas por ID

- O `ActiveOrganizationMiddleware` lê a rota `{organizationId}` e, quando ela não existe, lê o cabeçalho `X-Gestalyn-Organization`. A associação é validada da mesma forma nos dois casos.
- Nas rotas `/projects/{id}`, o cabeçalho é obrigatório: sem ele, ou com valor inválido, a resposta é **400**.
- Projeto de outra organização, inclusive de outra organização do mesmo usuário, responde **404**, porque o projeto é lido pelo filtro da organização ativa.

### Demais regras

- **Arquivamento lógico:** `ArchivedAt` e `ArchivedByUserId`. O projeto arquivado fica somente leitura: dados, equipe e repositórios respondem 409. Ele sai da listagem padrão (`includeArchived=true` o inclui), e arquivar de novo responde 204 sem mudar nada. Não há restauração nesta fase.
- **Dados:**
  - prioridade `Low`, `Medium` (padrão), `High` ou `Critical`;
  - datas sem hora (`DateOnly`), com prazo igual ou posterior ao início, conferido também por *check constraint* no banco.
- **Listagem:** paginada (`page`, `pageSize` com padrão 50 e máximo 100), ordenada por nome, com filtros `tag`, `priority` e `includeArchived`.
- **Concorrência:** `xmin`. A edição marca o projeto como alterado, inclusive quando só as etiquetas mudam, e um conflito responde 409.
- **JSON:** enumerações trafegam pelo nome (`JsonStringEnumConverter`, sem aceitar números).

### Migração de dados (SQL sobre dados de organizações)

A migration `AddProjectsAndTeams` acrescenta as permissões novas aos papéis de sistema das organizações já existentes, com `INSERT ... SELECT` sobre `roles` (Proprietário e Administrador recebem as três; Membro, `projects.create`). O AGENTS.md exige justificativa para SQL bruto sobre dados de organizações:

- é a única forma de levar o catálogo novo às organizações criadas antes da migration;
- roda uma única vez, dentro da migration;
- não depende de organização ativa;
- é idempotente (`ON CONFLICT DO NOTHING`);
- é coberta por teste: o banco é migrado até a Fase 04, recebe uma organização antiga e depois é migrado até o fim.

Projetos antigos recebem a prioridade `Medium` pelo valor padrão da coluna.

## Alternativas consideradas

| Alternativa | Motivo da rejeição |
|---|---|
| Todo membro da organização vê todos os projetos | Impede projetos restritos dentro da organização |
| Só a equipe vê o projeto, inclusive Proprietário e Administrador | A organização perderia o controle sobre os próprios projetos |
| Papéis de projeto na tabela `roles`, com escopo de projeto | Papéis de sistema por organização, migração e FKs a mais sem critério que o exija |
| Responsável como coluna em `projects` | A FK composta não pode ser anulada com segurança (`organization_id` não pode ficar nulo) e o responsável removido da organização ficaria órfão |
| Integração com a API do GitHub agora | Exige credenciais, OAuth e tratamento de limites, sem critério da fase |
| Catálogo de etiquetas da organização | Depende da interface da Fase 09 para fazer sentido; pode evoluir deste modelo |
| Resolver a organização no servidor pelas associações do usuário | Seria uma segunda leitura sem filtro de organização, contra a regra do ADR-008 |

## Consequências

- Toda rota por ID de recurso das próximas fases (tarefas, fases, documentos) usa o cabeçalho `X-Gestalyn-Organization` e o mesmo cálculo de acesso ao projeto.
- Novas permissões de projeto entram em `ProjectPermissions`, e os papéis fixos passam a concedê-las sem migração.
- A remoção de um membro da organização apaga, por cascata, as participações dele nas equipes. O histórico dessas remoções fica para a auditoria da Fase 08.
