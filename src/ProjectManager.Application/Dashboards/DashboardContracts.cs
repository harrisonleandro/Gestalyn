using System.ComponentModel.DataAnnotations;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Activity;
using ProjectManager.Application.Common;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Workflows;

namespace ProjectManager.Application.Dashboards;

/// <summary>Tarefa resumida nas listas dos dashboards.</summary>
public sealed record TaskBrief(Guid Id, Guid ProjectId, int Number, string Title, DateOnly? DueDate, string StateName, StateCategory Category);

public sealed record PhaseProgress(Guid PhaseId, string Name, int Position, Progress Progress);

public sealed record MilestoneProgress(Guid MilestoneId, string Name, DateOnly? DueDate, DateTimeOffset? ReachedAt, Progress Progress);

/// <summary>Progresso do projeto, de cada fase e de cada marco, pela mesma regra (ADR-014, D7).</summary>
public sealed record ProjectProgress(Guid ProjectId, Progress Project, IReadOnlyList<PhaseProgress> Phases, IReadOnlyList<MilestoneProgress> Milestones);

public sealed record StateCount(Guid StateId, string Name, StateCategory Category, int Count);

/// <summary>Dashboard do projeto, calculado na hora sobre os dados reais. As datas são em UTC.</summary>
public sealed record ProjectDashboard(
    Guid ProjectId,
    string Name,
    DateOnly Today,
    Progress Progress,
    IReadOnlyList<StateCount> ByState,
    int OverdueCount,
    IReadOnlyList<TaskBrief> Overdue,
    int DueSoonCount,
    IReadOnlyList<TaskBrief> DueSoon,
    int BlockedCount,
    IReadOnlyList<TaskBrief> Blocked,
    IReadOnlyList<MilestoneProgress> UpcomingMilestones,
    IReadOnlyList<ActivityItem> RecentActivity);

public sealed record WeeklyThroughput(DateOnly WeekStart, int Created, int Completed);

/// <summary>Dias entre a criação da tarefa e a primeira entrada num estado concluído.</summary>
public sealed record CycleTime(int SampleSize, double? MedianDays, double? Percentile85Days);

/// <summary>Estatísticas do projeto nas últimas semanas (UTC, de segunda a domingo), incluindo tarefas arquivadas.</summary>
public sealed record ProjectStatistics(Guid ProjectId, int Weeks, DateOnly From, DateOnly To, IReadOnlyList<WeeklyThroughput> Weekly, CycleTime CycleTime);

public sealed class StatisticsQuery
{
    public const int MaxWeeks = 26;

    [Range(1, MaxWeeks, ErrorMessage = "Informe de {1} a {2} semanas.")]
    public int Weeks { get; init; } = 8;
}

public sealed record ProjectOverview(Guid ProjectId, string Name, Progress Progress, int OpenCount, int OverdueCount);

/// <summary>Visão geral da organização, só com os projetos ativos que o usuário enxerga.</summary>
public sealed record OrganizationDashboard(
    DateOnly Today,
    int ProjectCount,
    int OpenCount,
    int OverdueCount,
    int CompletedLast7Days,
    IReadOnlyList<ProjectOverview> Projects,
    int MyOpenCount,
    IReadOnlyList<TaskBrief> MyOpenTasks);

/// <summary>Dashboards, progresso e estatísticas (ADR-014, D7 e D8). O acesso já foi verificado.</summary>
public interface IDashboardService
{
    Task<OrganizationDashboard> GetOrganizationAsync(ActiveMembership actor, CancellationToken cancellationToken);

    Task<Result<ProjectDashboard>> GetProjectAsync(Guid projectId, CancellationToken cancellationToken);

    Task<ProjectProgress> GetProgressAsync(Guid projectId, CancellationToken cancellationToken);

    Task<ProjectStatistics> GetStatisticsAsync(Guid projectId, StatisticsQuery query, CancellationToken cancellationToken);
}
