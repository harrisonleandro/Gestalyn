using Microsoft.EntityFrameworkCore;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Activity;
using ProjectManager.Application.Common;
using ProjectManager.Application.Dashboards;
using ProjectManager.Domain.Organizations;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Workflows;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Dashboards;

/// <summary>
/// Dashboards, progresso e estatísticas (ADR-014, D7 e D8), calculados na hora por consultas agregadas sobre os dados
/// reais, sem tabelas de resumo. Progresso e listas consideram só tarefas não arquivadas; as estatísticas contam o
/// histórico, inclusive de tarefas arquivadas depois.
/// </summary>
internal sealed class DashboardService(ProjectManagerDbContext dbContext, IActivityService activities, TimeProvider timeProvider) : IDashboardService
{
    private const int ListSize = 10;
    private const int UpcomingMilestones = 5;
    private const int DueSoonDays = 7;

    public async Task<OrganizationDashboard> GetOrganizationAsync(ActiveMembership actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var userId = actor.UserId;

        // Mesma visibilidade da listagem de projetos (ADR-010): sem ver todos, só os projetos da equipe do usuário.
        var readsAll = actor.HasPermission(OrganizationPermissions.ProjectsReadAll) || actor.HasPermission(OrganizationPermissions.ProjectsManageAll);
        var visible = dbContext.Projects.AsNoTracking().Where(p => p.ArchivedAt == null);
        if (!readsAll)
        {
            visible = visible.Where(p => dbContext.ProjectMembers.Any(member => member.ProjectId == p.Id && member.UserId == userId));
        }

        var visibleIds = visible.Select(p => p.Id);
        var projects = await visible.OrderBy(p => p.Name).ThenBy(p => p.Id).Select(p => new { p.Id, p.Name }).ToListAsync(cancellationToken);

        var byCategory = await (
            from task in ActiveTasks()
            join state in dbContext.WorkflowStates on task.StateId equals state.Id
            where visibleIds.Contains(task.ProjectId)
            group task by new { task.ProjectId, state.Category } into g
            select new { g.Key.ProjectId, g.Key.Category, Count = g.Count() }).ToListAsync(cancellationToken);

        var overdueByProject = await OpenTasks()
            .Where(row => visibleIds.Contains(row.Task.ProjectId) && row.Task.DueDate < today)
            .GroupBy(row => row.Task.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(row => row.ProjectId, row => row.Count, cancellationToken);

        var weekAgo = now.AddDays(-7);
        var completedLast7Days = await FirstCompletions()
            .Where(row => visibleIds.Contains(row.ProjectId) && row.CompletedAt >= weekAgo)
            .CountAsync(cancellationToken);

        var mine = OpenTasks().Where(row => visibleIds.Contains(row.Task.ProjectId) && row.Task.Assignments.Any(a => a.UserId == userId));
        var myOpenCount = await mine.CountAsync(cancellationToken);
        var myOpen = await ToBriefs(mine.OrderBy(row => row.Task.DueDate == null).ThenBy(row => row.Task.DueDate).ThenBy(row => row.Task.Number))
            .Take(ListSize)
            .ToListAsync(cancellationToken);

        var overviews = projects
            .Select(project =>
            {
                var counts = CategoryCounts.From(byCategory.Where(row => row.ProjectId == project.Id).Select(row => (row.Category, row.Count)));
                return new ProjectOverview(
                    project.Id,
                    project.Name,
                    ProgressRule.Calculate(counts),
                    counts.Todo + counts.InProgress + counts.Blocked,
                    overdueByProject.GetValueOrDefault(project.Id));
            })
            .ToList();

        return new OrganizationDashboard(
            today,
            overviews.Count,
            overviews.Sum(project => project.OpenCount),
            overviews.Sum(project => project.OverdueCount),
            completedLast7Days,
            overviews,
            myOpenCount,
            myOpen);
    }

    public async Task<Result<ProjectDashboard>> GetProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => new { p.Name }).SingleOrDefaultAsync(cancellationToken);
        if (project is null)
        {
            return Result.Failure<ProjectDashboard>(ErrorKind.NotFound, "Projeto não encontrado.");
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var soon = today.AddDays(DueSoonDays);

        var states = await dbContext.WorkflowStates.AsNoTracking()
            .Where(state => state.ProjectId == projectId)
            .OrderBy(state => state.Position)
            .Select(state => new { state.Id, state.Name, state.Category })
            .ToListAsync(cancellationToken);
        var countsByState = await ActiveTasks()
            .Where(task => task.ProjectId == projectId)
            .GroupBy(task => task.StateId)
            .Select(g => new { StateId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(row => row.StateId, row => row.Count, cancellationToken);
        var byState = states.Select(state => new StateCount(state.Id, state.Name, state.Category, countsByState.GetValueOrDefault(state.Id))).ToList();
        var progress = ProgressRule.Calculate(CategoryCounts.From(byState.Select(state => (state.Category, state.Count))));

        var open = OpenTasks().Where(row => row.Task.ProjectId == projectId);
        var overdue = open.Where(row => row.Task.DueDate < today);
        var dueSoon = open.Where(row => row.Task.DueDate >= today && row.Task.DueDate <= soon);
        var blocked = open.Where(row => row.State.Category == StateCategory.Blocked);

        var milestones = await dbContext.Milestones.AsNoTracking()
            .Where(milestone => milestone.ProjectId == projectId && milestone.ReachedAt == null)
            .OrderBy(milestone => milestone.DueDate == null)
            .ThenBy(milestone => milestone.DueDate)
            .ThenBy(milestone => milestone.Name)
            .Take(UpcomingMilestones)
            .Select(milestone => new { milestone.Id, milestone.Name, milestone.DueDate, milestone.ReachedAt })
            .ToListAsync(cancellationToken);
        var milestoneCounts = await CountsByMilestoneAsync(projectId, cancellationToken);

        var recent = await activities.ListProjectAsync(projectId, new ActivityListQuery { PageSize = ListSize }, cancellationToken);

        return Result.Success(new ProjectDashboard(
            projectId,
            project.Name,
            today,
            progress,
            byState,
            await overdue.CountAsync(cancellationToken),
            await ToBriefs(overdue.OrderBy(row => row.Task.DueDate).ThenBy(row => row.Task.Number)).Take(ListSize).ToListAsync(cancellationToken),
            await dueSoon.CountAsync(cancellationToken),
            await ToBriefs(dueSoon.OrderBy(row => row.Task.DueDate).ThenBy(row => row.Task.Number)).Take(ListSize).ToListAsync(cancellationToken),
            await blocked.CountAsync(cancellationToken),
            await ToBriefs(blocked.OrderBy(row => row.Task.Number)).Take(ListSize).ToListAsync(cancellationToken),
            milestones
                .Select(m => new MilestoneProgress(m.Id, m.Name, m.DueDate, m.ReachedAt, ProgressRule.Calculate(milestoneCounts.GetValueOrDefault(m.Id, CategoryCounts.Empty))))
                .ToList(),
            recent.Items));
    }

    public async Task<ProjectProgress> GetProgressAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var rows = await (
            from task in ActiveTasks()
            join state in dbContext.WorkflowStates on task.StateId equals state.Id
            where task.ProjectId == projectId
            group task by new { task.PhaseId, state.Category } into g
            select new { g.Key.PhaseId, g.Key.Category, Count = g.Count() }).ToListAsync(cancellationToken);

        var phases = await dbContext.ProjectPhases.AsNoTracking()
            .Where(phase => phase.ProjectId == projectId)
            .OrderBy(phase => phase.Position)
            .Select(phase => new { phase.Id, phase.Name, phase.Position })
            .ToListAsync(cancellationToken);
        var milestones = await dbContext.Milestones.AsNoTracking()
            .Where(milestone => milestone.ProjectId == projectId)
            .OrderBy(milestone => milestone.DueDate == null)
            .ThenBy(milestone => milestone.DueDate)
            .ThenBy(milestone => milestone.Name)
            .Select(milestone => new { milestone.Id, milestone.Name, milestone.DueDate, milestone.ReachedAt })
            .ToListAsync(cancellationToken);
        var milestoneCounts = await CountsByMilestoneAsync(projectId, cancellationToken);

        return new ProjectProgress(
            projectId,
            ProgressRule.Calculate(CategoryCounts.From(rows.Select(row => (row.Category, row.Count)))),
            phases
                .Select(phase => new PhaseProgress(
                    phase.Id,
                    phase.Name,
                    phase.Position,
                    ProgressRule.Calculate(CategoryCounts.From(rows.Where(row => row.PhaseId == phase.Id).Select(row => (row.Category, row.Count))))))
                .ToList(),
            milestones
                .Select(m => new MilestoneProgress(m.Id, m.Name, m.DueDate, m.ReachedAt, ProgressRule.Calculate(milestoneCounts.GetValueOrDefault(m.Id, CategoryCounts.Empty))))
                .ToList());
    }

    public async Task<ProjectStatistics> GetStatisticsAsync(Guid projectId, StatisticsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var firstWeek = WeekStart(today).AddDays(-7 * (query.Weeks - 1));
        var since = new DateTimeOffset(firstWeek.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var created = await dbContext.Tasks.AsNoTracking()
            .Where(task => task.ProjectId == projectId && task.CreatedAt >= since)
            .Select(task => task.CreatedAt)
            .ToListAsync(cancellationToken);
        var completions = await (
            from completion in FirstCompletions()
            join task in dbContext.Tasks on completion.TaskId equals task.Id
            where completion.ProjectId == projectId && completion.CompletedAt >= since
            select new { completion.CompletedAt, task.CreatedAt }).ToListAsync(cancellationToken);

        var weekly = Enumerable.Range(0, query.Weeks)
            .Select(index => firstWeek.AddDays(7 * index))
            .Select(week => new WeeklyThroughput(
                week,
                created.Count(at => WeekStart(DateOnly.FromDateTime(at.UtcDateTime)) == week),
                completions.Count(row => WeekStart(DateOnly.FromDateTime(row.CompletedAt.UtcDateTime)) == week)))
            .ToList();

        var durations = completions
            .Select(row => Math.Max(0, (row.CompletedAt - row.CreatedAt).TotalDays))
            .Order()
            .ToList();

        return new ProjectStatistics(projectId, query.Weeks, firstWeek, today, weekly, CycleTimeOf(durations));
    }

    /// <summary>Mediana e percentil 85 pelo método do posto mais próximo, em dias, com uma casa decimal.</summary>
    internal static CycleTime CycleTimeOf(IReadOnlyList<double> sortedDays)
    {
        if (sortedDays.Count == 0)
        {
            return new CycleTime(0, null, null);
        }

        var count = sortedDays.Count;
        var median = count % 2 == 1 ? sortedDays[count / 2] : (sortedDays[(count / 2) - 1] + sortedDays[count / 2]) / 2;
        var p85 = sortedDays[(int)Math.Ceiling(0.85 * count) - 1];
        return new CycleTime(count, Round(median), Round(p85));
    }

    /// <summary>Segunda-feira da semana da data (semanas de segunda a domingo).</summary>
    internal static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static double Round(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private IQueryable<Domain.Tasks.ProjectTask> ActiveTasks() => dbContext.Tasks.AsNoTracking().Where(task => task.ArchivedAt == null);

    /// <summary>Tarefas não arquivadas fora dos estados finais, com o estado atual.</summary>
    private IQueryable<OpenTaskRow> OpenTasks() =>
        from task in ActiveTasks()
        join state in dbContext.WorkflowStates on task.StateId equals state.Id
        where state.Category != StateCategory.Done && state.Category != StateCategory.Canceled
        select new OpenTaskRow { Task = task, State = state };

    /// <summary>
    /// Primeira entrada de cada tarefa num estado concluído, pela categoria registrada no histórico na data. Uma
    /// tarefa reaberta e concluída de novo conta uma vez, pela primeira conclusão.
    /// </summary>
    private IQueryable<FirstCompletion> FirstCompletions() =>
        dbContext.TaskStatusHistory.AsNoTracking()
            .Where(entry => entry.ToCategory == StateCategory.Done)
            .GroupBy(entry => new { entry.TaskId, entry.ProjectId })
            .Select(g => new FirstCompletion { TaskId = g.Key.TaskId, ProjectId = g.Key.ProjectId, CompletedAt = g.Min(entry => entry.ChangedAt) });

    private static IQueryable<TaskBrief> ToBriefs(IQueryable<OpenTaskRow> rows) =>
        rows.Select(row => new TaskBrief(row.Task.Id, row.Task.ProjectId, row.Task.Number, row.Task.Title, row.Task.DueDate, row.State.Name, row.State.Category));

    private async Task<Dictionary<Guid, CategoryCounts>> CountsByMilestoneAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var rows = await (
            from task in ActiveTasks()
            join state in dbContext.WorkflowStates on task.StateId equals state.Id
            where task.ProjectId == projectId && task.MilestoneId != null
            group task by new { task.MilestoneId, state.Category } into g
            select new { g.Key.MilestoneId, g.Key.Category, Count = g.Count() }).ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.MilestoneId!.Value)
            .ToDictionary(group => group.Key, group => CategoryCounts.From(group.Select(row => (row.Category, row.Count))));
    }

    private sealed class OpenTaskRow
    {
        public required Domain.Tasks.ProjectTask Task { get; init; }

        public required WorkflowState State { get; init; }
    }

    private sealed class FirstCompletion
    {
        public Guid TaskId { get; init; }

        public Guid ProjectId { get; init; }

        public DateTimeOffset CompletedAt { get; init; }
    }
}
