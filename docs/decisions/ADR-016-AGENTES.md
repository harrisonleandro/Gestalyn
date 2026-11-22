# ADR-016: Agentes, credenciais, idempotência e auditoria de operações

- **Status:** Aceito e implementado. A documentação corrigida da Fase 10 foi aprovada pelo responsável em 2026-11-18; as etapas 1 a 12 foram implementadas de 2026-11-18 a 2026-11-22 na branch `feature/fase-10-agentes`. A integração em `develop`, por PR com o CI aprovado, ainda depende de autorização.
- **Data:** 2026-11-15
- **Fase:** 10 (API e autenticação de agentes)
- **Aprovação:**
  - em 2026-11-15, o responsável aprovou as 24 decisões da revisão técnica e as 4 decisões complementares (C4, A6 rate limiting, A6 retenção e C2 Cancelada);
  - em 2026-11-18, aprovou a documentação corrigida, inclusive as escolhas técnicas que a detalham.
- **Detalhamento:** [especificação da Fase 10](../specifications/FASE-10-AGENTES.md)

## Contexto

A especificação mestre pede que agentes de IA operem na plataforma (seções 4.7, 5.8, 6.1, 6.4, 6.5 e 7):
- com credenciais próprias, revogáveis e com expiração;
- com permissões limitadas por organização e projeto;
- registrando todas as operações;
- sem operações duplicadas;
- sem aprovar o próprio trabalho.

Até a Fase 09:
- só existia a autenticação de usuários (ADR-006);
- a organização ativa dependia da associação de um usuário (ADR-008);
- `IgnoreQueryFilters` era permitido só no `MembershipDirectory`;
- a autoria das tarefas era sempre de um usuário.

A revisão técnica de 2026-11-15 confirmou no código seis problemas críticos na primeira versão da especificação. Este ADR registra as decisões que os resolvem.

## Decisão

### Identidade do agente

- `Agent` é uma entidade da organização (`IOrganizationOwned`). **Não é membro**, não tem papel da organização e não acessa a Web.
- **Criação e administração:** só pessoas com a permissão nova `agents.manage`.
  - Nas organizações existentes, uma migração de dados a concede a Proprietário e Administrador.
  - Nas novas, ela vem com `OrganizationPermissions.All`.
  - A autorização é pela permissão, como no ADR-009. Papéis personalizados, quando tiverem endpoints, poderão recebê-la explicitamente.
- **Sem autorregistro, sem exclusão:** agentes são desativados, para preservar a autoria.

### Credenciais

- **Formato:** `gsk_` + 32 bytes de `RandomNumberGenerator` em base64url (256 bits).
- **Armazenamento:** só o SHA-256 do token completo (índice único) e o prefixo de 8 caracteres.
- **Exibição:** o token aparece uma única vez, na criação.
- **Expiração obrigatória:** padrão de 90 dias, máxima de 365.
- **Até 3 credenciais ativas por agente:** a contagem e a criação são protegidas por uma trava otimista na linha do agente (`xmin`), que só faz disputar criações para o mesmo agente.
- **Revogação:** vale na requisição seguinte, porque toda requisição confere a credencial.
- **Escopos na credencial:** `context:read`, `tasks:read`, `tasks:write`, `activities:write` e `reports:write`.

### Autenticação e separação das políticas

- O JWT continua sendo o esquema padrão, e `AgentToken` é um esquema adicional. **Nenhum seletor de esquema é o padrão.**
- **O `new AuthorizeFilter()` sem política é substituído por duas políticas explícitas:**
  - `User`: esquema JWT e claim `sid`;
  - `Agent`: esquema `AgentToken` e claim `actor_kind=agent`.
  - Uma convenção aplica `Agent` aos controllers marcados com `[AgentApi]` e `User` aos demais. A marca é conferida contra a rota, e uma incoerência impede a API de iniciar.
  - Um teste percorre todos os endpoints e exige exatamente uma das duas políticas, ou `[AllowAnonymous]` explícito.
- **A organização de um agente vem só da credencial.** Rota ou cabeçalho divergentes respondem 404.
- **O 401 é igual** para token inexistente, expirado, revogado ou de agente desativado.

### Exceções às regras de isolamento do AGENTS.md

O `AGENTS.md` permitia `IgnoreQueryFilters` só no `MembershipDirectory` e exigia justificativa em ADR para SQL bruto sobre dados de organizações. Este ADR autoriza **apenas** estas exceções:

| Exceção | Onde | Restrição | Justificativa |
|---|---|---|---|
| `IgnoreQueryFilters` | `AgentCredentialDirectory` | Só a busca pela igualdade do hash do token; devolve a credencial, o agente e a organização | A organização do agente só é conhecida depois de achar a credencial |
| SQL bruto (upsert com `RETURNING`) | Rate limiting de agentes | Só a tabela `agent_rate_limit_counters`, pela credencial autenticada | O EF Core não expressa um incremento atômico com retorno numa só instrução |
| SQL bruto (`pg_try_advisory_xact_lock`) | Idempotência | Só a trava pela chave da credencial | Responder 409 imediato a uma chave em processamento, sem esperar a transação concorrente |
| SQL bruto e consulta sem filtro de organização | `AgentRetentionService` | Só `DELETE` em lotes dos registros vencidos de `agent_operations`, `idempotency_records` e `agent_rate_limit_counters`, e a trava consultiva da limpeza | A retenção atravessa organizações. O gatilho do banco limita o que pode ser apagado |

### Acesso a projetos e regra única

- **`AgentProjectAccess`**, com papel Colaborador ou Observador. **Gerente é proibido.**
- **Quem concede:** precisa de `agents.manage` e de `project.members.manage` no projeto.
- **Permissões possíveis para agentes:** `project.read`, `tasks.write` e `documents.write`, intersectadas com as do papel. **`tasks.approve` nunca entra.**
- **Escopos:**
  - `activities:write` exige `documents.write`, a mesma permissão do registro manual por pessoas;
  - `reports:write` exige `tasks.write`.
- **`AgentAccessGrant.Compute`** (`Application/Projects`) é a única regra de acesso de agentes:
  - **recebe** o ator, o estado do agente, os escopos, o papel e a exigência da operação;
  - **devolve** a decisão com o motivo.
- **`ProjectAccessGrant` passa a carregar o ator.** Os serviços que gravam autoria com FK para `users` recusam um ator agente.

### Transições por agentes

Ficam em `TransitionRules`, a regra única do ADR-013. Para um ator agente:
- **transição com aprovação:** 403 `AgentCannotApprove`;
- **destino da categoria Concluída:** 403 `AgentCannotEnterDone`, mesmo sem aprovação, inclusive em workflows personalizados;
- **Cancelada:** permitida quando a transição existe, não exige aprovação e o agente tem `tasks:write` e `tasks.write`.

As transições disponíveis passam a ser calculadas por `TransitionRules`, para pessoas e agentes. A rota de pessoas não muda, e a rota `POST /agents/tasks/{id}/transitions` usa o mesmo serviço.

### Autoria e concorrência das tarefas

- **Autoria em `tasks`:**
  - `created_by_user_id` passa a anulável, e entra `created_by_agent_id`, com FK composta para `agents`;
  - `CHECK (num_nonnulls(created_by_user_id, created_by_agent_id) = 1)`;
  - um gatilho recusa a mudança da autoria;
  - as tarefas existentes ficam inalteradas.
- **Ator nos registros:** histórico, eventos e atividades guardam o agente com `ActorKind.Agent`.
- **`version`:**
  - é o token de concorrência das tarefas, conferido no `UPDATE`;
  - os agentes são obrigados a enviá-la; para pessoas, é opcional;
  - versão divergente responde 409.

### Relatório de trabalho

- **`work_reports`** é imutável e permanente: um gatilho recusa `UPDATE`, `DELETE` e `TRUNCATE`.
- **Os cinco níveis ficam separados:**
  1. declarado;
  2. aceito, que significa **recebido e validado pela API**;
  3. testes;
  4. CI;
  5. aprovação humana.
- **Verificações posteriores** (testes e CI) ficam numa tabela só de inclusão, criada na Fase 12. Até lá, esses níveis são "não verificado".
- **A aprovação humana** é calculada pelo histórico da tarefa.
- **O relatório não muda o estado da tarefa.**

### Idempotência

- **`Idempotency-Key`** é obrigatório em todos os `POST` de agentes.
- **A chave** é única por credencial e vinculada ao método, ao modelo da rota e ao SHA-256 do caminho concreto, da query e do corpo (o caminho entra no hash desde a etapa 8: a mesma chave em outra tarefa é outra requisição).
- **Tudo numa transação:** a reserva, a operação e a resposta guardada.
- **Respostas:**
  - chave em processamento: 409, pela trava consultiva;
  - corpo, método ou rota diferentes: 422.
- **Antes de devolver uma resposta guardada**, a autorização é reavaliada. 401, 403, 404, 429 e 5xx não são guardados.
- **Prazos:** expiração lógica em 24 horas e exclusão depois de 7 dias.

### Auditoria de operações

- **Uma linha de `agent_operations` por requisição com credencial identificada**, inclusive as negadas, as de credenciais revogadas ou expiradas e a primeira rejeição por limite em cada janela.
- **Campos:**
  - agente e credencial;
  - método e modelo da rota;
  - projeto e tarefa;
  - status, data e duração;
  - resultado da autorização e código da recusa;
  - correlation ID;
  - replay de idempotência;
  - origem informativa.
- **Nunca gravados:** tokens, prefixos, hashes, cabeçalhos ou corpos.
- **Gravação:** fora da transação da operação. Uma falha nela não derruba a requisição: gera log de erro e o health check `agents-audit` fica `Degraded`.
- **Tokens inexistentes:** só log estruturado e limite por IP.

### Rate limiting

- **Contador por credencial e janela de 60 segundos** no PostgreSQL, com uma única instrução atômica por requisição:

  ```sql
  INSERT … ON CONFLICT (credential_id, window_start)
  DO UPDATE SET request_count = request_count + 1
  RETURNING …
  ```

  - **Índice:** a chave primária `(credential_id, window_start)`.
  - **Relógio:** o do banco.
  - **Instâncias:** funciona com várias.
- **Limite inicial:** 120 por minuto. Acima dele, 429 com `Retry-After`.
- **Janela fixa:** admite até o dobro do limite na virada, o que é aceito e documentado.
- **Autenticação explícita na entrada das rotas de agentes** (etapa 10): com as políticas explícitas, o `UseAuthentication` só executa o JWT, o esquema padrão. O `AgentRequestMiddleware` autentica pelo `AgentToken` logo depois dele, aplica o limite por IP e o da credencial e envolve a requisição na auditoria; a política `Agent` da rota reaproveita o resultado guardado pelo ASP.NET Core, sem nova consulta. A ordem do pipeline da D3 fica preservada.
- **Complemento:** um limite em memória de falhas de autenticação por IP, só para proteger a consulta do hash. Não é a cota dos agentes.

### Retenção

| Registro | Retenção |
|---|---|
| `agent_operations` | 180 dias |
| `idempotency_records` | 24 horas (lógica); exclusão depois de 7 dias |
| `work_reports` | Permanente |
| `agent_rate_limit_counters` | 1 dia |

- **Serviço:** `AgentRetentionService`, um `BackgroundService` na API, sem infraestrutura nova.
- **Funcionamento:**
  - uma instância por vez, pela trava consultiva;
  - lotes de 1.000 linhas com `statement_timeout`;
  - logs com as contagens e o health check `agents-retention`;
  - falhas registradas sem afetar as requisições.
- **Proteção no banco:** o gatilho de `agent_operations` só aceita o `DELETE` com mais de 180 dias. Mudar o prazo exige uma migração.

### Exibição do token na Web (exceção à seção 7 da especificação mestre e ao ADR-006)

A seção 7 da especificação mestre e o ADR-006 dizem que credenciais completas nunca são enviadas à interface Web. **Por decisão do responsável (2026-11-15), há uma única exceção:** o token de uma credencial de agente recém-criada aparece na Web, uma vez, na resposta da criação. Condições obrigatórias:

1. o token completo aparece uma única vez, na resposta do formulário de criação, sem redirecionamento;
2. ele não é guardado no banco (só o hash), na Web, no navegador (armazenamento local, cookie, URL), na sessão nem em aviso;
3. a API e a Web respondem com `Cache-Control: no-store`;
4. ele não aparece em logs, telemetria, monitoramento nem respostas posteriores;
5. ao sair da tela ou perder o valor, não há recuperação: a pessoa cria uma credencial nova.

Os tokens de sessão dos usuários continuam sem chegar ao navegador, sem exceção. **A exceção não se estende a nenhuma outra credencial.**

### Rotas

- **Agentes** (política `Agent`), sob `/api/v1/agents`:
  - `GET /me`, `GET /projects`, `GET /projects/{id}/context` e `GET /projects/{id}/tasks`;
  - `GET /tasks/{id}` (**ajuste à seção 5.8**);
  - `POST /tasks` e `PATCH /tasks/{id}`;
  - `POST /tasks/{id}/transitions` (**ajuste à seção 5.8**);
  - `POST /activities` e `POST /reports`.
- **Gestão por pessoas** (política `User`, `[OrganizationPermission("agents.manage")]`), sob `/api/v1/organizations/{organizationId}/agents`:
  - `GET` e `POST`;
  - `GET` e `PATCH /{agentId}`;
  - `POST /{agentId}/disable` e `/enable`;
  - `GET` e `POST /{agentId}/credentials` e `POST /{agentId}/credentials/{credentialId}/revoke`;
  - `PUT` e `DELETE /{agentId}/projects/{projectId}`, com `project.members.manage` no projeto;
  - `GET /{agentId}/operations`.
- **Relatórios por pessoas** (`project.read`):
  - `GET /api/v1/projects/{id}/reports`;
  - `GET /api/v1/tasks/{id}/reports`;
  - `GET /api/v1/reports/{id}`.
- **Mudanças em contratos existentes:**
  - `TaskDetails.CreatedByUserId` passa a anulável, e entra `createdBy { id, kind, name }`;
  - as atividades e o histórico ganham o nome do ator agente;
  - `PATCH /tasks/{id}` aceita `expectedVersion` opcional.
- **Toda escrita** de gestão, de agente e de relatório registra um `ActivityEvent` e entra no `ActivityCoverageTests`.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Agente como conta técnica em `users` | Mistura identidades no Identity e abre o fluxo de senha a agentes |
| Agente como membro da organização | As permissões da organização (`projects.read_all`) vazariam para agentes |
| JWT assinado ou OAuth client credentials | Revogação imediata exigiria lista negra ou servidor de autorização |
| Hash lento do token | Custo por requisição sem ganho para um segredo de 256 bits aleatórios |
| Seletor de esquema como padrão | A política global aceitaria agentes nas rotas de usuário |
| Bloquear só as transições marcadas com aprovação | Um workflow personalizado deixaria o agente concluir sem pessoa |
| Reservar a chave de idempotência numa gravação separada | Uma queda entre a operação e a resposta deixaria a chave presa ou duplicaria a operação |
| Rate limiting só em memória | Não funciona com várias instâncias |
| Redis para rate limiting | Infraestrutura nova sem aprovação; o PostgreSQL já atende |
| Limpeza num projeto `Worker` | Um serviço em segundo plano na API basta; o `Worker` surge quando houver necessidade concreta |
| Criar credenciais só pela API e pela CLI | Impediria a gestão de agentes pela Web; recusada pelo responsável |

## Revisão de 2026-11-18 (revisão técnica independente das etapas 1 a 4)

A revisão manteve a arquitetura e pediu correções pontuais:

- **C1, política por marca:** a política `Agent` deixou de depender do namespace do controller e passou a depender da marca explícita `[AgentApi]`, conferida contra a rota na inicialização.
- **C2 e C3, testes:** isolamento multi-tenant testado no pipeline real de agentes, e recusas de autenticação comprovadamente indistinguíveis. Nenhuma mudança de comportamento foi necessária.
- **C4, escopos:** `CHECK ck_agent_credentials_scopes_known` restringe o `text[]` aos escopos do catálogo também fora do domínio. O armazenamento em `text[]` foi mantido.
- **Limite de credenciais:** durante as correções, os testes revelaram que a transação `Serializable` da criação de credenciais fazia agentes diferentes, inclusive de outras organizações, receberem 409 sob concorrência. Ela foi substituída pela trava otimista na linha do agente. A decisão original (até 3 ativas) não muda.
- **Proteções obrigatórias:** `AgentOperation`, `last_used_at` e a proteção contra abuso de autenticação (limite por IP para tokens desconhecidos) continuam na etapa 10. Sem elas, a autenticação de agentes não está pronta para produção.

## Implementação da etapa 6 (2026-11-18)

- **Ator na concessão:** `TaskService.CreateAsync` e `TransitionAsync` recebem só a concessão, que identifica a pessoa ou o agente. Isso muda a assinatura interna dos serviços, mas não as rotas.
- **Contrato de tarefa:** `createdByUserId` passou a anulável; `createdBy { id, kind, name }` e `version` foram acrescentados, e `expectedVersion` é opcional no `PATCH` e na transição de pessoas. A Web foi ajustada na mesma etapa, e a CLI ainda não existe.
- **Versão:** `ProjectTask.Revision`, na coluna `version`, é incrementada num ponto único do `SaveChanges` (`TaskRevisionRules`) e convive com o `xmin`, que continua sendo o token de concorrência de todas as entidades.
- **Deadlock entre edições simultâneas:** o `PATCH` marcava a tarefa inteira como modificada, e edições simultâneas da mesma tarefa podiam causar um deadlock (40P01), que virava 500. Agora só a versão é marcada, e `SaveChangesConflicts` trata o 40P01 como conflito (409).

## Implementação das etapas 11 e 12 (2026-11-22)

- **Prefixo na Web:** a API guarda e devolve o prefixo sem o marcador (8 caracteres depois de `gsk_`). A Web o exibe como `gsk_xxxxxxxx…`, igual ao começo do token que a pessoa guardou. O contrato da API não mudou.
- **`no-store` na Web:** além da resposta da criação na API, todas as telas `/o/{org}/agentes/...` da Web respondem `Cache-Control: no-store` (`UseNoStoreForAgentPages`).
- **Ponta a ponta:** `AgentEndToEndTests` percorre os oito passos da estratégia de testes com um token real e procura o token em todas as tabelas e no log; só o hash aparece, em `agent_credentials`.
- **Mutações de segurança:** as oito da estratégia de testes, inclusive o `CHECK` de autoria, foram detectadas pela suíte de agentes ([relatório da Fase 10](../reports/FASE-10-RELATORIO.md)).

## Consequências

- **Políticas:** todas as rotas passam a ter uma política explícita. Uma rota sem política faz o teste falhar.
- **Workflows:** um workflow sem nenhum caminho de pessoa até Concluída deixa tarefas de agentes paradas em revisão. É o comportamento desejado.
- **Contrato:** a mudança em `TaskDetails` exige ajustar a Web na mesma etapa. A CLI (Fase 11) já nasce com o contrato novo.
- **Latência:** cada requisição de agente faz, além da operação, a busca do hash, o upsert do contador e a gravação da operação. A medição fica para a Fase 13.
- **Escala:** o limite de falhas por IP é por instância e depende dos forwarded headers atrás de um proxy (Fase 14).
- **Mudança de prazo:** alterar a retenção das operações exige uma migração do gatilho.
- **AGENTS.md:** fica atualizado com as políticas por esquema, as exceções de isolamento, a rota de transição de agentes, a autoria e as regras de agentes em `TransitionRules`.
- **Revisões:** o ADR-006 e a especificação mestre ganharam notas que remetem a esta exceção.
