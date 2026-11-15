# Relatório final: Fase 08, Auditoria, documentação e dashboards

- **Data:** 2026-11-15
- **Ambiente:** Windows 11 Pro · .NET SDK 10.0.404 · Docker 29.8.2 · PostgreSQL 18 · Mailpit v1.29
- **Branch:** `feature/fase-08-auditoria` (a partir de `develop` `fe04718`, com as Fases 01 a 07 integradas)
- **Estado:** **Completed.** Critérios verificados localmente e no CI; integrada em `develop` pelo [PR #15](https://github.com/harrisonleandro/Gestalyn/pull/15) (merge `0e9dbdd`).
- **Formato:** seção 17 da [especificação mestre](../specifications/ESPECIFICACAO-MESTRE.md)

## 1. Nome e número da fase

Fase 08: Auditoria, documentação e dashboards. A especificação da fase está em [FASE-08-AUDITORIA.md](../specifications/FASE-08-AUDITORIA.md), e as decisões, no [ADR-014](../decisions/ADR-014-AUDITORIA-DOCUMENTOS-E-DASHBOARDS.md).

## 2. Objetivo

Dar visibilidade sobre o andamento dos projetos:
- um registro de atividades confiável, com o autor e a origem de cada alteração;
- documentos e decisões técnicas por projeto;
- dashboards, progresso e estatísticas calculados a partir de dados reais, por regras documentadas, sem percentuais arbitrários.

## 3. Resumo das implementações

- **Eventos de atividade (D1 e D2):**
  - `ActivityEvent` imutável, gravado pelo `ActivityRecorder` no mesmo `SaveChanges` da alteração;
  - 51 ações no catálogo, cobrindo organização, membros e convites, projeto e equipe, planejamento, tarefas, comentários, anexos, workflow, documentos e decisões;
  - alterações campo a campo em JSON, sem o conteúdo dos textos longos.
- **Registros manuais (D1):** `ActivityLog` (`Note`, `Meeting` ou `Other`), numa só lista com os eventos.
- **Autor e origem (D3):**
  - o autor vem do `sub` do token;
  - a origem vem do cabeçalho `X-Gestalyn-Client`, que a Web passou a enviar com `web`;
  - cada evento guarda o ID da requisição.
- **Leitura e imutabilidade (D4):**
  - lista por projeto (`project.read`) e por organização (`activity.read_all`, permissão nova, migrada para os papéis existentes), com filtros e paginação;
  - gatilhos no banco recusam `UPDATE`, `DELETE` e `TRUNCATE`.
- **Documentos (D5):** Markdown com versões imutáveis, edição pela versão esperada, arquivamento e restauração, e as permissões `documents.write` e `documents.manage`.
- **Decisões (D6):** proposta, aceita ou rejeitada, e substituída por outra aceita do mesmo projeto; imutável depois de decidida.
- **Progresso (D7):** `ProgressRule` (concluídas ÷ (tarefas − canceladas)), para o projeto, as fases e os marcos, sempre com as contagens.
- **Dashboards e estatísticas (D8):**
  - visão da organização, só com os projetos visíveis;
  - painel do projeto;
  - estatísticas semanais e tempo de ciclo.
  - O histórico de estados passou a guardar a categoria na data (`ToCategory`).
- **Migrations:** `AddActivity`, `AddDocumentsAndDecisions` e `AddStatusHistoryCategory`.
- **Documentação:** ADR-014, `docs/api/ACTIVITY.md`, `DOCUMENTS.md` e `DASHBOARDS.md`, ARCHITECTURE (§3.7 com diagrama ER), PROJECT_STRUCTURE, README, AGENTS, especificação e status.

## 4. Arquivos criados

| Área | Arquivos |
|---|---|
| Domain | `Activity/{ActivityActor,ActivityEvent,ActivityLog,ActivityActions}.cs`, `Documents/{ProjectDocument,Decision}.cs`, `Projects/ProgressRule.cs` |
| Application | `Abstractions/Activity/IActivityContext.cs`, `Activity/ActivityContracts.cs`, `Documents/DocumentContracts.cs`, `Dashboards/DashboardContracts.cs` |
| Infrastructure | `Activity/{ActivityRecorder,ActivityChanges,ActivityService,SystemActivityContext}.cs`, `Documents/{DocumentService,DecisionService}.cs`, `Dashboards/DashboardService.cs`, `Persistence/Configurations/{ActivityConfigurations,DocumentConfigurations}.cs`, três migrations |
| API | `Activity/HttpActivityContext.cs`, `Controllers/{ActivityController,DocumentsController,DashboardController}.cs` |
| Testes | `UnitTests/Domain/{ActivityRulesTests,DocumentRulesTests,ProgressRuleTests}.cs`; `IntegrationTests/Activity/{ActivityTestKit,ActivityCoverageTests,ActivityTests,ActivityPersistenceTests}.cs`, `Documents/DocumentTests.cs`, `Dashboards/DashboardTests.cs` |
| Documentação | `docs/specifications/FASE-08-AUDITORIA.md`, `docs/decisions/ADR-014-AUDITORIA-DOCUMENTOS-E-DASHBOARDS.md`, `docs/api/{ACTIVITY,DOCUMENTS,DASHBOARDS}.md`, este relatório |

## 5. Arquivos modificados

| Arquivo | Alteração |
|---|---|
| `Infrastructure/{Organizations,Projects,Planning,Tasks,Workflows}/*Service.cs` | Registro do evento em cada alteração |
| `Domain/Organizations/OrganizationPermissions.cs`, `Domain/Projects/ProjectPermissions.cs` | `activity.read_all`; `documents.write` e `documents.manage` |
| `Domain/Workflows/TaskStatusHistory.cs`, `Persistence/Configurations/WorkflowConfigurations.cs` | `ToCategory` |
| `Application/Projects/ProjectAccess.cs`, `Infrastructure/Projects/ProjectAccessResolver.cs` | Acesso pelo projeto do documento e da decisão |
| `API/Organizations/ProjectPermissionAttribute.cs` | `[DocumentPermission]` e `[DecisionPermission]` |
| `API/Program.cs`, `Infrastructure/DependencyInjection.cs`, `Persistence/ProjectManagerDbContext.cs` | Contexto da atividade, serviços e tabelas novas |
| `Web/Program.cs` | Cabeçalho `X-Gestalyn-Client: web` nas chamadas à API |
| `UnitTests/Domain/ProjectRulesTests.cs`, `IntegrationTests/Workflows/WorkflowMigrationTests.cs` | Permissões do Colaborador; categoria no histórico migrado |
| `README.md`, `AGENTS.md`, `docs/architecture/*`, `docs/specifications/README.md`, `docs/PROJECT_STATUS.md` | Documentação da fase |

## 6. Dependências adicionadas

Nenhuma.

## 7. Justificativas técnicas

- **Registro explícito, na mesma gravação (D2):**
  - os eventos carregam a intenção da operação ("aceitou", "removeu da equipe"), que um interceptor genérico perderia;
  - gravar junto com a alteração impede evento sem alteração e alteração sem evento.
- **Teste de cobertura por rota:** o risco do registro explícito é esquecer uma rota. O teste enumera as rotas da aplicação e falha se alguma não gerar exatamente um evento. A mutação 1 comprova isso.
- **Gatilhos de imutabilidade (D4):** a garantia vale também para SQL direto. Foi a única exceção ao "sem SQL bruto", justificada no ADR-014.
- **Origem declarada (D3):** útil para entender o uso, mas nunca usada para autorizar. O autor vem sempre da credencial.
- **Versão esperada nos documentos (D5):** duas pessoas não sobrescrevem o trabalho uma da outra. O `xmin` e o índice único de versão resolvem a corrida que a verificação na memória não pega.
- **Regra única de progresso (D7):** `null` sem dados evita os "0%" e "100%" enganosos. As contagens na resposta deixam o número verificável.
- **Categoria no histórico:** as estatísticas não podem mudar o passado quando um estado é recategorizado ou excluído.

## 8. Testes executados

| Suíte | Novos nesta fase | Conteúdo |
|---|---|---|
| UnitTests (`ActivityRulesTests`) | 5 métodos, 12 casos | Origem pelo cabeçalho; autor, resumo cortado e campos obrigatórios do evento; ID da requisição limitado; registro manual sem data futura; alterações campo a campo (texto longo sem conteúdo, listas, nulos, enumerações e datas) |
| UnitTests (`DocumentRulesTests`) | 6 | Versões; conflito de versão, arquivado e corpo acima do limite; projeto arquivado; decisões editáveis só como proposta; substituição só por outra aceita do mesmo projeto; tarefa relacionada do mesmo projeto |
| UnitTests (`ProgressRuleTests`) | 5 métodos, 12 casos | Regra de progresso com todas as categorias; `null` sem tarefas; agrupamento das contagens; mediana e percentil 85; semanas de segunda a domingo |
| IntegrationTests (`ActivityCoverageTests`) | 1 | Todas as rotas de escrita da API, cada uma com exatamente um evento e a ação esperada; lista de exceções explícita |
| IntegrationTests (`ActivityTests`) | 6 | Autor do token mesmo com outro ID no corpo; origens `Web`, `Cli` e `Api`; alterações sem conteúdo; operações recusadas sem evento; gatilhos; filtros e paginação da lista combinada; registro manual (validação e permissões); atividades da organização (permissão, isolamento e ausência de e-mails) |
| IntegrationTests (`ActivityPersistenceTests`) | 3 | Migração de `activity.read_all` para Proprietário e Administrador (e não para Membro, Leitor ou papel personalizado); filtro de leitura; FKs compostas |
| IntegrationTests (`DocumentTests`) | 4 | Versões, conflito e duas edições simultâneas; matriz de permissões, arquivamento e restauração; ciclo das decisões; FKs compostas das versões e da substituta |
| IntegrationTests (`DashboardTests`) | 4 | Progresso do projeto, das fases e dos marcos; painel do projeto; estatísticas com tempos conhecidos; visão da organização só com os projetos visíveis |

## 9. Resultado do build

`dotnet restore`: exit 0. `dotnet build -c Release`: **0 avisos, 0 erros**, com analisadores `Recommended` e avisos tratados como erros.

## 10. Resultado dos testes

| Suíte | Resultado |
|---|---|
| UnitTests | **178/178** |
| IntegrationTests (PostgreSQL 18 e Mailpit via Testcontainers) | **150/150** |
| Total | **328/328** |

O teste com duas edições simultâneas de documento passou em 5 execuções seguidas.

## 11. Evidências

- **Mutações** (todas desfeitas e conferidas):
  1. sem o registro do evento na exclusão de marco: `EveryWriteRoute_RecordsExactlyOneEvent` falha;
  2. canceladas no denominador do progresso: 3 testes unitários e 2 de dashboard falham;
  3. dashboard da organização ignorando a visibilidade: `OrganizationDashboard_CountsOnlyTheProjectsTheUserSees` falha;
  4. origem ignorando o cabeçalho: `Events_IdentifyTheAuthorFromTheToken_TheDeclaredOrigin_AndTheChangedFields` falha.
- **Migrations no banco de desenvolvimento:**
  - os 16 papéis de Proprietário e Administrador receberam `activity.read_all`;
  - os 15 registros de histórico ganharam a categoria, nenhum ficou nulo;
  - os 4 gatilhos de imutabilidade estão ativos.
- **Execução real** (API local, dois usuários reais, Gerente e Colaboradora; sem erros no log):

  | Verificação | Resultado |
  |---|---|
  | Atividades do projeto | 10 itens, com as origens `Web`, `Api` e `Cli` conforme o cabeçalho |
  | Edição com descrição | `changes` com o título de/para e a descrição como `{"changed":true}`; o texto da descrição não aparece na resposta; autor é a colaboradora |
  | Filtro `action=task.transitioned` | 4 |
  | Atividades da organização pela colaboradora (Membro) / pelo gerente | 403 / 200 com 13 itens e sem o e-mail da colaboradora |
  | Documento: edição pela versão 1, de novo pela versão 1 | 200, 409 |
  | Colaboradora arquiva o documento | 403 |
  | Versões | 2 e 1 |
  | Decisão: colaboradora aceita a própria proposta, gerente aceita, editar depois | 403, 200, 409 |
  | Progresso | 50% (1 concluída e 1 a fazer, regra `doneOverNotCanceled`) |
  | Dashboard do projeto | 1 atrasada, 0 bloqueadas, 10 atividades |
  | Estatísticas (4 semanas) | Semana atual com 2 criadas e 1 concluída; tempo de ciclo com 1 amostra |
  | Dashboard da organização (colaboradora) | 1 projeto, 1 tarefa aberta dela, 1 concluída nos últimos 7 dias |

## 12. Problemas encontrados

1. Um comando de shell com vários arquivos em heredoc falhou por causa das aspas dentro do código.
2. As estatísticas dependeriam da categoria atual dos estados, que pode mudar ou deixar de existir.
3. Na primeira tentativa de aplicar as migrations no banco local, faltou a senha na connection string de tempo de design.
4. Erros de analisador e compilação: parâmetro `organizationId` sem uso (IDE0060), matriz constante repetida (CA1861) e dois namespaces faltando nos testes.

## 13. Problemas corrigidos

1. Os arquivos foram escritos diretamente, e as edições passaram por um script de substituição exata, que falha se o trecho não for único.
2. `TaskStatusHistory.ToCategory`, com a migration que preenche os registros existentes (ajuste 1 da especificação).
3. As migrations foram aplicadas com `--connection`, lendo a senha do `.env` sem exibi-la, como no README.
4. Todos corrigidos. O build ficou com 0 avisos.

## 14. Pendências

- **Fase 09:** telas de atividades, documentos (com Markdown sanitizado), decisões e dashboards; SignalR só se houver necessidade.
- **Fase 10:**
  - autores do tipo agente, pela credencial;
  - relatórios de trabalho (seção 6.4);
  - tipo de autor no histórico de estados;
  - o teste de integração "agentes não aprovam o próprio trabalho".
- **Fase 11:** a CLI envia `X-Gestalyn-Client: cli`.
- **Fase 13:**
  - medir as consultas dos dashboards e da lista de atividades;
  - revisar a auditoria de autenticação, IP e User-Agent.
- **Fase 14:** retenção e exportação da auditoria.

## 15. Critérios atendidos

| Critério | Evidência |
|---|---|
| Histórico confiável | `EveryWriteRoute_RecordsExactlyOneEvent` (todas as rotas de escrita) e mutação 1; `RejectedOperations_LeaveNoEvent`; `ActivityRecords_CannotBeChangedOrDeleted_EvenDirectlyInTheDatabase` |
| Identificação do autor | `Events_IdentifyTheAuthorFromTheToken_TheDeclaredOrigin_AndTheChangedFields` (autor do token mesmo com outro ID no corpo) e execução real |
| Identificação da origem | O mesmo teste (`Web`, `Cli`, `Api` e valor desconhecido) e mutação 4 |
| Progresso calculado a partir de regras documentadas | `ProgressRule`, `ProgressRuleTests`, `Progress_FollowsTheDocumentedRule_ForTheProjectPhasesAndMilestones`, mutação 2 e [DASHBOARDS.md](../api/DASHBOARDS.md) |
| Dashboard com dados reais | `ProjectDashboard_ShowsRealNumbers`, `Statistics_CountCreatedAndCompletedTasksPerWeek_AndTheCycleTime`, `OrganizationDashboard_CountsOnlyTheProjectsTheUserSees`, mutação 3 e execução real |
| Documentos protegidos | `Documents_FollowProjectPermissions_AndArchivingMakesThemReadOnly`, `Editing_CreatesVersions_AndAnOutdatedVersionIsAConflict` e `CompositeForeignKeys_KeepVersionsAndReplacementsInsideTheirProject` |
| Auditoria consistente | `List_CombinesEventsAndManualLogs_WithFiltersAndPagination`, `OrganizationActivities_RequireThePermission_AreIsolated_AndCarryNoEmails`, `ActivityPersistenceTests` (migração, filtro de leitura e FKs) |

## 16. Critérios não atendidos

Nenhum. A definição de conclusão também foi cumprida: CI aprovado ([run 43672967965](https://github.com/harrisonleandro/Gestalyn/actions/runs/43672967965): 0 avisos, 178/178 e 150/150) e integração por PR ([PR #15](https://github.com/harrisonleandro/Gestalyn/pull/15), merge `0e9dbdd`).

## 17. Riscos identificados

| Risco | Impacto | Mitigação |
|---|---|---|
| Rota de escrita nova sem evento | Lacuna na auditoria | `ActivityCoverageTests` falha; regra no AGENTS.md |
| Crescimento das tabelas de atividade | Consultas mais lentas | Índices por organização, projeto e tarefa; paginação; medir na Fase 13 |
| Dashboards calculados na hora | Latência com muitos dados | Consultas agregadas no banco; tabelas de resumo só se as medições pedirem |
| Origem declarada falsa | Estatística de uso imprecisa | Documentada como informativa; o autor vem da credencial |
| Gatilhos impedem correções legítimas | Correção exige processo | Correções viram novos registros; remoção só por migration revisada e ADR |
| Markdown com HTML nos documentos | Risco de XSS em quem renderizar | Guardado como veio; a Web (Fase 09) renderiza com sanitização |
| Registros de histórico antigos com estado já excluído | Ficam sem categoria e fora das estatísticas | Nenhum no banco de desenvolvimento (0 de 15); comportamento documentado |

## 18. Estado do Git

- **Branch:** `feature/fase-08-auditoria`, a partir de `develop` `fe04718`, enviada e incorporada pelo [PR #15](https://github.com/harrisonleandro/Gestalyn/pull/15).
- **Commits:**
  - `826bdda` (especificação);
  - `3bd3504` (atividades, autor, origem, filtros e cobertura);
  - `8795be0` (documentos e decisões);
  - `2d20219` (progresso, dashboards e estatísticas);
  - `558c1d8` (documentação de encerramento).
- **PR #15 (Fase 08):** CI aprovado sobre `558c1d8`; merge `0e9dbdd`.
- **`develop`:** `0e9dbdd`.

## 19. Próxima fase recomendada

1. **Fase 09, Interface web completa:**
   - telas de projetos, tarefas (quadro pelo workflow e pelas transições disponíveis), atividades, documentos, decisões e dashboards;
   - Markdown renderizado com sanitização;
   - validação de alcançabilidade na configuração do workflow;
   - envio de `X-Gestalyn-Client: web` já em vigor.
