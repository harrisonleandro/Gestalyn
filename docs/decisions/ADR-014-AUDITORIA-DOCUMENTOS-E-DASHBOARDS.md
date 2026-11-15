# ADR-014: Auditoria, documentos, decisões e dashboards

- **Status:** Aceito
- **Data:** 2026-11-15
- **Fase:** 08 (Auditoria, documentação e dashboards). Decisões D1 a D8 aprovadas pelo responsável em 2026-11-15.

## Contexto

A especificação mestre (seções 4.6, 5.6 e 5.7) pede:
- o registro de atividades, alterações e ações de usuários e agentes, com o autor e a origem de cada operação;
- documentos e decisões técnicas por projeto;
- dashboards e estatísticas com dados reais;
- progresso calculado por regras documentadas, sem percentuais arbitrários.

Até a Fase 07, o único histórico era o de estados das tarefas (ADR-013), e as entidades guardavam só as datas de criação e de alteração (Fase 02).

## Decisão

### Atividades (D1)

- **`ActivityEvent`:** registro automático e imutável de uma alteração, gerado pelo backend. Guarda:
  - organização, projeto e tarefa (os dois últimos opcionais);
  - autor, tipo de autor e origem, mais o ID da requisição;
  - ação, do catálogo `ActivityActions`;
  - tipo e ID da entidade, e um resumo legível;
  - as alterações campo a campo, em `changes` (JSON).
- **`ActivityLog`:** registro manual de uma atividade (`Note`, `Meeting` ou `Other`), criado por `POST /projects/{id}/activities`.
  - É imutável: uma correção é um novo registro.
  - Fica separado dos eventos para distinguir o que alguém declarou do que o sistema registrou (seção 6.4).
- **Uma só lista:** `GET /projects/{id}/activities` mostra os dois, mais novos primeiro, com `source` (`Event` ou `Log`).
  - A paginação é feita sobre a união dos IDs e datas (`UNION ALL`).
  - Os detalhes da página são lidos depois.

### Geração dos eventos (D2)

- **`ActivityRecorder`:** os serviços registram o evento explicitamente, depois de validar a operação e antes de gravar. O evento entra no mesmo `SaveChanges` da alteração: ou os dois são gravados, ou nenhum.
- **Alterações campo a campo:** saem dos valores originais e atuais do EF, com os nomes do contrato.
  - Ficam de fora chaves, propriedades sombra (`xmin`) e campos técnicos.
  - Textos longos registram `{ "changed": true }`, sem o conteúdo: descrições, corpos, contexto e resultado das decisões.
  - Listas, como etiquetas, registram o que entrou e o que saiu.
- **Cobertura garantida por teste:** `ActivityCoverageTests` chama cada rota de escrita da API e confere que ela gera exatamente um evento, com a ação esperada.
  - Uma rota nova sem evento, ou que fique fora do cenário do teste, faz o teste falhar.
  - Exceções explícitas: as rotas de autenticação e o próprio registro manual.
- **Sem eventos retroativos:** os eventos começam nesta fase.

### Autor e origem (D3)

- **Autor:** sempre o `sub` do token (`HttpActivityContext`), nunca um valor do corpo.
  - Até a Fase 10, todo autor é uma pessoa (`ActorKind.User`).
  - Fora de uma requisição, a operação é do sistema (`SystemActivityContext`).
- **Origem:** vem do cabeçalho **`X-Gestalyn-Client`**.
  - `web` vira `Web`, `cli` vira `Cli` e `mcp` vira `Mcp`. Sem o cabeçalho, ou com um valor desconhecido, a origem é `Api`.
  - `System` nunca vem de fora.
  - A Web (BFF) envia `web`. A CLI enviará `cli` quando chamar a API (Fase 11).
  - **A origem é declarada pelo cliente:** é informativa e nunca concede acesso.
- **ID da requisição:** o `TraceIdentifier` do ASP.NET Core, para correlacionar o evento com os logs.
- **Dados não guardados:** IP e User-Agent, por minimização de dados pessoais. Pode ser revisto na Fase 13.

### Leitura e imutabilidade (D4)

- **Projeto:** `GET /projects/{id}/activities` exige `project.read`.
- **Organização:** `GET /organizations/{id}/activities` traz os eventos da organização e os de todos os projetos.
  - Exige a permissão nova **`activity.read_all`** (Proprietário e Administrador).
  - A migration a concede aos papéis de sistema existentes, com teste.
- **Filtros:** `actorId`, `actorKind`, `origin`, `action`, `entityType`, `taskId`, `from`, `to`, `source` e, na organização, `projectId`.
  - `action` e `entityType` só existem nos eventos: usá-los exclui os registros manuais.
- **Imutabilidade:** a API não tem rota de alteração nem de exclusão.
  - No banco, gatilhos (`reject_activity_change`) recusam `UPDATE`, `DELETE` e `TRUNCATE` em `activity_events` e `activity_logs`.
  - **Justificativa do SQL bruto** (exigida pelo AGENTS.md): é a única forma de impedir alterações também por SQL direto. Fica restrito a gatilhos, sem tocar em dados.
  - Uma remoção excepcional exige migration revisada e um novo ADR.
- **Dados pessoais:** e-mails nunca vão para os eventos. Um convite aparece pelo ID e pelo papel.
- **FKs:**
  - compostas para o projeto e para a tarefa;
  - sem FK para o autor, que pode ser um agente, nem para a entidade, que pode ser excluída (fase, marco, estado, comentário).

### Documentos (D5)

- **`ProjectDocument`:** documento em Markdown, com título de até 200 caracteres e corpo de até 200 mil caracteres, guardado como veio.
  - A Web (Fase 09) renderiza com sanitização.
- **Versões:** cada edição cria uma `ProjectDocumentVersion` imutável, e o documento guarda a versão atual.
- **Edição:** `PUT /documents/{id}` exige `expectedVersion`; uma versão diferente da atual responde 409.
  - Duas edições simultâneas a partir da mesma versão: o `xmin` do documento e o índice único `(document_id, number)` deixam só uma vencer.
- **Permissões novas no catálogo de projeto:**
  - `documents.write` (Gerente e Colaborador) cria e edita;
  - `documents.manage` (Gerente) arquiva e restaura.
  - Não exigem migração, porque o catálogo de projeto é calculado pelo papel fixo.
- **Arquivado:** o documento fica somente leitura até ser restaurado.
- **Acesso:** as rotas `/documents/{id}` usam `[DocumentPermission]`, que calcula o acesso pelo projeto do documento.

### Decisões (D6)

- **`Decision`:** registro no estilo de ADR, com título, contexto, decisão (`outcome`), alternativas, consequências e uma tarefa relacionada opcional, do mesmo projeto.
- **Estados:** `Proposed` passa a `Accepted` ou `Rejected`; `Accepted` passa a `Superseded`.
  - **Substituição:** só por outra decisão aceita do mesmo projeto, garantido também por FK composta e por *check* contra substituir a si mesma.
- **Edição:** só enquanto proposta. Decidida, a decisão é imutável: `DecisionRuleException` responde 409.
- **Permissões:** `documents.write` propõe e edita propostas; `documents.manage` aceita, rejeita e substitui.
  - Assim, quem propõe sem ser Gerente não aceita a própria proposta.
- **Acesso:** as rotas `/decisions/{id}` usam `[DecisionPermission]`.

### Progresso (D7)

- **Regra única** (`ProgressRule`, nome `doneOverNotCanceled`): concluídas ÷ (tarefas − canceladas), contando só tarefas não arquivadas.
- **Contagem:**
  - concluída é a tarefa num estado da categoria `Done`;
  - as canceladas saem da conta;
  - `Blocked`, `InProgress` e `Todo` contam como não concluídas.
- **Sem tarefas na conta:** o progresso é `null`, nunca 0% nem 100%.
- **Resposta:** traz sempre as contagens por categoria e o nome da regra. O percentual tem uma casa decimal.
- **Alcance:** a mesma regra vale para o projeto, cada fase e cada marco. Não há pesos.

### Dashboards e estatísticas (D8)

- **Cálculo na hora:** consultas agregadas no banco, sem tabelas de resumo nem cache.
- **Organização:** `GET /organizations/{id}/dashboard`.
  - **Ajuste de contrato:** a especificação prevê `/dashboard/overview`. A rota segue a regra da organização ativa pela rota (ADR-008).
  - Só considera os projetos ativos que o usuário enxerga, pela mesma regra da listagem de projetos (ADR-010).
  - Exige `organization.read`.
- **Projeto:**
  - `GET /projects/{id}/dashboard`: progresso; tarefas por estado e por categoria; atrasadas, a vencer em 7 dias e bloqueadas (contagem e as 10 primeiras); os 5 próximos marcos; e as 10 atividades mais recentes;
  - `GET /projects/{id}/progress`;
  - `GET /projects/{id}/statistics?weeks=` (de 1 a 26 semanas).
- **Estatísticas:**
  - tarefas criadas e concluídas por semana, e o tempo de ciclo (mediana e percentil 85, pelo posto mais próximo, em dias);
  - **conclusão** é a primeira entrada da tarefa num estado da categoria `Done`;
  - as estatísticas contam tarefas arquivadas depois, porque são histórico;
  - semanas em UTC, de segunda a domingo.
- **Categoria no histórico:** `TaskStatusHistory.ToCategory` guarda a categoria do destino na data. Assim, as estatísticas não dependem da categoria atual de um estado que mudou ou foi excluído.
  - A migration `AddStatusHistoryCategory` preenche os registros existentes com a categoria atual do estado. Fica nula só se o estado já foi excluído.
  - É SQL sobre dados de organizações, justificado aqui: é a única forma de dar a categoria aos registros antigos, e roda uma vez, na migration.

## Alternativas consideradas

| Alternativa | Motivo da rejeição |
|---|---|
| Uma só tabela para eventos e registros manuais | Mistura o que o sistema garante com o que alguém declarou (seção 6.4) |
| Interceptor genérico do EF | Registra ruído técnico e perde a intenção da operação |
| Eventos assíncronos por fila | Infraestrutura nova e janela de perda |
| Origem por credencial de cliente | A Web e a CLI usam o mesmo login, e um segredo na CLI não é segredo |
| Guardar IP e User-Agent | Dado pessoal sem uso definido |
| Leitura da auditoria da organização com `members.read` | O Membro veria a atividade de projetos que não enxerga |
| Imutabilidade só pela API | Uma correção manual no banco apagaria rastros |
| Documentos como arquivos no `IFileStorage` | Sem versões nem edição pela API |
| Progresso ponderado pela prioridade, ou "meio concluída" | Percentuais arbitrários, proibidos pela especificação |
| Tabelas de resumo para os dashboards | Um segundo lugar para errar; o volume atual não justifica |
| Categoria atual do estado nas estatísticas | Mudaria o passado ao mudar ou excluir um estado |

## Consequências

- **Fase 09:** usa as atividades, os documentos (com sanitização do Markdown), as decisões e os dashboards nas telas.
- **Fase 10:**
  - os agentes entram como `ActorKind.Agent`, pelo tipo da credencial;
  - os relatórios de trabalho (seção 6.4) viram registros com origem e autor próprios;
  - `TaskStatusHistory` passa a usar o tipo de autor do contexto.
- **Fase 11:** a CLI envia `X-Gestalyn-Client: cli`.
- **Fase 13:**
  - medir o custo das consultas dos dashboards e da lista de atividades;
  - revisar a auditoria de eventos de autenticação e a retenção.
- **Toda rota de escrita nova** precisa registrar o evento e entrar no cenário de `ActivityCoverageTests`.
