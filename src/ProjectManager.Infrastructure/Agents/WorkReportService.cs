using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManager.Application.Abstractions.Activity;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Agents;
using ProjectManager.Application.Common;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Agents;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Workflows;
using ProjectManager.Infrastructure.Activity;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Agents;

/// <summary>
/// Relatórios de trabalho (ADR-016, D8). O envio valida o projeto, a tarefa e o estado sugerido, grava o relatório com
/// o retrato do recebimento e o evento <c>work_report.received</c> juntos, e nunca muda o estado da tarefa. A aprovação
/// humana é calculada na leitura: a primeira conclusão da tarefa por uma pessoa depois do relatório.
/// </summary>
internal sealed partial class WorkReportService(
    ProjectManagerDbContext dbContext,
    ITenantContext tenantContext,
    IActivityContext activityContext,
    ActivityRecorder activity,
    TimeProvider timeProvider,
    ILogger<WorkReportService> logger) : IWorkReportService
{
    private const string ReportNotFoundMessage = "Relatório não encontrado.";

    public async Task<Result<WorkReportReceipt>> SubmitAsync(ProjectAccessGrant access, SubmitWorkReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(request);

        var agent = tenantContext.Agent;
        if (agent is null || access.AgentId != agent.AgentId)
        {
            return Result.Failure<WorkReportReceipt>(ErrorKind.Forbidden, "Relatórios de trabalho são enviados só por agentes.");
        }

        var projectId = access.ProjectId;
        var project = await dbContext.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return Result.Failure<WorkReportReceipt>(ErrorKind.NotFound, "Projeto não encontrado.");
        }

        if (project.IsArchived)
        {
            return Result.Failure<WorkReportReceipt>(ErrorKind.Conflict, Project.ArchivedMessage);
        }

        var task = request.TaskId is { } taskId
            ? await dbContext.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.ProjectId == projectId && t.Id == taskId, cancellationToken)
            : null;
        if (request.TaskId is not null && task is null)
        {
            return Result.Failure<WorkReportReceipt>(ErrorKind.Validation, "A tarefa informada não é deste projeto.");
        }

        var currentState = task is null
            ? null
            : await dbContext.WorkflowStates.AsNoTracking().SingleAsync(s => s.Id == task.StateId, cancellationToken);

        var suggested = request.SuggestedStatus is { } suggestedId
            ? await dbContext.WorkflowStates.AsNoTracking().SingleOrDefaultAsync(s => s.ProjectId == projectId && s.Id == suggestedId, cancellationToken)
            : null;
        if (request.SuggestedStatus is not null && suggested is null)
        {
            return Result.Failure<WorkReportReceipt>(ErrorKind.Validation, "O estado sugerido não é do workflow deste projeto.");
        }

        var direct = currentState is null || suggested is null
            ? null
            : await dbContext.WorkflowTransitions.AsNoTracking()
                .Where(t => t.ProjectId == projectId && t.FromStateId == currentState.Id && t.ToStateId == suggested.Id)
                .Select(t => (bool?)t.RequiresApproval)
                .SingleOrDefaultAsync(cancellationToken);

        var report = new WorkReport(
            project.OrganizationId,
            projectId,
            task?.Id,
            agent.AgentId,
            agent.CredentialId,
            request.ToDeclaration(),
            currentState is null ? null : new WorkReportStateSnapshot(currentState.Id, currentState.Name),
            suggested is null ? null : new WorkReportStateSnapshot(suggested.Id, suggested.Name),
            activityContext.Actor.RequestId,
            timeProvider.GetUtcNow());
        dbContext.WorkReports.Add(report);

        var received = activity.Record(
            ActivityActions.WorkReportReceived,
            ActivityEntityTypes.WorkReport,
            report.Id,
            task is null ? "Relatório de trabalho recebido" : $"Relatório de trabalho recebido na tarefa #{task.Number}",
            projectId,
            task?.Id);

        await dbContext.SaveChangesAsync(cancellationToken);
        LogReportReceived(logger, report.Id, projectId, agent.AgentId);

        return Result.Success(new WorkReportReceipt(
            report.Id,
            Accepted: true,
            received.Id,
            currentState is null ? null : new WorkReportTaskStatus(currentState.Id, currentState.Name, currentState.Category),
            direct,
            direct is null ? WorkReportLevels.NoTransition : WorkReportLevels.DirectTransition,
            Evidence(report, approval: null)));
    }

    public async Task<PagedResult<WorkReportSummary>> ListByProjectAsync(ProjectAccessGrant access, WorkReportListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);

        var projectId = access.ProjectId;
        return await ListAsync(dbContext.WorkReports.Where(r => r.ProjectId == projectId), query, cancellationToken);
    }

    public async Task<Result<PagedResult<WorkReportSummary>>> ListByTaskAsync(
        ProjectAccessGrant access,
        Guid taskId,
        WorkReportListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);

        var projectId = access.ProjectId;
        if (!await dbContext.Tasks.AnyAsync(t => t.ProjectId == projectId && t.Id == taskId, cancellationToken))
        {
            return Result.Failure<PagedResult<WorkReportSummary>>(ErrorKind.NotFound, "Tarefa não encontrada.");
        }

        return Result.Success(await ListAsync(dbContext.WorkReports.Where(r => r.ProjectId == projectId && r.TaskId == taskId), query, cancellationToken));
    }

    public async Task<Result<WorkReportDetails>> GetAsync(ProjectAccessGrant access, Guid reportId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);

        var projectId = access.ProjectId;
        var report = await dbContext.WorkReports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reportId && r.ProjectId == projectId, cancellationToken);
        if (report is null)
        {
            return Result.Failure<WorkReportDetails>(ErrorKind.NotFound, ReportNotFoundMessage);
        }

        var approvals = await ApprovalsAsync([report], cancellationToken);
        var agents = await AgentNamesAsync([report.AgentId], cancellationToken);
        approvals.TryGetValue(report.Id, out var approval);

        return Result.Success(new WorkReportDetails(
            report.Id,
            report.ProjectId,
            report.TaskId,
            AgentOf(report, agents),
            report.Summary,
            report.Changes,
            report.Files,
            report.TestsCommand is null && report.TestsPassed is null && report.TestsFailed is null && report.TestsSkipped is null
                ? null
                : new WorkReportTests(report.TestsCommand, report.TestsPassed, report.TestsFailed, report.TestsSkipped),
            report.CommitSha is null ? null : new WorkReportCommit(report.CommitSha, report.CommitRepository),
            Snapshot(report.TaskStateId, report.TaskStateName),
            Snapshot(report.SuggestedStateId, report.SuggestedStateName),
            report.RequestId,
            report.ReceivedAt,
            Evidence(report, approval),
            approval));
    }

    /// <summary>Os cinco níveis. Sem tarefa, a aprovação humana não se aplica; com tarefa, vem do histórico.</summary>
    private static WorkReportEvidence Evidence(WorkReport report, WorkReportApproval? approval) =>
        new(
            Declared: true,
            Accepted: true,
            WorkReportLevels.NotVerified,
            WorkReportLevels.NotVerified,
            report.TaskId is null ? WorkReportLevels.NotApplicable
                : approval is null ? WorkReportLevels.Pending
                : WorkReportLevels.Approved);

    private static WorkReportStateSnapshot? Snapshot(Guid? stateId, string? name) =>
        stateId is { } id ? new WorkReportStateSnapshot(id, name!) : null;

    private static ActorReference AgentOf(WorkReport report, IReadOnlyDictionary<Guid, string> agents) =>
        new(report.AgentId, ActorKind.Agent, agents.GetValueOrDefault(report.AgentId));

    private async Task<PagedResult<WorkReportSummary>> ListAsync(IQueryable<WorkReport> reports, WorkReportListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var total = await reports.CountAsync(cancellationToken);
        var page = await reports.AsNoTracking()
            .OrderByDescending(r => r.ReceivedAt)
            .ThenByDescending(r => r.Id)
            .Skip(query.CalculateOffset())
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var approvals = await ApprovalsAsync(page, cancellationToken);
        var agents = await AgentNamesAsync([.. page.Select(r => r.AgentId)], cancellationToken);
        var items = page
            .Select(report => new WorkReportSummary(
                report.Id,
                report.ProjectId,
                report.TaskId,
                AgentOf(report, agents),
                report.Summary,
                Snapshot(report.SuggestedStateId, report.SuggestedStateName),
                report.ReceivedAt,
                Evidence(report, approvals.GetValueOrDefault(report.Id))))
            .ToList();

        return new PagedResult<WorkReportSummary>(items, query.Page, query.PageSize, total);
    }

    /// <summary>
    /// Aprovação humana (nível 5): a primeira entrada do histórico da tarefa posterior ao relatório, feita por uma
    /// pessoa e com destino na categoria Concluída. Agentes nunca entram em Concluída (D5).
    /// </summary>
    private async Task<Dictionary<Guid, WorkReportApproval>> ApprovalsAsync(IReadOnlyCollection<WorkReport> reports, CancellationToken cancellationToken)
    {
        var withTask = reports.Where(r => r.TaskId is not null).ToList();
        if (withTask.Count == 0)
        {
            return [];
        }

        var taskIds = withTask.Select(r => r.TaskId!.Value).Distinct().ToList();
        var earliest = withTask.Min(r => r.ReceivedAt);
        var completions = await dbContext.TaskStatusHistory.AsNoTracking()
            .Where(h => taskIds.Contains(h.TaskId)
                && h.ChangedAt > earliest
                && h.ActorKind == ActorKind.User
                && h.ToCategory == StateCategory.Done)
            .OrderBy(h => h.ChangedAt)
            .Select(h => new { h.TaskId, h.ChangedById, h.ChangedAt })
            .ToListAsync(cancellationToken);

        var userIds = completions.Select(c => c.ChangedById).Distinct().ToList();
        var names = await dbContext.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        var result = new Dictionary<Guid, WorkReportApproval>();
        foreach (var report in withTask)
        {
            var first = completions.FirstOrDefault(c => c.TaskId == report.TaskId && c.ChangedAt > report.ReceivedAt);
            if (first is not null)
            {
                result[report.Id] = new WorkReportApproval(first.ChangedById, names.GetValueOrDefault(first.ChangedById), first.ChangedAt);
            }
        }

        return result;
    }

    private async Task<Dictionary<Guid, string>> AgentNamesAsync(IReadOnlyCollection<Guid> agentIds, CancellationToken cancellationToken) =>
        await dbContext.Agents.AsNoTracking()
            .Where(a => agentIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

    [LoggerMessage(Level = LogLevel.Information, Message = "Relatório de trabalho {ReportId} recebido no projeto {ProjectId} do agente {AgentId}.")]
    private static partial void LogReportReceived(ILogger logger, Guid reportId, Guid projectId, Guid agentId);
}
