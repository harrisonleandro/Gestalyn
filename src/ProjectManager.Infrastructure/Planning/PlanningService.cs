using Microsoft.EntityFrameworkCore;
using ProjectManager.Application.Common;
using ProjectManager.Application.Planning;
using ProjectManager.Domain.Projects;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Planning;

/// <summary>
/// Fases e marcos. Tudo passa pelo filtro da organização ativa; o projeto arquivado é somente leitura.
/// </summary>
internal sealed class PlanningService(ProjectManagerDbContext dbContext, TimeProvider timeProvider) : IPlanningService
{
    private const string ProjectNotFoundMessage = "Projeto não encontrado.";
    private const string PhaseNotFoundMessage = "Fase não encontrada.";
    private const string MilestoneNotFoundMessage = "Marco não encontrado.";
    private const string ConflictMessage = "O planejamento foi alterado por outra pessoa. Recarregue e tente novamente.";

    public async Task<IReadOnlyList<PhaseDetails>> ListPhasesAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var phases = await dbContext.ProjectPhases
            .AsNoTracking()
            .Where(phase => phase.ProjectId == projectId)
            .OrderBy(phase => phase.Position)
            .ToListAsync(cancellationToken);

        return phases.Select(ToDetails).ToList();
    }

    public async Task<Result<PhaseDetails>> CreatePhaseAsync(Guid projectId, CreatePhaseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var project = await FindWritableProjectAsync(projectId, cancellationToken, includePhases: true);
        if (project.Error is not null)
        {
            return Result.Failure<PhaseDetails>(project.Error.Kind, project.Error.Message);
        }

        if (!ProjectPhase.IsValidSchedule(request.StartDate, request.EndDate))
        {
            return Result.Failure<PhaseDetails>(ErrorKind.Validation, ProjectPhase.ScheduleMessage);
        }

        if (project.Value!.Phases.Count >= ProjectPhase.MaxPerProject)
        {
            return Result.Failure<PhaseDetails>(ErrorKind.Validation, $"Um projeto pode ter no máximo {ProjectPhase.MaxPerProject} fases.");
        }

        var phase = project.Value.AddPhase(request.Name);
        phase.ChangeDescription(request.Description);
        phase.Schedule(request.StartDate, request.EndDate);

        return await dbContext.TrySaveChangesAsync(cancellationToken)
            ? Result.Success(ToDetails(phase))
            : Result.Failure<PhaseDetails>(ErrorKind.Conflict, ConflictMessage);
    }

    public async Task<Result<PhaseDetails>> UpdatePhaseAsync(Guid projectId, Guid phaseId, UpdatePhaseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var project = await FindWritableProjectAsync(projectId, cancellationToken);
        if (project.Error is not null)
        {
            return Result.Failure<PhaseDetails>(project.Error.Kind, project.Error.Message);
        }

        var phase = await dbContext.ProjectPhases.SingleOrDefaultAsync(p => p.ProjectId == projectId && p.Id == phaseId, cancellationToken);
        if (phase is null)
        {
            return Result.Failure<PhaseDetails>(ErrorKind.NotFound, PhaseNotFoundMessage);
        }

        var startDate = request.HasStartDate ? request.StartDate : phase.StartDate;
        var endDate = request.HasEndDate ? request.EndDate : phase.EndDate;
        if (!ProjectPhase.IsValidSchedule(startDate, endDate))
        {
            return Result.Failure<PhaseDetails>(ErrorKind.Validation, ProjectPhase.ScheduleMessage);
        }

        if (request.Name is not null)
        {
            phase.Rename(request.Name);
        }

        if (request.HasDescription)
        {
            phase.ChangeDescription(request.Description);
        }

        phase.Schedule(startDate, endDate);

        return await dbContext.TrySaveChangesAsync(cancellationToken)
            ? Result.Success(ToDetails(phase))
            : Result.Failure<PhaseDetails>(ErrorKind.Conflict, ConflictMessage);
    }

    /// <summary>
    /// Reordena pela lista completa de IDs. Em transação, as posições são primeiro deslocadas para além
    /// da maior posição e depois gravadas na ordem final: o índice único <c>(project_id, position)</c> e a
    /// regra <c>position &gt; 0</c> valem a cada comando. O projeto é marcado como alterado para que duas
    /// reordenações simultâneas conflitem (<c>xmin</c>).
    /// </summary>
    public async Task<Result<IReadOnlyList<PhaseDetails>>> ReorderPhasesAsync(
        Guid projectId,
        ReorderPhasesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var project = await FindWritableProjectAsync(projectId, cancellationToken);
        if (project.Error is not null)
        {
            return Result.Failure<IReadOnlyList<PhaseDetails>>(project.Error.Kind, project.Error.Message);
        }

        var current = await dbContext.ProjectPhases
            .AsNoTracking()
            .Where(phase => phase.ProjectId == projectId)
            .Select(phase => new { phase.Id, phase.Position })
            .ToListAsync(cancellationToken);
        var requested = request.PhaseIds;
        if (requested.Count != current.Count || requested.Distinct().Count() != requested.Count || current.Any(phase => !requested.Contains(phase.Id)))
        {
            return Result.Failure<IReadOnlyList<PhaseDetails>>(ErrorKind.Validation, "Informe todas as fases do projeto, cada uma uma única vez.");
        }

        if (current.Count == 0)
        {
            return Result.Success<IReadOnlyList<PhaseDetails>>([]);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.Entry(project.Value!).State = EntityState.Modified;
        if (!await dbContext.TrySaveChangesAsync(cancellationToken))
        {
            return Result.Failure<IReadOnlyList<PhaseDetails>>(ErrorKind.Conflict, ConflictMessage);
        }

        var now = timeProvider.GetUtcNow();
        var offset = current.Max(phase => phase.Position);
        await dbContext.ProjectPhases
            .Where(phase => phase.ProjectId == projectId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(phase => phase.Position, phase => phase.Position + offset), cancellationToken);

        for (var index = 0; index < requested.Count; index++)
        {
            var phaseId = requested[index];
            var position = index + 1;
            await dbContext.ProjectPhases
                .Where(phase => phase.ProjectId == projectId && phase.Id == phaseId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(phase => phase.Position, position).SetProperty(phase => phase.UpdatedAt, now),
                    cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Success(await ListPhasesAsync(projectId, cancellationToken));
    }

    public async Task<Result> DeletePhaseAsync(Guid projectId, Guid phaseId, CancellationToken cancellationToken)
    {
        var project = await FindWritableProjectAsync(projectId, cancellationToken);
        if (project.Error is not null)
        {
            return Result.Failure(project.Error.Kind, project.Error.Message);
        }

        var phase = await dbContext.ProjectPhases.SingleOrDefaultAsync(p => p.ProjectId == projectId && p.Id == phaseId, cancellationToken);
        if (phase is null)
        {
            return Result.Failure(ErrorKind.NotFound, PhaseNotFoundMessage);
        }

        if (await dbContext.Tasks.AnyAsync(task => task.ProjectId == projectId && task.PhaseId == phaseId, cancellationToken))
        {
            return Result.Failure(ErrorKind.Conflict, "A fase tem tarefas e não pode ser excluída. Mova as tarefas para outra fase antes.");
        }

        // Marcos ligados à fase continuam existindo, sem fase.
        var milestones = await dbContext.Milestones.Where(m => m.ProjectId == projectId && m.PhaseId == phaseId).ToListAsync(cancellationToken);
        milestones.ForEach(milestone => milestone.LinkToPhase(null));
        dbContext.ProjectPhases.Remove(phase);

        return await dbContext.TrySaveChangesAsync(cancellationToken)
            ? Result.Success()
            : Result.Failure(ErrorKind.Conflict, ConflictMessage);
    }

    public async Task<IReadOnlyList<MilestoneDetails>> ListMilestonesAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var milestones = await dbContext.Milestones
            .AsNoTracking()
            .Where(milestone => milestone.ProjectId == projectId)
            .OrderBy(milestone => milestone.DueDate == null)
            .ThenBy(milestone => milestone.DueDate)
            .ThenBy(milestone => milestone.Name)
            .ToListAsync(cancellationToken);

        return milestones.Select(ToDetails).ToList();
    }

    public async Task<Result<MilestoneDetails>> CreateMilestoneAsync(Guid projectId, CreateMilestoneRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var project = await FindWritableProjectAsync(projectId, cancellationToken);
        if (project.Error is not null)
        {
            return Result.Failure<MilestoneDetails>(project.Error.Kind, project.Error.Message);
        }

        if (await dbContext.Milestones.CountAsync(m => m.ProjectId == projectId, cancellationToken) >= Milestone.MaxPerProject)
        {
            return Result.Failure<MilestoneDetails>(ErrorKind.Validation, $"Um projeto pode ter no máximo {Milestone.MaxPerProject} marcos.");
        }

        var phase = await FindPhaseAsync(projectId, request.PhaseId, cancellationToken);
        if (request.PhaseId is not null && phase is null)
        {
            return Result.Failure<MilestoneDetails>(ErrorKind.NotFound, PhaseNotFoundMessage);
        }

        var milestone = new Milestone(project.Value!, request.Name);
        milestone.ChangeDescription(request.Description);
        milestone.ChangeDueDate(request.DueDate);
        milestone.LinkToPhase(phase);
        dbContext.Milestones.Add(milestone);

        return await dbContext.TrySaveChangesAsync(cancellationToken)
            ? Result.Success(ToDetails(milestone))
            : Result.Failure<MilestoneDetails>(ErrorKind.Conflict, ConflictMessage);
    }

    public async Task<Result<MilestoneDetails>> UpdateMilestoneAsync(
        Guid projectId,
        Guid milestoneId,
        UpdateMilestoneRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var project = await FindWritableProjectAsync(projectId, cancellationToken);
        if (project.Error is not null)
        {
            return Result.Failure<MilestoneDetails>(project.Error.Kind, project.Error.Message);
        }

        var milestone = await dbContext.Milestones.SingleOrDefaultAsync(m => m.ProjectId == projectId && m.Id == milestoneId, cancellationToken);
        if (milestone is null)
        {
            return Result.Failure<MilestoneDetails>(ErrorKind.NotFound, MilestoneNotFoundMessage);
        }

        if (request.HasPhaseId)
        {
            var phase = await FindPhaseAsync(projectId, request.PhaseId, cancellationToken);
            if (request.PhaseId is not null && phase is null)
            {
                return Result.Failure<MilestoneDetails>(ErrorKind.NotFound, PhaseNotFoundMessage);
            }

            milestone.LinkToPhase(phase);
        }

        if (request.Name is not null)
        {
            milestone.Rename(request.Name);
        }

        if (request.HasDescription)
        {
            milestone.ChangeDescription(request.Description);
        }

        if (request.HasDueDate)
        {
            milestone.ChangeDueDate(request.DueDate);
        }

        if (request.IsReached == true)
        {
            milestone.MarkAsReached(timeProvider.GetUtcNow());
        }
        else if (request.IsReached == false)
        {
            milestone.MarkAsNotReached();
        }

        return await dbContext.TrySaveChangesAsync(cancellationToken)
            ? Result.Success(ToDetails(milestone))
            : Result.Failure<MilestoneDetails>(ErrorKind.Conflict, ConflictMessage);
    }

    public async Task<Result> DeleteMilestoneAsync(Guid projectId, Guid milestoneId, CancellationToken cancellationToken)
    {
        var project = await FindWritableProjectAsync(projectId, cancellationToken);
        if (project.Error is not null)
        {
            return Result.Failure(project.Error.Kind, project.Error.Message);
        }

        var milestone = await dbContext.Milestones.SingleOrDefaultAsync(m => m.ProjectId == projectId && m.Id == milestoneId, cancellationToken);
        if (milestone is null)
        {
            return Result.Failure(ErrorKind.NotFound, MilestoneNotFoundMessage);
        }

        if (await dbContext.Tasks.AnyAsync(task => task.ProjectId == projectId && task.MilestoneId == milestoneId, cancellationToken))
        {
            return Result.Failure(ErrorKind.Conflict, "O marco tem tarefas e não pode ser excluído. Desvincule as tarefas antes.");
        }

        dbContext.Milestones.Remove(milestone);

        return await dbContext.TrySaveChangesAsync(cancellationToken)
            ? Result.Success()
            : Result.Failure(ErrorKind.Conflict, ConflictMessage);
    }

    private async Task<Result<Project>> FindWritableProjectAsync(Guid projectId, CancellationToken cancellationToken, bool includePhases = false)
    {
        var query = dbContext.Projects.AsQueryable();
        if (includePhases)
        {
            query = query.Include(project => project.Phases);
        }

        var project = await query.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        return project switch
        {
            null => Result.Failure<Project>(ErrorKind.NotFound, ProjectNotFoundMessage),
            { IsArchived: true } => Result.Failure<Project>(ErrorKind.Conflict, Project.ArchivedMessage),
            _ => Result.Success(project),
        };
    }

    private async Task<ProjectPhase?> FindPhaseAsync(Guid projectId, Guid? phaseId, CancellationToken cancellationToken) =>
        phaseId is null
            ? null
            : await dbContext.ProjectPhases.AsNoTracking().SingleOrDefaultAsync(p => p.ProjectId == projectId && p.Id == phaseId, cancellationToken);

    private static PhaseDetails ToDetails(ProjectPhase phase) =>
        new(phase.Id, phase.Name, phase.Description, phase.Position, phase.StartDate, phase.EndDate, phase.CreatedAt, phase.UpdatedAt);

    private static MilestoneDetails ToDetails(Milestone milestone) =>
        new(milestone.Id, milestone.Name, milestone.Description, milestone.DueDate, milestone.PhaseId, milestone.ReachedAt, milestone.CreatedAt, milestone.UpdatedAt);
}
