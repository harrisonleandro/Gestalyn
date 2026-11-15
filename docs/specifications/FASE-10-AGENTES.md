# Fase 10: API e autenticação de agentes

- **Estado:** Planned. Aguarda a aprovação das decisões D1 a D10; nada foi implementado.
- **Referência:** [Especificação mestre](ESPECIFICACAO-MESTRE.md):
  - seção 9 (Fase 10);
  - seções 4.7 (inteligência artificial), 5.8 (contratos de agentes) e 6.1, 6.4 e 6.5 (REST, relatório de trabalho, idempotência e concorrência);
  - seção 7 (segurança).
- **Branch:** `feature/fase-10-agentes`, a partir de `docs/fase-09-concluida` (`788a6a2`, com as Fases 01 a 09 integradas). Será rebaseada em `develop` depois do merge do PR #18, sem reescrever nada já enviado.
- **Pré-requisito para implementar:** decisões aprovadas

## Objetivo

Permitir que agentes de IA operem no Gestalyn com segurança:
- com identidade e credenciais próprias, independentes dos usuários;
- com acesso limitado a projetos e escopos explícitos;
- com toda operação registrada e protegida contra duplicação.

Os agentes trabalham sobre as mesmas tarefas, atividades e histórico que as pessoas, pelas mesmas regras do backend. Nunca aprovam o próprio trabalho.

## Situação atual

- **Autenticação:** só existe a de usuários (ADR-006):
  - JWT com `sub` e `sid`;
  - a sessão é conferida a cada requisição em `OnTokenValidated`;
  - um `AuthorizeFilter` global exige usuário autenticado.
- **Organização ativa:** `ActiveOrganizationMiddleware` resolve a organização pela rota ou pelo cabeçalho `X-Gestalyn-Organization`, e a associação do **usuário** pelo `MembershipDirectory`.
- **Acesso a projetos:** `ProjectAccessGrant.Compute` combina o papel na organização e o papel na equipe. Os serviços recebem o `ProjectAccessGrant` e usam `UserId` como autor.
- **`ActorKind.Agent`** já existe no domínio:
  - `TransitionRules` recusa a aprovação do próprio trabalho a agentes;
  - `ActivityEvent`, `ActivityLog` e `TaskStatusHistory` têm a coluna `actor_kind`;
  - hoje, porém, `HttpActivityContext` e `TaskService` gravam sempre `User`.
- **Autorias com chave estrangeira para `users`:**
  - `tasks.created_by_user_id`;
  - comentários, dependências, anexos, documentos, versões e decisões.

  `activity_events`, `activity_logs` e `task_status_history` guardam o autor **sem FK**.
- **Tarefas não têm controle de concorrência:** o `PATCH /tasks/{id}` sobrescreve. Os documentos já usam `expectedVersion`.
- **Ferramentas existentes:**
  - `OpaqueTokens` gera 32 bytes aleatórios em base64url e calcula o SHA-256;
  - o rate limiting existe só para a autenticação, por IP (`AuthRateLimiting`).
- **Interface de agentes:** transferida da Fase 09 (F09-018) para esta fase.

## Escopo

1. **Agentes provisionados** por pessoas autorizadas da organização, sem autorregistro (D1).
2. **Credenciais próprias**:
   - guardadas só como hash;
   - com prefixo visível, expiração obrigatória, revogação imediata e mais de uma ativa, para rotação (D2).
3. **Esquema de autenticação de agentes**, separado do de usuários. Um agente só alcança as rotas `/api/v1/agents/*` (D3).
4. **Escopos da credencial e acesso por projeto** (`AgentProjectAccess`), combinados numa regra única ao lado de `ProjectAccessGrant` (D4).
5. **Autoria de agentes** nas tarefas, no histórico, nos eventos e nas atividades, com `ActorKind.Agent` (D5).
6. **Endpoints de agentes:**
   - contexto, projetos e tarefas (listar, ler, criar, atualizar, mover);
   - atividades e relatórios de trabalho;
   - os contratos da seção 5.8, com os ajustes documentados (D6).
7. **Relatórios de trabalho** que distinguem o que foi declarado, aceito, testado, confirmado pelo CI e aprovado por uma pessoa (D7).
8. **Idempotência** por `Idempotency-Key` (`IdempotencyRecord`) e **concorrência** por versão nas tarefas (D8).
9. **Auditoria de operações** (`AgentOperation`) e **rate limiting** por credencial (D9).
10. **Interface web de agentes**, transferida da Fase 09 (D10):
    - agentes, credenciais e acesso a projetos;
    - operações recentes;
    - relatórios na tarefa e no projeto.
11. **Testes de segurança** (D10): revogação, expiração, escopos, projetos não autorizados, cross-tenant, aprovação do próprio trabalho, rotas de usuário e replay de idempotência.

## Fora de escopo

- **CLI** (Fase 11) e **servidor MCP** (Fase 12). Eles usarão estas credenciais e rotas.
- **Integração com GitHub e CI** (Fase 12). O nível "confirmado pelo CI" do relatório fica registrado como **não verificado** até lá (D7).
- **Agente como responsável de tarefa** (`task_assignments`), comentários, anexos, documentos e decisões escritos por agentes. São autorias com FK para `users`, sem pedido explícito na seção 4.7. Podem entrar depois, com o mesmo modelo da D5.
- **Aprovação por agentes**: nenhum escopo concede `tasks.approve` (D4). Uma eventual exceção configurável exige um ADR.
- **OAuth, client credentials ou tokens assinados para agentes** (D2).
- **Tempo real (SignalR):** reavaliado na D10, sem necessidade comprovada nesta fase.
- **Limpeza automática de registros expirados** (idempotência e operações): sem `Worker` nesta fase. A retenção fica para a Fase 14 (D8 e D9).

## Decisões (D1 a D10 para aprovação)

| ID | Proposta | Alternativas |
|---|---|---|
| **D1** Agente e provisionamento | **`Agent`** é uma entidade da organização (`IOrganizationOwned`), com: <br>- nome (único na organização) e descrição; <br>- estado `Active` ou `Disabled`; <br>- quem criou e quando. <br>Só é criado por **pessoas com a permissão nova `agents.manage`**, migrada para os papéis de sistema Proprietário e Administrador, com teste como em `ProjectMigrationTests`. **Não há autorregistro:** nenhuma rota anônima cria agente ou credencial. <br>O agente **não é membro** da organização: <br>- não ocupa a tabela de membros; <br>- não tem papel da organização; <br>- não entra na Web; <br>- só enxerga os projetos concedidos (D4). <br>Desativar um agente bloqueia todas as credenciais dele na hora; reativar não ressuscita credenciais revogadas. Os agentes não são excluídos, para preservar a autoria. | (a) Agente como linha em `users` (conta técnica): as FKs de autoria funcionariam sem migração, mas mistura duas identidades no Identity e abre o risco de um agente entrar pelo fluxo de senha. (b) Agente como membro da organização com um papel: as permissões da organização (como `projects.read_all`) vazariam para agentes. |
| **D2** Credenciais | **`AgentCredential`**: <br>- **token opaco** `gsk_` + 32 bytes aleatórios em base64url (`OpaqueTokens.Generate`), **mostrado uma única vez**, na criação; <br>- no banco ficam só o **SHA-256** do token (índice único) e um **prefixo** de identificação (os 8 primeiros caracteres depois de `gsk_`), mostrado nas listas; <br>- **expiração obrigatória**: padrão 90 dias, máximo 365, configurável em `Agents:Credentials`; <br>- **revogação imediata**, com data e autor; <br>- **até 3 credenciais ativas** por agente, para rotação sem parada; <br>- os escopos ficam na credencial (D4); <br>- o último uso é registrado com granularidade de 1 minuto, para não gravar a cada requisição. <br>O SHA-256 basta porque o token tem 256 bits de entropia: um hash lento só protege segredos escolhidos por pessoas. É o mesmo raciocínio dos refresh tokens (ADR-006). | (a) JWT assinado para agentes: a revogação exigiria lista negra ou tempo de vida curto com renovação, o que complica a CLI e o MCP. (b) Client credentials (OAuth): exige um servidor de autorização, fora do escopo do MVP. (c) Hash lento (PBKDF2 ou Argon2): custo por requisição sem ganho para um token aleatório. |
| **D3** Autenticação | **Esquema próprio `AgentToken`**, com `Authorization: Bearer gsk_...`. <br>- **Roteamento:** um seletor de esquema escolhe pelo prefixo; tokens sem `gsk_` seguem para o JWT. <br>- **A cada requisição, o handler confere:** o hash, a expiração, a revogação e o estado do agente. Uma falha responde 401 genérico, sem dizer o motivo. <br>- **Claims:** `sub` (ID do agente), `org` (organização do agente), `cid` (ID da credencial), `actor_kind=agent` e os escopos. <br>- **Separação total:** <br> - a política global de usuários continua exigindo o esquema JWT; <br> - as rotas `/api/v1/agents/*` exigem o esquema `AgentToken`; <br> - um token de agente numa rota de usuário recebe 401, e um token de usuário numa rota de agente também. <br>- **Organização:** vem **só da credencial**. O cabeçalho `X-Gestalyn-Organization`, se enviado, precisa ser igual; se for diferente, a resposta é 404. O middleware de organização ativa ganha o caminho do agente, sem `MembershipDirectory`. | (a) Cabeçalho próprio (`X-Gestalyn-Agent-Key`): foge do padrão `Bearer` que CLIs e clientes MCP já tratam. (b) Deixar agentes usarem as rotas de usuário: cada rota precisaria saber lidar com agentes, e uma omissão daria a eles poderes de pessoa. |
| **D4** Escopos e acesso a projetos | **`AgentProjectAccess`** `(organização, agente, projeto, papel)`: <br>- o papel é **Colaborador ou Observador**; **Gerente não é permitido** para agentes; <br>- quem concede precisa de `agents.manage` na organização **e** de `project.members.manage` naquele projeto, o que impede conceder acesso a um projeto que a pessoa não gerencia; <br>- projeto arquivado: o agente lê, mas não escreve, pela regra que já vale para pessoas. <br>**Escopos da credencial** (pelo menos um, escolhidos na criação): <br>- `context:read`: contexto e projetos; <br>- `tasks:read`: listar e ler tarefas; <br>- `tasks:write`: criar, editar e mover tarefas, sem aprovação; <br>- `activities:write`: registrar atividades; <br>- `reports:write`: enviar relatórios. <br>**Regra única:** `AgentAccessGrant.Compute(acesso, escopos)` devolve um `ProjectAccessGrant` com `ActorKind.Agent`. As permissões são as do papel do projeto (`ProjectPermissions.For`) **intersectadas** com as que os escopos concedem. **`tasks.approve` nunca entra.** A regra fica em `Application/Projects`, ao lado de `ProjectAccessGrant`, e não é duplicada. <br>Projeto sem acesso responde 404; escopo ou permissão insuficiente, 403. | (a) Escopos no agente, e não na credencial: impediria uma credencial só de leitura para uma ferramenta de menor confiança. (b) Permissões avulsas por projeto, sem papel: duplicaria o catálogo de papéis da Fase 05. |
| **D5** Autoria de agentes | O serviço passa a receber o **ator** (ID e `ActorKind`), em vez de só o `UserId`. <br>- **`tasks`:** `created_by_user_id` passa a aceitar nulo, e entra a coluna **`created_by_agent_id`**, com FK composta para `agents`. Uma restrição `CHECK` exige exatamente um dos dois. <br>- **`task_status_history`, `activity_events` e `activity_logs`:** guardam o ID do agente com `actor_kind = 'Agent'`. Essas tabelas já não têm FK de autor. <br>- **Contexto da atividade:** `HttpActivityContext` passa a usar `ActorKind.Agent` quando a credencial é de agente. A origem continua informativa (`X-Gestalyn-Client`). <br>- **Leitura:** as respostas de tarefa e atividade ganham `createdBy { id, kind, name }`, para a Web mostrar "Agente: nome". Os campos atuais continuam, para não quebrar contratos. | (a) Gravar o usuário que provisionou o agente como autor: perde a distinção entre pessoa e agente, que a seção 4.6 exige. (b) Conta técnica em `users`: veja a D1 (a). |
| **D6** Endpoints de agentes | Seção 5.8 com **ajustes documentados** (a própria seção os permite). Todas as rotas ficam sob `/api/v1/agents`: <br>- `GET /me`: agente, organização, credencial (prefixo, escopos, expiração); <br>- `GET /projects`: projetos concedidos, com o papel; <br>- `GET /projects/{id}/context`: dados do projeto, estados e transições do workflow, fases e marcos, progresso, decisões aceitas, títulos dos documentos e as 20 atividades mais recentes, com tamanho limitado; <br>- `GET /projects/{id}/tasks`: os filtros e a paginação da lista de tarefas; <br>- **novo:** `GET /tasks/{id}`, os detalhes com as transições disponíveis para o agente; <br>- `POST /tasks`: com `projectId` no corpo; <br>- `PATCH /tasks/{id}`: com `expectedVersion` obrigatório (D8); <br>- **novo:** `POST /tasks/{id}/transitions`: o mesmo serviço e o mesmo `TransitionRules` da rota de pessoas. As transições que exigem aprovação são recusadas com 403, e a mudança fica no `TaskStatusHistory`; <br>- `POST /activities` e `POST /reports`. <br>Todas reutilizam os serviços existentes. Nenhuma regra de negócio é copiada para os controllers de agentes. O AGENTS.md passa a citar a rota de transição de agentes como o segundo caminho permitido, que usa a mesma regra. | (a) Só os oito endpoints literais: sem ler a tarefa nem mover o estado, o agente não consegue "sugerir a conclusão" (seção 4.5) nem conhecer as transições disponíveis. (b) Transição dentro do `PATCH`: misturaria a edição com a mudança de estado, que o ADR-013 isolou. |
| **D7** Relatório de trabalho | **`WorkReport`**, imutável, com gatilho no banco como os eventos da Fase 08. <br>**Dados declarados pelo agente** (com limites de tamanho): <br>- `projectId` e `taskId` (opcional, mas, se vier, a tarefa precisa ser do projeto); <br>- `summary` e `changes` (Markdown); <br>- `files` (até 200 caminhos); <br>- `tests` (comando, aprovados, falhas, ignorados, declarados); <br>- `commit` (SHA e repositório opcional); <br>- `suggestedStatus` (o ID de um estado do workflow do projeto). <br>**Resposta:** `{ reportId, accepted, activityCreated, taskStatus, approvalRequired, evidence }`. <br>- `approvalRequired`: diz se a transição do estado atual até o sugerido exige aprovação. <br>- **O relatório não muda o estado da tarefa**; para isso, o agente usa a rota de transição. <br>**Os cinco níveis da seção 6.4**, guardados e devolvidos separados: <br>1. **Declarado:** o conteúdo do agente, marcado como declaração. <br>2. **Aceito:** a validação da API (projeto, tarefa, estado, tamanhos). <br>3. **Testes executados:** `notVerified`. O Gestalyn não executa testes, e a contagem do agente é só declaração. <br>4. **Confirmado pelo CI:** `notVerified` até a integração com o GitHub (Fase 12). <br>5. **Aprovado por uma pessoa:** derivado, na leitura, do histórico da tarefa (uma transição com aprovação feita por `ActorKind.User` depois do relatório). Nunca é gravado pelo agente. <br>**Leitura por pessoas:** `GET /projects/{id}/reports` e `GET /tasks/{id}/reports`, com `project.read`. | (a) Relatório como `ActivityLog` com um tipo novo: o log não tem estrutura para arquivos, testes e commit, e misturaria a declaração com o registro manual. (b) Aceitar `suggestedStatus` e mover a tarefa: um relatório viraria um atalho para mudar o estado sem a rota de transição. |
| **D8** Idempotência e concorrência | **`Idempotency-Key`** (de 1 a 100 caracteres visíveis) **obrigatório** nos `POST` de agentes; sem ele, a resposta é 400. <br>**`IdempotencyRecord`** `(organização, agente, chave)` único, com: <br>- método e rota; <br>- o SHA-256 do corpo; <br>- o estado `Pending` ou `Completed`; <br>- o código e o corpo da resposta (até 64 KB); <br>- a criação e a expiração (24 horas). <br>**Fluxo:** <br>1. A chave é reservada (`Pending`) antes da operação, numa gravação própria; a restrição única resolve a corrida. <br>2. Depois da operação, a resposta é guardada. <br>3. Mesma chave com o mesmo corpo: a resposta guardada é devolvida, com `Idempotency-Replayed: true`. <br>4. Mesma chave com outro corpo: 422. <br>5. Mesma chave ainda em processamento: 409. <br>6. Erro 5xx libera a chave, para nova tentativa; erros 4xx ficam guardados. <br>Registros expirados são ignorados e sobrescritos na próxima reserva (limpeza em lote na Fase 14). <br>**Concorrência nas tarefas:** uma coluna nova, `version`, incrementada a cada alteração e usada como token de concorrência. <br>- O `PATCH` de agentes **exige** `expectedVersion` e responde 409 quando ela diverge. <br>- O `PATCH` de pessoas aceita o campo como **opcional**, para não quebrar a Web. A Web passa a enviá-lo. | (a) Idempotência opcional: um agente que repete a chamada depois de um tempo esgotado criaria tarefas duplicadas, o que a seção 6.5 proíbe. (b) Idempotência em memória: perderia as chaves ao reiniciar e não funcionaria com mais de uma instância. (c) ETag e `If-Match`: equivalente; o `expectedVersion` no corpo segue o padrão dos documentos (Fase 08). |
| **D9** Auditoria de operações e rate limiting | **`AgentOperation`**, imutável, com gatilho no banco. Uma linha por requisição autenticada de agente, **inclusive leituras e negadas**: <br>- agente e credencial; <br>- método e modelo da rota (não a URL com valores); <br>- projeto e recurso, quando houver; <br>- código da resposta, duração, ID da requisição e se foi replay de idempotência. <br>**Nenhum corpo, token, cabeçalho ou e-mail** é gravado. Tokens inválidos não têm agente: só o log estruturado, sem o token nem o prefixo. <br>Leitura por pessoas: `GET /organizations/{id}/agents/{agentId}/operations`, com `agents.manage`. As escritas continuam gerando `ActivityEvent`, agora com `ActorKind.Agent`, e entram no `ActivityCoverageTests`. <br>**Rate limiting:** janela fixa **por credencial**, em `RateLimiting:Agents` (padrão: 120 requisições por minuto). O excesso responde 429 em Problem Details, com `Retry-After`. | (a) Registrar só as escritas: perde o padrão de leitura de um agente, que é o que mais interessa quando uma credencial vaza. (b) Gravar os corpos: copiaria textos longos e possíveis segredos, o que o ADR-014 proíbe. |
| **D10** Interface web, tempo real e testes | **Interface web (F09-018):** <br>- **Configurações da organização → Agentes** (com `agents.manage`): lista, criação, desativação e reativação; <br>- **detalhes do agente:** <br> - credenciais (prefixo, escopos, expiração, último uso), criação e revogação; <br> - projetos concedidos, com concessão e remoção; <br> - operações recentes. <br>- **Detalhes da tarefa e atividades do projeto:** autoria "Agente: nome" e os relatórios de trabalho, com os níveis da D7 rotulados ("Declarado pelo agente", "Não verificado pelo CI"). <br>- **O token novo aparece uma única vez**, na resposta da criação: <br> - com `Cache-Control: no-store`; <br> - sem passar por URL, aviso ou cookie; <br> - com um botão de copiar, num módulo JS sem script inline. <br>A Web nunca recebe o hash nem o token depois disso. As telas ganham testes bUnit e entram em `docs/web/TELAS.md`. <br>**Tempo real:** reavaliado. Os relatórios e as operações de agentes são lidos ao abrir a tela; sem necessidade comprovada de SignalR, **continua fora**. <br>**Testes de segurança:** veja a estratégia de testes. Inclui o teste "agentes não aprovam o próprio trabalho" com um agente real, pendente desde a Fase 07. | (a) Mostrar o token numa página separada, com redirecionamento: o token precisaria ficar guardado entre as requisições. (b) Telas de agentes sem testes de componente: contraria o AGENTS.md. |

### Decisões de baixo risco (serão registradas no ADR-016)

- **Erros:** Problem Details em pt-BR. O 401 de agente é sempre o mesmo, sem distinguir token inexistente, expirado ou revogado.
- **Limites:**
  - nome do agente: até 100 caracteres;
  - descrição: até 500;
  - nome da credencial: até 100;
  - `summary` do relatório: até 2.000; `changes`: até 20.000;
  - caminho de arquivo: até 500 caracteres.
- **Rotas de gestão** (pessoas, com `[OrganizationPermission("agents.manage")]`):
  - `GET`, `POST /organizations/{id}/agents`;
  - `GET`, `PATCH /organizations/{id}/agents/{agentId}`;
  - `POST .../{agentId}/disable` e `.../enable`;
  - `GET`, `POST .../{agentId}/credentials` e `POST .../credentials/{credentialId}/revoke`;
  - `PUT`, `DELETE .../{agentId}/projects/{projectId}`, com `project.members.manage` no projeto;
  - `GET .../{agentId}/operations`.

  Todas as escritas registram eventos com `ActivityRecorder` e entram no `ActivityCoverageTests`.
- **Logs:** só os IDs do agente e da credencial, por `[LoggerMessage]`. Nunca o token, o prefixo, o hash nem o corpo.
- **OpenAPI:**
  - o esquema `AgentToken` documentado;
  - as rotas de agentes agrupadas na tag "Agentes";
  - o token nunca aparece nos exemplos.

## Contratos previstos

| Rota | Quem | Resumo |
|---|---|---|
| `GET /api/v1/agents/me` | Agente (qualquer escopo) | `{ agentId, name, organizationId, credential { id, prefix, scopes, expiresAt } }` |
| `GET /api/v1/agents/projects` | `context:read` | Projetos concedidos, com o papel |
| `GET /api/v1/agents/projects/{id}/context` | `context:read` + `project.read` | Contexto limitado (D6) |
| `GET /api/v1/agents/projects/{id}/tasks` | `tasks:read` | Lista paginada |
| `GET /api/v1/agents/tasks/{id}` | `tasks:read` | Detalhes, versão e transições disponíveis para o agente |
| `POST /api/v1/agents/tasks` | `tasks:write` + `tasks.write` + `Idempotency-Key` | 201 com a tarefa |
| `PATCH /api/v1/agents/tasks/{id}` | `tasks:write` + `tasks.write`, com `expectedVersion` | 200, ou 409 se a versão divergir |
| `POST /api/v1/agents/tasks/{id}/transitions` | `tasks:write` + `tasks.write` + `Idempotency-Key` | 200, 403 (aprovação), 409 (sem transição ou dependências abertas) |
| `POST /api/v1/agents/activities` | `activities:write` + `tasks.write` + `Idempotency-Key` | 201 com o registro |
| `POST /api/v1/agents/reports` | `reports:write` + `project.read` + `Idempotency-Key` | 201 com `{ reportId, accepted, activityCreated, taskStatus, approvalRequired, evidence }` |
| `GET /api/v1/projects/{id}/reports`, `GET /api/v1/tasks/{id}/reports` | Pessoa com `project.read` | Relatórios com os cinco níveis |
| Rotas de gestão (decisões de baixo risco) | Pessoa com `agents.manage` | Agentes, credenciais, acesso a projetos e operações |

## Dependências

| Item | Estado |
|---|---|
| Fases 03 a 09 | Completed. A Fase 09 integrada em `develop` (PR #17); o registro como Completed está no PR #18, ainda aberto |
| Pacotes novos | **Nenhum.** Autenticação, rate limiting e hash usam o ASP.NET Core e o .NET |
| Docker (PostgreSQL e Mailpit) | Instalado; precisa estar em execução para os testes de integração e a execução real |

## Tarefas

| ID | Tarefa | Subtarefas |
|---|---|---|
| F10-001 | Implementar entidade Agent | Entidade, configuração, migração, permissão `agents.manage` com migração de dados (D1) |
| F10-002 | Implementar provisionamento | Rotas de gestão, eventos de auditoria, testes de permissão e de isolamento (D1) |
| F10-003 | Implementar credenciais | Criação com o token uma vez só, listagem pelo prefixo, até 3 ativas (D2) |
| F10-004 | Implementar hash e armazenamento seguro de tokens | SHA-256 com índice único; nada do token em logs ou respostas posteriores (D2) |
| F10-005 | Implementar expiração | Padrão e máximo configuráveis, checagem a cada requisição (D2) |
| F10-006 | Implementar revogação | Credencial e desativação do agente, com efeito imediato (D2) |
| F10-007 | Implementar escopos de acesso | Escopos na credencial e `AgentAccessGrant.Compute` (D4) |
| F10-008 | Implementar AgentProjectAccess | Concessão e remoção com as duas permissões; papel Colaborador ou Observador (D4) |
| F10-009 | Implementar autenticação de agentes | Esquema `AgentToken`, seletor por prefixo, separação das rotas, organização da credencial (D3) |
| F10-010 | Implementar endpoints de contexto | `me`, `projects`, `projects/{id}/context` (D6) |
| F10-011 | Implementar listagem de tarefas | `projects/{id}/tasks` e `tasks/{id}` (D6) |
| F10-012 | Implementar criação e atualização de tarefas | `POST`, `PATCH` com `expectedVersion`, transições, autoria de agente (D5, D6 e D8) |
| F10-013 | Implementar registro de atividades | `POST /agents/activities` com `ActorKind.Agent` (D5) |
| F10-014 | Implementar relatórios de trabalho | `WorkReport`, os cinco níveis, leitura por pessoas (D7) |
| F10-015 | Implementar idempotência | `IdempotencyRecord`, reserva, replay, conflito e expiração (D8) |
| F10-016 | Implementar auditoria de operações | `AgentOperation`, imutabilidade, leitura por pessoas, rate limiting (D9) |
| F10-017 | Criar testes de segurança | Veja a estratégia de testes (D10) |
| F09-018 | Implementar interface de agentes (transferida) | Telas de agentes, credenciais, acesso, operações e relatórios (D10) |

## Arquivos previstos

```
src/ProjectManager.Domain/Agents/                  Agent, AgentCredential, AgentProjectAccess, AgentScopes,
                                                   AgentOperation, WorkReport
src/ProjectManager.Application/Agents/             Contratos, IAgentService, IWorkReportService, IIdempotencyStore
src/ProjectManager.Application/Projects/AgentAccessGrant.cs   Regra única de acesso de agentes (D4)
src/ProjectManager.Infrastructure/Agents/          Serviços, autenticação de credenciais, idempotência
src/ProjectManager.Infrastructure/Persistence/Configurations/AgentConfigurations.cs
src/ProjectManager.Infrastructure/Persistence/Migrations/       Agentes, credenciais, acesso, operações,
                                                   relatórios, idempotência, autoria e versão das tarefas
src/ProjectManager.API/Authentication/AgentTokenAuthentication.cs
src/ProjectManager.API/Agents/                     Controllers de agentes e de gestão, filtro de idempotência,
                                                   registro de operações, rate limiting por credencial
src/ProjectManager.Web/Components/Pages/Agents/    Telas de agentes (D10)
tests/ProjectManager.UnitTests/Agents/             Escopos, AgentAccessGrant, credenciais, relatórios
tests/ProjectManager.IntegrationTests/Agents/      Autenticação, isolamento, idempotência, segurança
tests/ProjectManager.WebTests/Agents/              Telas de agentes
docs/decisions/ADR-016-AGENTES.md
docs/security/AGENTES.md                           Modelo de ameaças e uso das credenciais
```

## Critérios de aceitação

| Critério (especificação mestre) | Como será verificado |
|---|---|
| Agentes autenticam independentemente de usuários | Teste de integração: credencial de agente sem nenhuma sessão de usuário; o token de agente recusado nas rotas de usuário e o token de usuário recusado nas rotas de agente |
| Credenciais podem ser revogadas | Testes: credencial revogada, agente desativado e credencial expirada recebem 401 na requisição seguinte; outra credencial ativa continua funcionando |
| Escopos são respeitados | Teste por escopo: cada rota recusa (403) a credencial sem o escopo; `tasks.approve` nunca é concedido |
| Acesso limitado aos projetos autorizados | Testes: projeto da mesma organização sem concessão (404), projeto de outra organização (404), cabeçalho de organização divergente (404), Observador sem escrita (403) |
| Operações registradas | Testes: cada requisição de agente gera uma `AgentOperation`, inclusive as negadas; cada escrita gera um `ActivityEvent` com `ActorKind.Agent` (`ActivityCoverageTests`); `UPDATE` e `DELETE` recusados pelo banco |
| Idempotência funcional | Testes: repetir um `POST` não duplica e devolve a mesma resposta; outro corpo com a mesma chave responde 422; chamadas concorrentes com a mesma chave criam um registro só |
| Tentativas de acesso indevido são bloqueadas | Testes de segurança da estratégia de testes; mutações; execução real com a credencial de um agente |
| Agentes não aprovam o próprio trabalho (Fase 07) | Teste de integração com um agente real: a transição com aprovação é recusada (403) e o estado não muda |

## Estratégia de testes

- **Unitários:**
  - `AgentAccessGrant` (papel × escopos, sem `tasks.approve`);
  - expiração e revogação;
  - formato e prefixo do token;
  - os níveis do relatório;
  - a comparação do hash da idempotência.
- **Integração (PostgreSQL real):**
  - **autenticação:** separação das rotas; credencial revogada, expirada ou de agente desativado; token adulterado;
  - **isolamento:** cross-tenant por rota e por cabeçalho, e projeto sem concessão;
  - **escopos e papéis:** escopos por rota; Observador versus Colaborador; projeto arquivado;
  - **quem pode gerir:** conceder acesso sem `project.members.manage`; a migração de `agents.manage`;
  - **regras de trabalho:** aprovação do próprio trabalho; idempotência (replay, conflito, concorrência); `expectedVersion` (409);
  - **registros:** autoria `Agent` em tarefas, histórico e eventos; operações e imutabilidade;
  - **limites:** rate limiting (429).
- **Componentes (bUnit):**
  - telas de agentes e credenciais (token mostrado uma vez, permissões);
  - relatórios com os níveis rotulados.
- **Mutação:** cada uma destas deve fazer os testes falharem:
  - remover a checagem de revogação;
  - incluir `tasks.approve` nos escopos;
  - aceitar o cabeçalho de outra organização;
  - pular o hash do corpo na idempotência.
- **Execução real:**
  - API e Web sobre o banco local: uma pessoa provisiona um agente, cria a credencial e concede um projeto;
  - com o token, chamadas reais (`curl`) a contexto, criação e transição de tarefa, relatório e replay;
  - a aprovação recusada; a revogação pela Web e o 401 em seguida;
  - nenhum token nos logs (busca no log da execução) nem no banco (só o hash).

## Riscos

| Risco | Mitigação |
|---|---|
| Vazamento de credencial | Token mostrado uma vez, hash no banco, expiração obrigatória, revogação imediata, escopos mínimos, rate limiting e registro de todas as operações |
| Agente ganhar poderes de pessoa | Esquemas e rotas separados; Gerente proibido; `tasks.approve` nunca concedido; testes de mutação |
| Regra de acesso duplicada | `AgentAccessGrant.Compute` é a única regra de agentes e reaproveita `ProjectPermissions.For`; os serviços continuam os mesmos |
| Migração da autoria das tarefas (`created_by_user_id` nulo) | `CHECK` de exatamente um autor; os dados existentes ficam inalterados; teste da migração |
| `version` nas tarefas quebrar a Web | Campo opcional para pessoas; a Web passa a enviá-lo; testes existentes mantidos |
| Volume de `AgentOperation` | Uma linha pequena por requisição, com índice por agente e data; retenção na Fase 14 |
| Corrida na idempotência | Reserva com restrição única antes da operação; teste com chamadas concorrentes |
| Volume da fase (18 tarefas) | Entrega por partes, um commit por área: domínio e migração; autenticação e escopos; endpoints; relatórios; idempotência e operações; Web; testes de segurança |

## Definição de conclusão

- Todos os critérios verificados com evidência, local e no CI.
- Mutações de segurança detectadas.
- ADR-016 registrado, inclusive os ajustes nos contratos da seção 5.8.
- `docs/security/AGENTES.md` escrito.
- Documentação atualizada: README, AGENTS.md (rota de transição de agentes, autoria e escopos), ARCHITECTURE, `docs/web/TELAS.md`, `PROJECT_STATUS.md` e relatório da Fase 10.
- Alterações integradas por PR, com o check "Build e testes" aprovado.
