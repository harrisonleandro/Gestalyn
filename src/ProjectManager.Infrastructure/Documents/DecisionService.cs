using Microsoft.EntityFrameworkCore;
using ProjectManager.Application.Common;
using ProjectManager.Application.Documents;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Documents;
using ProjectManager.Domain.Projects;
using ProjectManager.Infrastructure.Activity;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Documents;

/// <summary>
/// Decisões do projeto (ADR-014, D6). As regras de estado ficam no domínio (<see cref="DecisionRuleException"/>,
/// 409); cada mudança de estado gera um evento.
/// </summary>
internal sealed class DecisionService(ProjectManagerDbContext dbContext, TimeProvider timeProvider, ActivityRecorder activity) : IDecisionService
{
    private const string NotFoundMessage = "Decisão não encontrada.";
    private const string ConflictMessage = "A decisão foi alterada por outra pessoa. Recarregue e tente novamente.";

    public async Task<IReadOnlyList<DecisionSummary>> ListAsync(Guid projectId, DecisionStatus? status, CancellationToken cancellationToken)
    {
        var decisions = dbContext.Decisions.AsNoTracking().Where(d => d.ProjectId == projectId);
        if (status is { } filter)
        {
            decisions = decisions.Where(d => d.Status == filter);
        }

        return await decisions
            .OrderByDescending(d => d.CreatedAt)
            .ThenByDescending(d => d.Id)
            .Select(d => new DecisionSummary(d.Id, d.Title, d.Status, d.TaskId, d.ProposedById, d.DecidedAt, d.SupersededById, d.CreatedAt, d.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<DecisionDetails>> CreateAsync(ProjectAccessGrant access, CreateDecisionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);

        // Documentos e decisões gravam autoria com FK para pessoas: agentes não chegam aqui (ADR-016).
        if (!access.IsPerson)
        {
            return Result.Failure<DecisionDetails>(ErrorKind.Forbidden, ProjectAccessGrant.PeopleOnlyMessage);
        }
        ArgumentNullException.ThrowIfNull(request);

        var project = await dbContext.Projects.SingleOrDefaultAsync(p => p.Id == access.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result.Failure<DecisionDetails>(ErrorKind.NotFound, "Projeto não encontrado.");
        }

        if (project.IsArchived)
        {
            return Result.Failure<DecisionDetails>(ErrorKind.Conflict, Project.ArchivedMessage);
        }

        var task = await FindTaskAsync(project.Id, request.TaskId, cancellationToken);
        if (request.TaskId is not null && task is null)
        {
            return Result.Failure<DecisionDetails>(ErrorKind.NotFound, "Tarefa não encontrada neste projeto.");
        }

        var decision = new Decision(project, request.Title, request.Context, request.Outcome, access.UserId);
        decision.ChangeAlternatives(request.Alternatives);
        decision.ChangeConsequences(request.Consequences);
        decision.RelateToTask(task);
        dbContext.Decisions.Add(decision);
        activity.Record(ActivityActions.DecisionProposed, ActivityEntityTypes.Decision, decision.Id, $"Decisão '{decision.Title}' proposta", project.Id, decision.TaskId);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDetails(decision));
    }

    public async Task<Result<DecisionDetails>> GetAsync(Guid decisionId, CancellationToken cancellationToken)
    {
        var decision = await dbContext.Decisions.AsNoTracking().SingleOrDefaultAsync(d => d.Id == decisionId, cancellationToken);

        return decision is null
            ? Result.Failure<DecisionDetails>(ErrorKind.NotFound, NotFoundMessage)
            : Result.Success(ToDetails(decision));
    }

    public async Task<Result<DecisionDetails>> UpdateAsync(Guid decisionId, UpdateDecisionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var found = await FindWritableAsync(decisionId, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure<DecisionDetails>(found.Error.Kind, found.Error.Message);
        }

        var decision = found.Value!;
        var task = request.HasTaskId ? await FindTaskAsync(decision.ProjectId, request.TaskId, cancellationToken) : null;
        if (request.HasTaskId && request.TaskId is not null && task is null)
        {
            return Result.Failure<DecisionDetails>(ErrorKind.NotFound, "Tarefa não encontrada neste projeto.");
        }

        var ruleError = Apply(() =>
        {
            if (request.Title is not null)
            {
                decision.Rename(request.Title);
            }

            if (request.Context is not null)
            {
                decision.ChangeContext(request.Context);
            }

            if (request.Outcome is not null)
            {
                decision.ChangeOutcome(request.Outcome);
            }

            if (request.HasAlternatives)
            {
                decision.ChangeAlternatives(request.Alternatives);
            }

            if (request.HasConsequences)
            {
                decision.ChangeConsequences(request.Consequences);
            }

            if (request.HasTaskId)
            {
                decision.RelateToTask(task);
            }
        });
        if (ruleError is not null)
        {
            return Result.Failure<DecisionDetails>(ErrorKind.Conflict, ruleError);
        }

        var changes = activity.ChangesOf(decision);
        if (!changes.IsEmpty)
        {
            activity.Record(ActivityActions.DecisionUpdated, ActivityEntityTypes.Decision, decision.Id, $"Proposta '{decision.Title}' alterada", decision.ProjectId, decision.TaskId, changes);
        }

        return await SaveAsync(decision, cancellationToken);
    }

    public Task<Result<DecisionDetails>> AcceptAsync(ProjectAccessGrant access, Guid decisionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);

        // Documentos e decisões gravam autoria com FK para pessoas: agentes não chegam aqui (ADR-016).
        if (!access.IsPerson)
        {
            return Task.FromResult(Result.Failure<DecisionDetails>(ErrorKind.Forbidden, ProjectAccessGrant.PeopleOnlyMessage));
        }
        return DecideAsync(
            decisionId,
            decision => decision.Accept(access.UserId, timeProvider.GetUtcNow()),
            ActivityActions.DecisionAccepted,
            title => $"Decisão '{title}' aceita",
            cancellationToken);
    }

    public Task<Result<DecisionDetails>> RejectAsync(ProjectAccessGrant access, Guid decisionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);

        // Documentos e decisões gravam autoria com FK para pessoas: agentes não chegam aqui (ADR-016).
        if (!access.IsPerson)
        {
            return Task.FromResult(Result.Failure<DecisionDetails>(ErrorKind.Forbidden, ProjectAccessGrant.PeopleOnlyMessage));
        }
        return DecideAsync(
            decisionId,
            decision => decision.Reject(access.UserId, timeProvider.GetUtcNow()),
            ActivityActions.DecisionRejected,
            title => $"Decisão '{title}' rejeitada",
            cancellationToken);
    }

    public async Task<Result<DecisionDetails>> SupersedeAsync(Guid decisionId, SupersedeDecisionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var found = await FindWritableAsync(decisionId, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure<DecisionDetails>(found.Error.Kind, found.Error.Message);
        }

        var decision = found.Value!;
        var replacement = await dbContext.Decisions.SingleOrDefaultAsync(d => d.Id == request.SupersededById, cancellationToken);
        if (replacement is null || replacement.ProjectId != decision.ProjectId)
        {
            return Result.Failure<DecisionDetails>(ErrorKind.NotFound, "A decisão que substitui não foi encontrada neste projeto.");
        }

        if (Apply(() => decision.SupersedeWith(replacement)) is { } ruleError)
        {
            return Result.Failure<DecisionDetails>(ErrorKind.Conflict, ruleError);
        }

        activity.Record(
            ActivityActions.DecisionSuperseded,
            ActivityEntityTypes.Decision,
            decision.Id,
            $"Decisão '{decision.Title}' substituída por '{replacement.Title}'",
            decision.ProjectId,
            decision.TaskId,
            new ActivityChanges().Set("supersededById", null, replacement.Id));

        return await SaveAsync(decision, cancellationToken);
    }

    private async Task<Result<DecisionDetails>> DecideAsync(
        Guid decisionId,
        Action<Decision> decide,
        string action,
        Func<string, string> summary,
        CancellationToken cancellationToken)
    {
        var found = await FindWritableAsync(decisionId, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure<DecisionDetails>(found.Error.Kind, found.Error.Message);
        }

        var decision = found.Value!;
        if (Apply(() => decide(decision)) is { } ruleError)
        {
            return Result.Failure<DecisionDetails>(ErrorKind.Conflict, ruleError);
        }

        activity.Record(action, ActivityEntityTypes.Decision, decision.Id, summary(decision.Title), decision.ProjectId, decision.TaskId);
        return await SaveAsync(decision, cancellationToken);
    }

    /// <summary>Decisão rastreada para alteração; projeto arquivado é somente leitura.</summary>
    private async Task<Result<Decision>> FindWritableAsync(Guid decisionId, CancellationToken cancellationToken)
    {
        var decision = await dbContext.Decisions.SingleOrDefaultAsync(d => d.Id == decisionId, cancellationToken);
        if (decision is null)
        {
            return Result.Failure<Decision>(ErrorKind.NotFound, NotFoundMessage);
        }

        return await dbContext.Projects.AnyAsync(p => p.Id == decision.ProjectId && p.ArchivedAt != null, cancellationToken)
            ? Result.Failure<Decision>(ErrorKind.Conflict, Project.ArchivedMessage)
            : Result.Success(decision);
    }

    private async Task<Domain.Tasks.ProjectTask?> FindTaskAsync(Guid projectId, Guid? taskId, CancellationToken cancellationToken) =>
        taskId is null
            ? null
            : await dbContext.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.ProjectId == projectId && t.Id == taskId, cancellationToken);

    private async Task<Result<DecisionDetails>> SaveAsync(Decision decision, CancellationToken cancellationToken) =>
        await dbContext.TrySaveChangesAsync(cancellationToken)
            ? Result.Success(ToDetails(decision))
            : Result.Failure<DecisionDetails>(ErrorKind.Conflict, ConflictMessage);

    private static string? Apply(Action edit)
    {
        try
        {
            edit();
            return null;
        }
        catch (DecisionRuleException exception)
        {
            return exception.Message;
        }
    }

    private static DecisionDetails ToDetails(Decision decision) =>
        new(
            decision.Id,
            decision.ProjectId,
            decision.Title,
            decision.Context,
            decision.Outcome,
            decision.Alternatives,
            decision.Consequences,
            decision.TaskId,
            decision.Status,
            decision.ProposedById,
            decision.DecidedById,
            decision.DecidedAt,
            decision.SupersededById,
            decision.CreatedAt,
            decision.UpdatedAt);
}
