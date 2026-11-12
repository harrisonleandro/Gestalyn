# Relatório final: Fase 06, Fases, marcos e tarefas

- **Data:** 2026-11-12
- **Ambiente:** Windows 11 Pro · .NET SDK 10.0.404 · Docker 29.8.2 · PostgreSQL 18 · Mailpit v1.29
- **Branch:** `feature/fase-06-tarefas` (a partir de `develop`, com as Fases 01 a 05 integradas)
- **Estado:** **In Review.** Critérios verificados localmente (testes, mutação e execução real da API); faltam o CI no GitHub e a integração em `develop`.
- **Formato:** seção 17 da [especificação mestre](../specifications/ESPECIFICACAO-MESTRE.md)

## 1. Nome e número da fase

Fase 06: Fases, marcos e tarefas. A especificação da fase está em [FASE-06-TAREFAS.md](../specifications/FASE-06-TAREFAS.md).

## 2. Objetivo

Gerenciamento operacional do trabalho dentro de cada projeto: fases e marcos, e tarefas com vários responsáveis, prioridade, prazo, comentários, etiquetas, dependências e anexos protegidos, com arquivamento, filtros e paginação. Todo acesso passa pelo cálculo de acesso ao projeto da Fase 05.

## 3. Resumo das implementações

- **Fases (D3):**
  - nome, descrição, início e fim;
  - posição, com reordenação pela lista completa em transação;
  - exclusão só sem tarefas; os marcos da fase ficam sem fase.
- **Marcos (D3):** data prevista, fase opcional do mesmo projeto e data em que foi atingido.
- **Tarefas (D1 e D7):**
  - número sequencial por projeto, com contador protegido por `xmin` e novas tentativas;
  - título, descrição em Markdown, prioridade, prazo, fase e marco do mesmo projeto (FKs compostas) e etiquetas;
  - arquivamento lógico somente leitura;
  - nenhum estado até o workflow da Fase 07.
- **Responsáveis (D4):** até 10 por tarefa, só da equipe do projeto. Uma FK composta com cascata os remove quando saem da equipe.
- **Comentários (D6):** só o autor edita; o autor ou quem tem `tasks.archive` exclui.
- **Dependências (D5):** "bloqueada por", no mesmo projeto, sem duplicatas nem ciclos. A verificação do grafo e a gravação acontecem numa transação serializável.
- **Anexos (D2):**
  - armazenamento por `IFileStorage`, com `LocalFileStorage`: chave gerada pelo sistema, caminho conferido contra a raiz e gravação atômica;
  - até 10 MB, tipos conferidos pela extensão e pelo conteúdo, nome limpo e SHA-256;
  - download só pela API, como anexo, com `nosniff` e CSP `sandbox`.
- **Permissões (D6):** `project.plan`, `tasks.write`, `tasks.comment` e `tasks.archive` por papel de projeto. `[TaskPermission]` calcula o acesso pelo projeto da tarefa.
- **API:**
  - `/projects/{id}/phases`, `/milestones` e `/tasks`, com filtros, ordenação e paginação;
  - `/tasks/{id}`, com responsáveis, dependências, comentários e anexos.
- **Comum:**
  - `Priority`, compartilhada por projetos e tarefas;
  - `Labels`, com as regras de etiquetas;
  - `SaveChangesConflicts`, que converte conflitos de gravação em 409.
- **Migrations:** `AddPlanningAndTasks` e `AddTaskAttachments`.
- **Documentação:** ADR-011, `docs/api/TASKS.md`, ARCHITECTURE (§3.5 com diagrama ER), PROJECT_STRUCTURE, README, AGENTS, especificação e status.

## 4. Arquivos criados

| Área | Arquivos |
|---|---|
| Domain | `Common/{Priority,Labels}.cs`, `Projects/Milestone.cs`, `Tasks/{ProjectTask,TaskAssignment,TaskLabel,TaskComment,TaskDependency,TaskAttachment,AttachmentFiles}.cs` |
| Application | `Abstractions/Storage/IFileStorage.cs`, `Planning/{IPlanningService,PlanningContracts}.cs`, `Tasks/{ITaskService,TaskContracts,AttachmentContracts}.cs` |
| Infrastructure | `Planning/PlanningService.cs`, `Tasks/{TaskService,AttachmentService}.cs`, `Storage/LocalFileStorage.cs`, `Persistence/SaveChangesConflicts.cs`, `Persistence/Configurations/TaskConfigurations.cs`, migrations `20261112011239_AddPlanningAndTasks` e `20261112012439_AddTaskAttachments` |
| API | `Controllers/{PlanningController,TasksController}.cs` |
| Testes | `UnitTests/Domain/{TaskRulesTests,AttachmentFilesTests}.cs`; `IntegrationTests/Tasks/{TaskTestKit,PlanningTests,TaskTests,CommentAndDependencyTests,TaskAccessTests,AttachmentTests}.cs` |
| Documentação | `docs/specifications/FASE-06-TAREFAS.md`, `docs/decisions/ADR-011-TAREFAS-E-ANEXOS.md`, `docs/api/TASKS.md`, este relatório |

## 5. Arquivos modificados

| Arquivo | Alteração |
|---|---|
| `Domain/Projects/{Project,ProjectPhase,ProjectTag,ProjectPermissions}.cs` | Contador de tarefas, limite de fases, fase completa, regras de etiquetas em `Labels` e permissões de tarefa |
| `Application/Projects/{ProjectAccess,ProjectContracts}.cs` | `IProjectAccess.FindByTaskAsync`; `Priority` |
| `Infrastructure/Projects/{ProjectService,ProjectAccessResolver}.cs` | Conflitos de gravação pelo helper comum; acesso pelo projeto da tarefa |
| `Infrastructure/Persistence/Configurations/{ProjectConfiguration,ProjectPhaseConfiguration}.cs`, `ProjectManagerDbContext.cs`, `DependencyInjection.cs` | Novas entidades, chaves alternativas, `DbSet`, serviços e `StorageOptions` |
| `API/Organizations/ProjectPermissionAttribute.cs` | Base comum `ProjectResourcePermissionAttribute`, com `[ProjectPermission]` e `[TaskPermission]` |
| `API/appsettings.Development.json` | `Storage:Attachments:RootPath` |
| `IntegrationTests/Infrastructure/ApiFactory.cs` | Diretório temporário de anexos por instância, apagado no descarte |
| `README.md`, `AGENTS.md`, `docs/architecture/*`, `docs/specifications/README.md`, `docs/PROJECT_STATUS.md` | Documentação da fase |

## 6. Dependências adicionadas

Nenhuma. A verificação de tipos de arquivo usa assinaturas conhecidas e a validação UTF-8 do .NET.

## 7. Justificativas técnicas

- **Sem estado na tarefa (D1):** respeita a regra de não usar enum rígido e evita um contrato que mudaria na Fase 07.
- **Disco local por trás de `IFileStorage` (D2):** resolve a fase sem infraestrutura nova, e a troca para S3 na Fase 14 fica restrita à implementação.
- **Tipo conferido pelo conteúdo:** impede, por exemplo, um executável renomeado para `.png`.
- **Download sempre como anexo:** com `nosniff` e CSP `sandbox`, nem mesmo um arquivo de texto com HTML roda no navegador.
- **FKs compostas com `project_id`:** o banco garante que fase, marco, responsáveis e dependências são do mesmo projeto, mesmo com SQL direto (testado).
- **Transação serializável nas dependências:** a verificação de ciclo lê o grafo inteiro. Sem serialização, duas inclusões simultâneas poderiam formar um ciclo juntas.
- **Contador de números no projeto:** usa a trava otimista que já existe, sem SQL bruto nem bloqueio de tabela.

## 8. Testes executados

| Suíte | Novos nesta fase | Conteúdo |
|---|---|---|
| UnitTests (`TaskRulesTests`) | 12 | Números sequenciais, projeto arquivado, datas e limite de fases, marco, vínculos da tarefa, responsáveis, tarefa arquivada, etiquetas, edição de comentário, regras de dependência, detecção de ciclos (direto, longo e em losango) e permissões por papel |
| UnitTests (`AttachmentFilesTests`) | 28 casos | Tipos aceitos com conteúdo correspondente; recusa de executável, executável renomeado, JPEG como PNG, SVG, HTML, JS, arquivo sem extensão, texto com nulo, texto que não é UTF-8, WebP falso e arquivo vazio; limpeza de nomes (caminhos, caracteres reservados e de controle, pontos, vazio e tamanho) |
| IntegrationTests (`PlanningTests`) | 3 | Fases (ordem, datas, reordenação, exclusão), marcos (fase alheia, atingido, exclusão com tarefas) e permissão de planejamento |
| IntegrationTests (`TaskTests`) | 6 | Criação completa, números sob concorrência (8 criações simultâneas), `PATCH` parcial, arquivamento e projeto arquivado, responsáveis da equipe e listagem (todos os filtros, ordenações e paginação) |
| IntegrationTests (`CommentAndDependencyTests`) | 2 | Autoria dos comentários; dependências (consigo mesma, duplicada, outro projeto, ciclos curto e longo, arquivada, remoção) |
| IntegrationTests (`TaskAccessTests`) | 4 | Matriz de permissões por papel, 404 em todos os endpoints para dono de outra organização, filtro de leitura das novas tabelas e FKs compostas recusando vínculos entre projetos e organizações |
| IntegrationTests (`AttachmentTests`) | 4 | Gravação sob o ID e cabeçalhos do download; recusa de tipos, conteúdo falsificado, vazio e acima de 10 MB; permissões (inclusive membro fora da equipe) e isolamento; exclusão do arquivo e tarefa arquivada |

## 9. Resultado do build

`dotnet restore`: exit 0. `dotnet build -c Release`: **0 avisos, 0 erros**, com analisadores `Recommended` e avisos tratados como erros.

## 10. Resultado dos testes

| Suíte | Resultado |
|---|---|
| UnitTests | **99/99** |
| IntegrationTests (PostgreSQL 18 e Mailpit via Testcontainers) | **96/96** |
| Total | **195/195** |

## 11. Evidências

- **Mutação 1:** sem a verificação de ciclo, `Dependencies_RejectSelfDuplicatesOtherProjectsAndCycles` falha.
- **Mutação 2:** sem o `[TaskPermission]` no download, `Attachments_FollowProjectPermissions_AndAreInvisibleToOtherOrganizations` falha. As duas mutações foram desfeitas e conferidas.
- **Execução real** (API local, migrations aplicadas no banco de desenvolvimento, dois usuários reais):

  | Verificação | Resultado |
  |---|---|
  | Fases e reordenação | Posições 1 e 2; depois da reordenação, 1: Construção e 2: Descoberta |
  | Colaboradora cria fase | 403 |
  | Tarefas | Números 1, 2 e 3; etiquetas `Web` e `web` viraram uma só |
  | Dependências | #1 depende de #2 e #2 depende de #3 (200); #3 depender de #1 respondeu 409 (ciclo) |
  | Gerente edita comentário alheio; colaboradora arquiva tarefa | 403 e 403 |
  | Upload de PNG com o nome `../../segredo<x>.png` | 201, salvo como `segredox.png`, `image/png` |
  | Executável enviado como `.png` | 400 |
  | Download | Bytes idênticos aos enviados, `attachment; filename=segredox.png` e `nosniff` |
  | Listagem | Ordem por prioridade correta; "minhas tarefas" da colaboradora trouxe #1 |
  | Detalhes de #2 | Depende de #3 e bloqueia #1 |

  No disco, o único arquivo tem como nome o ID do anexo (70 bytes) e fica no diretório da organização.

## 12. Problemas encontrados

1. CA1859 (tipo de parâmetro), CA1305 (data formatada sem cultura) e CS0407 (grupo de método) durante o build.
2. A ordenação por prioridade, que é gravada como texto, precisava seguir a ordem da enumeração, e não a alfabética.
3. Responsáveis atribuídos no mesmo instante saíam em ordem indefinida, o que fazia um teste falhar às vezes.
4. A limpeza de nome transformava `.pdf` em `pdf`, perdendo a extensão.
5. Na primeira mutação do download, nenhum teste falhou: o filtro de organização já bastava nos casos entre organizações, e faltava o caso do membro fora da equipe.
6. O Docker estava parado no início da validação, e os testes de integração não conectavam.
7. O caminho de desenvolvimento dos anexos ficou com `attachments/attachments`.

## 13. Problemas corrigidos

1. Ajustes pontuais no código e nos testes, sem supressões.
2. Ordenação por uma expressão traduzida para `CASE` no SQL.
3. Desempate pelo nome e teste independente da ordem.
4. A extensão passou a ser separada antes da limpeza das pontas, com um teste para o caso.
5. O teste com membro da organização fora da equipe foi acrescentado, e a mutação passou a ser detectada.
6. O Docker Desktop foi iniciado.
7. A raiz de desenvolvimento passou a ser `%LOCALAPPDATA%/Gestalyn/storage`.

## 14. Pendências

- Push de `feature/fase-06-tarefas`, CI e PR para `develop` (com autorização).
- Estados das tarefas e histórico de estados: Fase 07. Histórico de alterações: Fase 08.
- Armazenamento em S3 e migração dos arquivos: Fase 14. Antivírus e cotas: Fases 13 e 14.
- Interface Web (Fase 09), tarefas por agentes (Fase 10) e comandos `gestalyn task` (Fase 11).

## 15. Critérios atendidos

| Critério | Evidência |
|---|---|
| Tarefas funcionais | `TaskTests` (6), `PlanningTests` (3) e execução real |
| Responsáveis corretamente associados | `Assignees_MustBeOnTheTeam_AndLeaveTasksWhenTheyLeaveTheTeam`; FK composta recusando quem está fora da equipe (`CompositeForeignKeys_RejectLinksAcrossProjectsAndOrganizations`) |
| Dependências validadas | `Dependencies_RejectSelfDuplicatesOtherProjectsAndCycles`, `CycleDetection_FindsDirectAndLongCycles_AndAcceptsDiamonds`, FK recusando dependência entre projetos e mutação 1 |
| Anexos protegidos | `AttachmentTests` (4), `AttachmentFilesTests` (28 casos), mutação 2 e execução real |
| Filtros funcionando | `List_FiltersSortsAndPaginates` (todos os filtros, três ordenações, paginação e total) |

## 16. Critérios não atendidos

Nenhum critério funcional deixou de ser atendido. A verificação no CI e a integração por PR (definição de conclusão) estão pendentes, por isso a fase está **In Review**.

## 17. Riscos identificados

| Risco | Impacto | Mitigação |
|---|---|---|
| Disco local com várias instâncias ou contêiner efêmero | Anexos perdidos ou inacessíveis | `Storage:Attachments:RootPath` obrigatório fora de Development; S3 na Fase 14 |
| Arquivo malicioso de um tipo permitido (PDF ou DOCX com conteúdo perigoso) | Risco para quem abre o arquivo | Download sempre como anexo; antivírus avaliado nas Fases 13 e 14 |
| Upload carregado inteiro na memória (até 10 MB) | Pressão de memória com muitos uploads simultâneos | Limite de tamanho; streaming para o S3 na Fase 14 |
| Verificação de ciclo lê todas as dependências do projeto | Custo em projetos com muitas dependências | Até 50 por tarefa; medir na Fase 13 |
| Arquivos órfãos após falha na exclusão | Espaço em disco | Registro em log; rotina de limpeza quando houver Worker |

## 18. Estado do Git

- **Branch:** `feature/fase-06-tarefas`, somente local.
- **Commits:**
  - `93b95e9` (especificação);
  - `a87dd63` (fases, marcos, tarefas, comentários e dependências);
  - `b452cdb` (anexos);
  - commit de documentação de encerramento.
- **`develop`:** `4072cb5`.

## 19. Próxima fase recomendada

1. **Antes de tudo:** push desta branch, CI e PR para `develop`.
2. **Fase 07, Workflow configurável e histórico:**
   - `WorkflowDefinition`, `WorkflowState` e `WorkflowTransition` por projeto, com os estados iniciais;
   - migration que coloca todas as tarefas existentes no estado inicial;
   - categorias e estados finais;
   - transições validadas no backend, com restrições por permissão e regras de aprovação;
   - `TaskStatusHistory` e `GET /tasks/{id}/history`;
   - uso das dependências para impedir a conclusão de tarefas bloqueadas.
