# ADR-011: Fases, marcos, tarefas e anexos

- **Status:** Aceito
- **Data:** 2026-11-12
- **Fase:** 06 (Fases, marcos e tarefas). Decisões D1 a D7 aprovadas pelo responsável em 2026-11-11.

## Contexto

A especificação mestre prevê fases, marcos e tarefas com vários responsáveis, prioridade, prazo, comentários, etiquetas, dependências, anexos protegidos, arquivamento, filtros e paginação (seções 4.3, 4.4 e 5.4). Os estados das tarefas não podem ser um enum rígido e chegam com o workflow configurável da Fase 07. O acesso a projetos já é calculado num único ponto ([ADR-010](ADR-010-PROJETOS-E-EQUIPES.md)).

## Decisão

### Estado da tarefa (D1)

A tarefa não tem campo de estado nesta fase, só o arquivamento lógico. A Fase 07 cria o workflow por projeto e dá o estado inicial às tarefas existentes, por migration.

### Anexos (D2)

- **Armazenamento:** a abstração `IFileStorage` fica na Application, e a implementação `LocalFileStorage`, em disco local, na Infrastructure. A Fase 14 troca por object storage compatível com S3 sem mudar a Application.
- **Diretório:** `Storage:Attachments:RootPath` é obrigatório, validado na inicialização e aceita variáveis de ambiente. Em desenvolvimento, `%LOCALAPPDATA%/Gestalyn/storage`; nos testes, um diretório temporário por instância.
- **Chave do arquivo:** `attachments/{organização}/{anexo}`, gerada pelo sistema. Só aceita letras minúsculas, números e barras, e o caminho final é conferido contra a raiz. O arquivo é gravado num temporário e depois movido, para nunca ficar pela metade.
- **Validação:**
  - até 10 MB, até 20 anexos por tarefa e arquivo vazio recusado;
  - tipos permitidos: PNG, JPEG, GIF, WebP, PDF, DOCX, XLSX, PPTX, TXT, LOG, MD, CSV e JSON;
  - o tipo é conferido pela extensão e pelo conteúdo: assinatura do arquivo nos formatos binários e UTF-8 válido, sem bytes nulos, nos de texto;
  - SVG, HTML, scripts e executáveis nunca são aceitos;
  - o tipo de conteúdo vem da lista do servidor, nunca do cliente;
  - o nome é limpo: sem diretórios, caracteres de controle ou reservados, com no máximo 255 caracteres. Ele fica só no banco e nunca é usado no disco;
  - o SHA-256 do conteúdo é registrado.
- **Download:** só pela API, com o acesso ao projeto da tarefa, `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; sandbox` e `Cache-Control: private, no-store`.
- **Consistência:**
  - no envio, o arquivo é gravado antes da linha no banco e apagado se essa gravação falhar;
  - na exclusão, a linha sai primeiro. Se depois o arquivo não puder ser apagado, a falha só é registrada em log, porque o arquivo órfão não fica acessível;
  - a FK do anexo para a tarefa é `Restrict`, para nada apagar linhas sem apagar arquivos.

### Fases e marcos (D3)

- **Fase:** nome, descrição, início e fim (*check constraint* `end_date >= start_date`) e posição única no projeto, com no máximo 50 fases.
  - **Reordenação:** recebe a lista completa de IDs e roda numa transação. O projeto é marcado como alterado, para que o `xmin` serialize reordenações simultâneas; depois as posições são deslocadas e gravadas na ordem final, respeitando o índice único e `position > 0`.
- **Marco:** nome, descrição, data prevista, fase opcional do mesmo projeto e `ReachedAt`. Marcar de novo como atingido mantém a primeira data.
- **Vínculo da tarefa:** ela pode estar numa fase e num marco do mesmo projeto, por FKs compostas `(project_id, phase_id)` e `(project_id, milestone_id)`. Com o valor nulo, a FK não se aplica.
- **Exclusão:** física, permitida só quando nenhuma tarefa usa a fase ou o marco (409). Ao excluir uma fase, os marcos ligados a ela ficam sem fase.

### Responsáveis (D4)

- Até 10 por tarefa.
- `task_assignments` tem uma FK composta `(organization_id, project_id, user_id)` → `project_members`, com cascata. Por isso, só quem está na equipe pode ser responsável, e quem sai da equipe deixa de ser. Para servir de alvo da FK, a unicidade correspondente em `project_members` passou a ser chave alternativa.

### Dependências (D5)

- Só um tipo: "bloqueada por". As duas tarefas são do mesmo projeto, e o banco garante isso com duas FKs compostas `(organization_id, project_id, task_id)` que usam a mesma coluna `project_id`.
- Recusas:
  - dependência consigo mesma: 400, também barrada por *check constraint*;
  - duplicada: 409;
  - de outro projeto: 400;
  - de tarefa arquivada: 409;
  - mais de 50 por tarefa: 400.
- **Ciclos:** recusados com 409. A verificação percorre o grafo de dependências do projeto em memória (`TaskDependency.WouldCreateCycle`) e roda numa transação serializável junto com a gravação. Um conflito de serialização responde 409.
- Nesta fase a dependência só informa. A Fase 07 poderá impedir a conclusão de uma tarefa bloqueada.

### Permissões (D6)

Permissões novas no catálogo de projeto. Como os papéis de projeto são definidos no código, não há migração de dados.

| Permissão | Uso | Gerente | Colaborador | Observador |
|---|---|---|---|---|
| `project.plan` | Fases e marcos | sim | não | não |
| `tasks.write` | Criar e editar tarefas, responsáveis, dependências e anexos | sim | sim | não |
| `tasks.comment` | Comentar | sim | sim | não |
| `tasks.archive` | Arquivar tarefas e excluir comentários de outros | sim | não | não |

- Ler tarefas, comentários e anexos exige `project.read`.
- Comentário: só o autor edita; o autor ou quem tem `tasks.archive` exclui.
- `projects.manage_all` continua concedendo todas as permissões de projeto.
- Nas rotas `/tasks/{taskId}`, o atributo `[TaskPermission]` calcula o acesso pelo projeto da tarefa (`IProjectAccess.FindByTaskAsync`). Tarefa de projeto invisível responde 404.

### Número da tarefa (D7)

- `projects.next_task_number` é incrementado com a trava otimista do projeto. Em criações simultâneas, a que conflita tenta de novo, com até 3 tentativas, e depois responde 409.
- O índice único `(project_id, number)` garante a unicidade.
- O número não muda nem é reutilizado. As rotas usam o UUID.

### Demais regras

- **Prioridade:** a enumeração passou a se chamar `Priority` e é compartilhada por projetos e tarefas. Os valores no JSON não mudaram.
- **Etiquetas:** as regras foram unificadas em `Labels` (de 1 a 50 caracteres, até 20, únicas sem diferenciar maiúsculas e minúsculas), para projetos e tarefas.
- **Somente leitura:** tarefa arquivada não aceita alterações, comentários, responsáveis, dependências nem anexos (409). Em projeto arquivado, tarefas, fases e marcos também ficam somente leitura.
- **Listagem:**
  - filtros `phaseId`, `milestoneId`, `assigneeId`, `assignedToMe`, `label`, `priority`, `dueBefore`, `dueAfter`, `overdue`, `search` e `includeArchived`;
  - `search` usa `ILIKE` com os caracteres curinga escapados;
  - ordenação `Number` (padrão), `DueDate` (sem prazo por último), `Priority` (pela ordem da enumeração, e não pelo texto gravado) ou `UpdatedAt`, crescente ou decrescente.
- **Conflitos:** o helper `SaveChangesConflicts` converte concorrência, unicidade, FK e serialização em 409 em todos os serviços.

## Alternativas consideradas

| Alternativa | Motivo da rejeição |
|---|---|
| Enum provisório de estado | Contraria a especificação e criaria um contrato que mudaria na Fase 07 |
| Anexos no PostgreSQL (`bytea`) | Aumenta o banco e a memória das consultas |
| S3 compatível (MinIO) agora | Contêiner, pacote e credenciais antes da fase de infraestrutura |
| Responsáveis de toda a organização | A pessoa ficaria responsável por tarefa de um projeto que talvez nem veja |
| Dependências entre projetos | A tarefa bloqueadora poderia estar num projeto invisível para o usuário |
| Número calculado como o maior número + 1 | Conflito de unicidade sob concorrência, ou trava de toda a tabela |
| Arquivamento lógico de fases e marcos | Sem critério que o exija; excluir itens vazios basta para corrigir o planejamento |

## Consequências

- O workflow da Fase 07 acrescenta o estado às tarefas existentes e pode usar as dependências para impedir conclusões.
- Disco local não serve para várias instâncias: a Fase 14 implementa `IFileStorage` sobre S3 e migra os arquivos pela chave.
- Antivírus e cotas de armazenamento por organização ficam para as Fases 13 e 14.
- Toda rota por ID de recurso dentro de um projeto (documentos, atividades) segue o modelo do `[TaskPermission]`: localizar o projeto do recurso e calcular o acesso com `IProjectAccess`.
