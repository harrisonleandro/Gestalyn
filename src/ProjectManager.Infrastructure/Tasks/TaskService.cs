using System.Data;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Common;
using ProjectManager.Application.Projects;
using ProjectManager.Application.Tasks;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Tasks;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Tasks;

/// <summary>
/// Casos de uso de tarefas. Tudo passa pelo filtro da organização ativa; tarefa arquivada e projeto
/// arquivado são somente leitura.
/// </summary>
internal sealed partial class TaskService(
    ProjectManagerDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<TaskService> logger) : ITaskService
{
    private const int NumberAllocationAttempts = 3;

    private const string TaskNotFoundMessage = "Tarefa não encontrada.";
    private const string ProjectNotFoundMessage = "Projeto não encontrado.";
    private const string NotOnTeamMessage = "A pessoa não está na equipe do projeto.";
    private const string ConflictMessage = "A tarefa foi alterada por outra pessoa. Recarregue e tente novamente.";

    public async Task<Result<TaskDetails>> CreateAsync(
        ActiveMembership actor,
        ProjectAccessGrant access,
        CreateTaskRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Labels is not null && Labels.Normalize(request.Labels, out var labelError) is null)
        {
            return Result.Failure<TaskDetails>(ErrorKind.Validation, labelError!);
        }

        var assigneeIds = (request.AssigneeIds ?? []).Distinct().ToList();
        var projectId = access.ProjectId;

        // O número vem do contador do projeto, protegido pelo xmin: em criações simultâneas, uma delas
        // conflita e tenta de novo com o contador atualizado.
        for (var attempt = 1; attempt <= NumberAllocationAttempts; attempt++)
        {
            var project = await dbContext.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
            if (project is null)
            {
                return Result.Failure<TaskDetails>(ErrorKind.NotFound, ProjectNotFoundMessage);
            }

            if (project.IsArchived)
            {
                return Result.Failure<TaskDetails>(ErrorKind.Conflict, Project.ArchivedMessage);
            }

            var links = await FindLinksAsync(projectId, request.PhaseId, request.MilestoneId, cancellationToken);
            if (links.Error is not null)
            {
                return Result.Failure<TaskDetails>(links.Error.Kind, links.Error.Message);
            }

            if (!await AreAllOnTeamAsync(projectId, assigneeIds, cancellationToken))
            {
                return Result.Failure<TaskDetails>(ErrorKind.NotFound, NotOnTeamMessage);
            }

            var now = timeProvider.GetUtcNow();
            var task = new ProjectTask(project, project.AllocateTaskNumber(), request.Title, actor.UserId);
            task.ChangeDescription(request.Description);
            task.ChangePriority(request.Priority ?? Priority.Medium);
            task.ChangeDueDate(request.DueDate);
            task.MoveToPhase(links.Value.Phase);
            task.LinkToMilestone(links.Value.Milestone);
            task.SetLabels(request.Labels ?? []);
            assigneeIds.ForEach(userId => task.Assign(userId, now));
            dbContext.Tasks.Add(task);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < NumberAllocationAttempts)
            {
                dbContext.ChangeTracker.Clear();
                continue;
            }
            catch (DbUpdateException exception) when (SaveChangesConflicts.IsConflict(exception))
            {
                return Result.Failure<TaskDetails>(ErrorKind.Conflict, ConflictMessage);
            }

            LogTaskCreated(logger, task.Id, task.Number, projectId, actor.UserId);
            return await GetAsync(access, task.Id, cancellationToken);
        }

        return Result.Failure<TaskDetails>(ErrorKind.Conflict, ConflictMessage);
    }

    public async Task<PagedResult<TaskSummary>> ListAsync(ActiveMembership actor, Guid projectId, TaskListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(query);

        var tasks = dbContext.Tasks.AsNoTracking().Where(task => task.ProjectId == projectId);

        if (!query.IncludeArchived)
        {
            tasks = tasks.Where(task => task.ArchivedAt == null);
        }

        if (query.PhaseId is { } phaseId)
        {
            tasks = tasks.Where(task => task.PhaseId == phaseId);
        }

        if (query.MilestoneId is { } milestoneId)
        {
            tasks = tasks.Where(task => task.MilestoneId == milestoneId);
        }

        if (query.AssigneeId is { } assigneeId)
        {
            tasks = tasks.Where(task => task.Assignments.Any(assignment => assignment.UserId == assigneeId));
        }

        if (query.AssignedToMe)
        {
            var userId = actor.UserId;
            tasks = tasks.Where(task => task.Assignments.Any(assignment => assignment.UserId == userId));
        }

        if (!string.IsNullOrWhiteSpace(query.Label))
        {
            var label = Labels.NormalizeName(query.Label);
            tasks = tasks.Where(task => task.Labels.Any(l => l.NormalizedName == label));
        }

        if (query.Priority is { } priority)
        {
            tasks = tasks.Where(task => task.Priority == priority);
        }

        if (query.DueBefore is { } dueBefore)
        {
            tasks = tasks.Where(task => task.DueDate < dueBefore);
        }

        if (query.DueAfter is { } dueAfter)
        {
            tasks = tasks.Where(task => task.DueDate > dueAfter);
        }

        if (query.Overdue)
        {
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            tasks = tasks.Where(task => task.DueDate < today);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{EscapeLikePattern(query.Search.Trim())}%";
            tasks = tasks.Where(task => EF.Functions.ILike(task.Title, pattern, "\\"));
        }

        var totalCount = await tasks.CountAsync(cancellationToken);
        var rows = await Sort(tasks, query.SortBy, query.SortDirection)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(task => new
            {
                task.Id,
                task.Number,
                task.Title,
                task.Priority,
                task.DueDate,
                task.PhaseId,
                task.MilestoneId,
                Labels = task.Labels.OrderBy(label => label.NormalizedName).Select(label => label.Name).ToList(),
                AssigneeIds = task.Assignments.OrderBy(assignment => assignment.AssignedAt).Select(assignment => assignment.UserId).ToList(),
                task.ArchivedAt,
                task.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new TaskSummary(
                row.Id, row.Number, row.Title, row.Priority, row.DueDate, row.PhaseId, row.MilestoneId,
                row.Labels, row.AssigneeIds, row.ArchivedAt, row.UpdatedAt))
            .ToList();

        return new PagedResult<TaskSummary>(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<Result<TaskDetails>> GetAsync(ProjectAccessGrant access, Guid taskId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);

        var task = await dbContext.Tasks
            .AsNoTracking()
            .Include(t => t.Labels)
            .SingleOrDefaultAsync(t => t.Id == taskId && t.ProjectId == access.ProjectId, cancellationToken);
        if (task is null)
        {
            return Result.Failure<TaskDetails>(ErrorKind.NotFound, TaskNotFoundMessage);
        }

        var assignees = await (
            from assignment in dbContext.TaskAssignments.AsNoTracking()
            join user in dbContext.Users on assignment.UserId equals user.Id
            where assignment.TaskId == taskId
            orderby assignment.AssignedAt, user.DisplayName, assignment.UserId
            select new TaskAssignee(assignment.UserId, user.DisplayName, assignment.AssignedAt)).ToListAsync(cancellationToken);

        var dependsOn = await (
            from dependency in dbContext.TaskDependencies.AsNoTracking()
            join other in dbContext.Tasks on dependency.DependsOnTaskId equals other.Id
            where dependency.TaskId == taskId
            orderby other.Number
            select new TaskReference(other.Id, other.Number, other.Title, other.ArchivedAt)).ToListAsync(cancellationToken);

        var blocks = await (
            from dependency in dbContext.TaskDependencies.AsNoTracking()
            join other in dbContext.Tasks on dependency.TaskId equals other.Id
            where dependency.DependsOnTaskId == taskId
            orderby other.Number
            select new TaskReference(other.Id, other.Number, other.Title, other.ArchivedAt)).ToListAsync(cancellationToken);

        return Result.Success(new TaskDetails(
            task.Id,
            task.ProjectId,
            task.Number,
            task.Title,
            task.Description,
            task.Priority,
            task.DueDate,
            task.PhaseId,
            task.MilestoneId,
            task.Labels.Select(label => label.Name).ToList(),
            assignees,
            dependsOn,
            blocks,
            task.CreatedByUserId,
            task.ArchivedAt,
            task.ArchivedByUserId,
            task.CreatedAt,
            task.UpdatedAt,
            access.Permissions.Order(StringComparer.Ordinal).ToList()));
    }

    public async Task<Result<TaskDetails>> UpdateAsync(ProjectAccessGrant access, Guid taskId, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(request);

        var found = await FindWritableTaskAsync(taskId, cancellationToken, includeLabels: true);
        if (found.Error is not null)
        {
            return Result.Failure<TaskDetails>(found.Error.Kind, found.Error.Message);
        }

        var task = found.Value!;
        if (request.Labels is not null && Labels.Normalize(request.Labels, out var labelError) is null)
        {
            return Result.Failure<TaskDetails>(ErrorKind.Validation, labelError!);
        }

        var links = await FindLinksAsync(
            task.ProjectId,
            request.HasPhaseId ? request.PhaseId : null,
            request.HasMilestoneId ? request.MilestoneId : null,
            cancellationToken);
        if (links.Error is not null)
        {
            return Result.Failure<TaskDetails>(links.Error.Kind, links.Error.Message);
        }

        if (request.Title is not null)
        {
            task.Rename(request.Title);
        }

        if (request.HasDescription)
        {
            task.ChangeDescription(request.Description);
        }

        if (request.Priority is { } priority)
        {
            task.ChangePriority(priority);
        }

        if (request.HasDueDate)
        {
            task.ChangeDueDate(request.DueDate);
        }

        if (request.HasPhaseId)
        {
            task.MoveToPhase(links.Value.Phase);
        }

        if (request.HasMilestoneId)
        {
            task.LinkToMilestone(links.Value.Milestone);
        }

        if (request.Labels is not null)
        {
            task.SetLabels(request.Labels);
        }

        // Mudanças só nas etiquetas não alteram a linha da tarefa; marcá-la faz o xmin detectar edições
        // simultâneas.
        dbContext.Entry(task).State = EntityState.Modified;

        return await dbContext.TrySaveChangesAsync(cancellationToken)
            ? await GetAsync(access, taskId, cancellationToken)
            : Result.Failure<TaskDetails>(ErrorKind.Conflict, ConflictMessage);
    }

    public async Task<Result> ArchiveAsync(ActiveMembership actor, Guid taskId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var task = await dbContext.Tasks.SingleOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure(ErrorKind.NotFound, TaskNotFoundMessage);
        }

        if (task.IsArchived)
        {
            return Result.Success();
        }

        if (await IsProjectArchivedAsync(task.ProjectId, cancellationToken))
        {
            return Result.Failure(ErrorKind.Conflict, Project.ArchivedMessage);
        }

        task.Archive(actor.UserId, timeProvider.GetUtcNow());
        if (!await dbContext.TrySaveChangesAsync(cancellationToken))
        {
            return Result.Failure(ErrorKind.Conflict, ConflictMessage);
        }

        LogTaskArchived(logger, task.Id, task.ProjectId, actor.UserId);
        return Result.Success();
    }

    public async Task<Result<TaskDetails>> AddAssignmentAsync(
        ProjectAccessGrant access,
        Guid taskId,
        AddAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(request);

        var found = await FindWritableTaskAsync(taskId, cancellationToken, includeAssignments: true);
        if (found.Error is not null)
        {
            return Result.Failure<TaskDetails>(found.Error.Kind, found.Error.Message);
        }

        var task = found.Value!;
        if (!await AreAllOnTeamAsync(task.ProjectId, [request.UserId], cancellationToken))
        {
            return Result.Failure<TaskDetails>(ErrorKind.NotFound, NotOnTeamMessage);
        }

        if (task.Assignments.Any(assignment => assignment.UserId == request.UserId))
        {
            return Result.Failure<TaskDetails>(ErrorKind.Conflict, "A pessoa já é responsável pela tarefa.");
        }

        if (task.Assignments.Count >= ProjectTask.MaxAssignees)
        {
            return Result.Failure<TaskDetails>(ErrorKind.Validation, $"Uma tarefa pode ter no máximo {ProjectTask.MaxAssignees} responsáveis.");
        }

        task.Assign(request.UserId, timeProvider.GetUtcNow());

        return await dbContext.TrySaveChangesAsync(cancellationToken)
            ? await GetAsync(access, taskId, cancellationToken)
            : Result.Failure<TaskDetails>(ErrorKind.Conflict, ConflictMessage);
    }

    public async Task<Result> RemoveAssignmentAsync(Guid taskId, Guid userId, CancellationToken cancellationToken)
    {
        var found = await FindWritableTaskAsync(taskId, cancellationToken, includeAssignments: true);
        if (found.Error is not null)
        {
            return Result.Failure(found.Error.Kind, found.Error.Message);
        }

        if (!found.Value!.Unassign(userId))
        {
            return Result.Failure(ErrorKind.NotFound, "A pessoa não é responsável pela tarefa.");
        }

        return await dbContext.TrySaveChangesAsync(cancellationToken)
            ? Result.Success()
            : Result.Failure(ErrorKind.Conflict, ConflictMessage);
    }

    public async Task<IReadOnlyList<CommentDetails>> ListCommentsAsync(Guid taskId, CancellationToken cancellationToken) =>
        await QueryComments(taskId).ToListAsync(cancellationToken);

    public async Task<Result<CommentDetails>> AddCommentAsync(ActiveMembership actor, Guid taskId, CommentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var found = await FindWritableTaskAsync(taskId, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure<CommentDetails>(found.Error.Kind, found.Error.Message);
        }

        var comment = new TaskComment(found.Value!, actor.UserId, request.Body);
        dbContext.TaskComments.Add(comment);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(await QueryComments(taskId, comment.Id).SingleAsync(cancellationToken));
    }

    public async Task<Result<CommentDetails>> UpdateCommentAsync(
        ActiveMembership actor,
        Guid taskId,
        Guid commentId,
        CommentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var found = await FindWritableTaskAsync(taskId, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure<CommentDetails>(found.Error.Kind, found.Error.Message);
        }

        var comment = await dbContext.TaskComments.SingleOrDefaultAsync(c => c.TaskId == taskId && c.Id == commentId, cancellationToken);
        if (comment is null)
        {
            return Result.Failure<CommentDetails>(ErrorKind.NotFound, "Comentário não encontrado.");
        }

        if (!comment.IsAuthor(actor.UserId))
        {
            return Result.Failure<CommentDetails>(ErrorKind.Forbidden, "Só o autor pode editar o comentário.");
        }

        comment.Edit(request.Body, timeProvider.GetUtcNow());

        return await dbContext.TrySaveChangesAsync(cancellationToken)
            ? Result.Success(await QueryComments(taskId, commentId).SingleAsync(cancellationToken))
            : Result.Failure<CommentDetails>(ErrorKind.Conflict, ConflictMessage);
    }

    public async Task<Result> DeleteCommentAsync(
        ActiveMembership actor,
        ProjectAccessGrant access,
        Guid taskId,
        Guid commentId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(access);

        var found = await FindWritableTaskAsync(taskId, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure(found.Error.Kind, found.Error.Message);
        }

        var comment = await dbContext.TaskComments.SingleOrDefaultAsync(c => c.TaskId == taskId && c.Id == commentId, cancellationToken);
        if (comment is null)
        {
            return Result.Failure(ErrorKind.NotFound, "Comentário não encontrado.");
        }

        if (!comment.IsAuthor(actor.UserId) && !access.HasPermission(ProjectPermissions.TasksArchive))
        {
            return Result.Failure(ErrorKind.Forbidden, "Só o autor ou um gerente do projeto pode excluir o comentário.");
        }

        dbContext.TaskComments.Remove(comment);

        return await dbContext.TrySaveChangesAsync(cancellationToken)
            ? Result.Success()
            : Result.Failure(ErrorKind.Conflict, ConflictMessage);
    }

    /// <summary>
    /// Acrescenta "a tarefa depende de outra". A verificação de ciclo e a gravação acontecem numa transação
    /// serializável: duas dependências simultâneas que juntas formariam um ciclo não passam as duas.
    /// </summary>
    public async Task<Result<TaskDetails>> AddDependencyAsync(
        ActiveMembership actor,
        ProjectAccessGrant access,
        Guid taskId,
        AddDependencyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(request);

        if (request.DependsOnTaskId == taskId)
        {
            return Result.Failure<TaskDetails>(ErrorKind.Validation, "Uma tarefa não pode depender de si mesma.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var found = await FindWritableTaskAsync(taskId, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure<TaskDetails>(found.Error.Kind, found.Error.Message);
        }

        var task = found.Value!;
        var dependsOn = await dbContext.Tasks.SingleOrDefaultAsync(t => t.Id == request.DependsOnTaskId, cancellationToken);
        if (dependsOn is null)
        {
            return Result.Failure<TaskDetails>(ErrorKind.NotFound, "A tarefa da qual esta dependeria não foi encontrada.");
        }

        if (dependsOn.ProjectId != task.ProjectId)
        {
            return Result.Failure<TaskDetails>(ErrorKind.Validation, "As tarefas precisam ser do mesmo projeto.");
        }

        if (dependsOn.IsArchived)
        {
            return Result.Failure<TaskDetails>(ErrorKind.Conflict, "Não é possível depender de uma tarefa arquivada.");
        }

        var edges = await dbContext.TaskDependencies
            .Where(dependency => dependency.ProjectId == task.ProjectId)
            .Select(dependency => new { dependency.TaskId, dependency.DependsOnTaskId })
            .ToListAsync(cancellationToken);

        if (edges.Exists(edge => edge.TaskId == taskId && edge.DependsOnTaskId == dependsOn.Id))
        {
            return Result.Failure<TaskDetails>(ErrorKind.Conflict, "Esta dependência já existe.");
        }

        if (edges.Count(edge => edge.TaskId == taskId) >= TaskDependency.MaxPerTask)
        {
            return Result.Failure<TaskDetails>(ErrorKind.Validation, $"Uma tarefa pode depender de no máximo {TaskDependency.MaxPerTask} tarefas.");
        }

        if (TaskDependency.WouldCreateCycle(edges.Select(edge => (edge.TaskId, edge.DependsOnTaskId)), taskId, dependsOn.Id))
        {
            return Result.Failure<TaskDetails>(ErrorKind.Conflict, "A dependência criaria um ciclo entre as tarefas.");
        }

        dbContext.TaskDependencies.Add(new TaskDependency(task, dependsOn, actor.UserId, timeProvider.GetUtcNow()));

        try
        {
            if (!await dbContext.TrySaveChangesAsync(cancellationToken))
            {
                return Result.Failure<TaskDetails>(ErrorKind.Conflict, ConflictMessage);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            return Result.Failure<TaskDetails>(ErrorKind.Conflict, ConflictMessage);
        }

        return await GetAsync(access, taskId, cancellationToken);
    }

    public async Task<Result> RemoveDependencyAsync(Guid taskId, Guid dependsOnTaskId, CancellationToken cancellationToken)
    {
        var found = await FindWritableTaskAsync(taskId, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure(found.Error.Kind, found.Error.Message);
        }

        var dependency = await dbContext.TaskDependencies
            .SingleOrDefaultAsync(d => d.TaskId == taskId && d.DependsOnTaskId == dependsOnTaskId, cancellationToken);
        if (dependency is null)
        {
            return Result.Failure(ErrorKind.NotFound, "Dependência não encontrada.");
        }

        dbContext.TaskDependencies.Remove(dependency);

        return await dbContext.TrySaveChangesAsync(cancellationToken)
            ? Result.Success()
            : Result.Failure(ErrorKind.Conflict, ConflictMessage);
    }

    /// <summary>Tarefa rastreada para alteração; tarefa ou projeto arquivado respondem conflito.</summary>
    private async Task<Result<ProjectTask>> FindWritableTaskAsync(
        Guid taskId,
        CancellationToken cancellationToken,
        bool includeLabels = false,
        bool includeAssignments = false)
    {
        var query = dbContext.Tasks.AsQueryable();
        if (includeLabels)
        {
            query = query.Include(task => task.Labels);
        }

        if (includeAssignments)
        {
            query = query.Include(task => task.Assignments);
        }

        var task = await query.SingleOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure<ProjectTask>(ErrorKind.NotFound, TaskNotFoundMessage);
        }

        if (task.IsArchived)
        {
            return Result.Failure<ProjectTask>(ErrorKind.Conflict, ProjectTask.ArchivedMessage);
        }

        return await IsProjectArchivedAsync(task.ProjectId, cancellationToken)
            ? Result.Failure<ProjectTask>(ErrorKind.Conflict, Project.ArchivedMessage)
            : Result.Success(task);
    }

    private Task<bool> IsProjectArchivedAsync(Guid projectId, CancellationToken cancellationToken) =>
        dbContext.Projects.AnyAsync(project => project.Id == projectId && project.ArchivedAt != null, cancellationToken);

    /// <summary>Fase e marco informados, que precisam existir no mesmo projeto.</summary>
    private async Task<Result<(ProjectPhase? Phase, Milestone? Milestone)>> FindLinksAsync(
        Guid projectId,
        Guid? phaseId,
        Guid? milestoneId,
        CancellationToken cancellationToken)
    {
        ProjectPhase? phase = null;
        if (phaseId is not null)
        {
            phase = await dbContext.ProjectPhases.AsNoTracking().SingleOrDefaultAsync(p => p.ProjectId == projectId && p.Id == phaseId, cancellationToken);
            if (phase is null)
            {
                return Result.Failure<(ProjectPhase?, Milestone?)>(ErrorKind.NotFound, "Fase não encontrada neste projeto.");
            }
        }

        Milestone? milestone = null;
        if (milestoneId is not null)
        {
            milestone = await dbContext.Milestones.AsNoTracking().SingleOrDefaultAsync(m => m.ProjectId == projectId && m.Id == milestoneId, cancellationToken);
            if (milestone is null)
            {
                return Result.Failure<(ProjectPhase?, Milestone?)>(ErrorKind.NotFound, "Marco não encontrado neste projeto.");
            }
        }

        return Result.Success<(ProjectPhase?, Milestone?)>((phase, milestone));
    }

    private async Task<bool> AreAllOnTeamAsync(Guid projectId, List<Guid> userIds, CancellationToken cancellationToken) =>
        userIds.Count == 0
        || await dbContext.ProjectMembers.CountAsync(member => member.ProjectId == projectId && userIds.Contains(member.UserId), cancellationToken) == userIds.Count;

    private IQueryable<CommentDetails> QueryComments(Guid taskId, Guid? commentId = null) =>
        from comment in dbContext.TaskComments.AsNoTracking()
        join user in dbContext.Users on comment.AuthorUserId equals user.Id
        where comment.TaskId == taskId && (commentId == null || comment.Id == commentId)
        orderby comment.CreatedAt
        select new CommentDetails(comment.Id, comment.AuthorUserId, user.DisplayName, comment.Body, comment.CreatedAt, comment.EditedAt);

    private static IQueryable<ProjectTask> Sort(IQueryable<ProjectTask> tasks, TaskSortField sortBy, SortDirection direction)
    {
        var descending = direction == SortDirection.Desc;

        return sortBy switch
        {
            // Sem prazo fica sempre por último.
            TaskSortField.DueDate => (descending
                    ? tasks.OrderBy(task => task.DueDate == null).ThenByDescending(task => task.DueDate)
                    : tasks.OrderBy(task => task.DueDate == null).ThenBy(task => task.DueDate))
                .ThenBy(task => task.Number),

            // A prioridade é gravada pelo nome; a ordem vem do valor da enumeração.
            TaskSortField.Priority => (descending
                    ? tasks.OrderByDescending(PriorityRank)
                    : tasks.OrderBy(PriorityRank))
                .ThenBy(task => task.Number),

            TaskSortField.UpdatedAt => (descending ? tasks.OrderByDescending(task => task.UpdatedAt) : tasks.OrderBy(task => task.UpdatedAt))
                .ThenBy(task => task.Number),

            _ => descending ? tasks.OrderByDescending(task => task.Number) : tasks.OrderBy(task => task.Number),
        };
    }

    private static readonly Expression<Func<ProjectTask, int>> PriorityRank = task =>
        task.Priority == Priority.Low ? 0
        : task.Priority == Priority.Medium ? 1
        : task.Priority == Priority.High ? 2
        : 3;

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tarefa {TaskId} (número {TaskNumber}) criada no projeto {ProjectId} pelo usuário {UserId}.")]
    private static partial void LogTaskCreated(ILogger logger, Guid taskId, int taskNumber, Guid projectId, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tarefa {TaskId} do projeto {ProjectId} arquivada pelo usuário {UserId}.")]
    private static partial void LogTaskArchived(ILogger logger, Guid taskId, Guid projectId, Guid userId);
}
