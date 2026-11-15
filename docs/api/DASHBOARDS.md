# API: progresso, dashboards e estatísticas

Contratos da Fase 08 ([ADR-014](../decisions/ADR-014-AUDITORIA-DOCUMENTOS-E-DASHBOARDS.md)). Todas as rotas exigem `Authorization: Bearer`. As rotas por projeto exigem também o cabeçalho **`X-Gestalyn-Organization`**. Os números são calculados na hora sobre os dados reais, e as datas são em UTC.

## Regra de progresso

**Progresso = tarefas concluídas ÷ (tarefas − tarefas canceladas)**, contando só as tarefas não arquivadas (regra `doneOverNotCanceled`).

- **Concluída:** tarefa num estado da categoria `Done`.
- **Canceladas** (categoria `Canceled`): saem da conta, porque não serão feitas.
- **Não concluídas:** `Todo`, `InProgress` e `Blocked`.
- **Sem tarefas na conta**, o progresso é `null` ("sem dados"), nunca 0% nem 100%.
- **Sem pesos:** não há estimativas no modelo, e usar a prioridade como peso seria arbitrário.

Todo progresso vem como `{ counts: { todo, inProgress, blocked, done, canceled, total }, percent, rule }`, com o percentual em uma casa decimal.

**Exemplo:** 2 concluídas, 1 cancelada, 1 bloqueada, 1 em andamento e 1 a fazer resultam em 2 ÷ (6 − 1) = **40,0%**.

## Rotas

| Método e rota | Permissão | Conteúdo |
|---|---|---|
| `GET /organizations/{organizationId}/dashboard` | `organization.read` | Visão geral, só com os projetos ativos que o usuário enxerga |
| `GET /projects/{projectId}/dashboard` | `project.read` | Painel do projeto |
| `GET /projects/{projectId}/progress` | `project.read` | Progresso do projeto, de cada fase e de cada marco |
| `GET /projects/{projectId}/statistics?weeks=8` | `project.read` | Ritmo semanal e tempo de ciclo, de 1 a 26 semanas (400 fora disso) |

### Organização

`{ today, projectCount, openCount, overdueCount, completedLast7Days, projects, myOpenCount, myOpenTasks }`.

- **Visibilidade:** a mesma da listagem de projetos. Quem não tem `projects.read_all` vê só os projetos de cuja equipe faz parte.
- **`projects`:** cada um com o progresso, as tarefas abertas e as atrasadas.
- **`completedLast7Days`:** tarefas que entraram pela primeira vez num estado concluído nos últimos 7 dias.
- **`myOpenTasks`:** as 10 primeiras tarefas abertas em que o usuário é responsável, por prazo.

### Projeto

`{ projectId, name, today, progress, byState, overdueCount, overdue, dueSoonCount, dueSoon, blockedCount, blocked, upcomingMilestones, recentActivity }`.

- **`byState`:** todos os estados do workflow, na ordem, com a quantidade de tarefas, inclusive zero.
- **`overdue`:** tarefas abertas com prazo anterior a hoje.
- **`dueSoon`:** tarefas abertas com prazo de hoje até 7 dias.
- **`blocked`:** tarefas num estado da categoria `Blocked`.
- **Listas:** cada uma traz até 10 tarefas, com a contagem total ao lado.
- **`upcomingMilestones`:** os 5 próximos marcos não atingidos, com o progresso de cada um.
- **`recentActivity`:** as 10 atividades mais recentes ([ACTIVITY.md](ACTIVITY.md)).

### Progresso

`{ projectId, project, phases: [{ phaseId, name, position, progress }], milestones: [{ milestoneId, name, dueDate, reachedAt, progress }] }`.

Uma fase ou marco sem tarefas tem `percent: null`.

### Estatísticas

`{ projectId, weeks, from, to, weekly: [{ weekStart, created, completed }], cycleTime: { sampleSize, medianDays, percentile85Days } }`.

- **Semanas:** de segunda a domingo, em UTC. A última é a semana atual.
- **Conclusão:** a primeira entrada da tarefa num estado da categoria `Done`, pela categoria registrada no histórico na data. Uma tarefa reaberta e concluída de novo conta uma vez.
- **Tempo de ciclo:** dias entre a criação e a conclusão, para as tarefas concluídas no período. Mediana e percentil 85 pelo posto mais próximo, com uma casa decimal; `null` sem conclusões.
- **Tarefas arquivadas:** as estatísticas as incluem, porque são histórico. O progresso não as inclui.
