using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectManager.Application.Abstractions.Activity;
using ProjectManager.Application.Activity;
using ProjectManager.Application.Common;
using ProjectManager.Domain.Activity;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Activity;

/// <summary>
/// Lista de atividades (ADR-014, D1 e D4): eventos do sistema e registros manuais numa só lista, mais novos
/// primeiro. A paginação é feita sobre a união dos IDs e datas; os detalhes da página são lidos depois.
/// </summary>
internal sealed class ActivityService(
    ProjectManagerDbContext dbContext,
    IActivityContext activityContext,
    TimeProvider timeProvider) : IActivityService
{
    public Task<PagedResult<ActivityItem>> ListProjectAsync(Guid projectId, ActivityListQuery query, CancellationToken cancellationToken) =>
        ListAsync(projectId, query, cancellationToken);

    public Task<PagedResult<ActivityItem>> ListOrganizationAsync(ActivityListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ListAsync(query.ProjectId, query, cancellationToken);
    }

    public async Task<Result<ActivityItem>> CreateLogAsync(Guid projectId, CreateActivityLogRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var project = await dbContext.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return Result.Failure<ActivityItem>(ErrorKind.NotFound, "Projeto não encontrado.");
        }

        if (project.IsArchived)
        {
            return Result.Failure<ActivityItem>(ErrorKind.Conflict, Domain.Projects.Project.ArchivedMessage);
        }

        if (request.TaskId is { } taskId && !await dbContext.Tasks.AnyAsync(t => t.ProjectId == projectId && t.Id == taskId, cancellationToken))
        {
            return Result.Failure<ActivityItem>(ErrorKind.NotFound, "Tarefa não encontrada neste projeto.");
        }

        var now = timeProvider.GetUtcNow();
        var occurredAt = request.OccurredAt ?? now;
        if (occurredAt > now)
        {
            return Result.Failure<ActivityItem>(ErrorKind.Validation, ActivityLog.FutureDateMessage);
        }

        var log = new ActivityLog(
            project.OrganizationId,
            projectId,
            request.TaskId,
            activityContext.Actor,
            request.Kind!.Value,
            request.Summary,
            request.Details,
            occurredAt,
            now);
        dbContext.ActivityLogs.Add(log);
        await dbContext.SaveChangesAsync(cancellationToken);

        var names = await FindNamesAsync([log.AuthorId], cancellationToken);
        return Result.Success(ToItem(log, names));
    }

    private async Task<PagedResult<ActivityItem>> ListAsync(Guid? projectId, ActivityListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Ação e tipo de entidade só existem nos eventos: filtrar por eles exclui os registros manuais.
        var includeEvents = query.Source is null or ActivitySource.Event;
        var includeLogs = (query.Source is null or ActivitySource.Log)
            && string.IsNullOrWhiteSpace(query.Action)
            && string.IsNullOrWhiteSpace(query.EntityType);

        var keys = includeEvents && includeLogs
            ? FilterEvents(projectId, query).Select(e => new { e.Id, e.OccurredAt })
                .Concat(FilterLogs(projectId, query).Select(l => new { l.Id, l.OccurredAt }))
            : includeEvents
                ? FilterEvents(projectId, query).Select(e => new { e.Id, e.OccurredAt })
                : FilterLogs(projectId, query).Select(l => new { l.Id, l.OccurredAt });

        var totalCount = await keys.CountAsync(cancellationToken);
        var page = await keys
            .OrderByDescending(key => key.OccurredAt)
            .ThenByDescending(key => key.Id)
            .Skip(query.CalculateOffset())
            .Take(query.PageSize)
            .Select(key => key.Id)
            .ToListAsync(cancellationToken);

        var events = page.Count == 0
            ? []
            : await dbContext.ActivityEvents.AsNoTracking().Where(e => page.Contains(e.Id)).ToListAsync(cancellationToken);
        var logs = page.Count == 0 || !includeLogs
            ? []
            : await dbContext.ActivityLogs.AsNoTracking().Where(l => page.Contains(l.Id)).ToListAsync(cancellationToken);

        var names = await FindNamesAsync(events.Select(e => e.ActorId).Concat(logs.Select(l => l.AuthorId)), cancellationToken);
        var items = events.Select(e => ToItem(e, names)).Concat(logs.Select(l => ToItem(l, names))).ToDictionary(item => item.Id);

        return new PagedResult<ActivityItem>(page.Select(id => items[id]).ToList(), query.Page, query.PageSize, totalCount);
    }

    private IQueryable<ActivityEvent> FilterEvents(Guid? projectId, ActivityListQuery query)
    {
        var events = dbContext.ActivityEvents.AsNoTracking();
        if (projectId is { } project)
        {
            events = events.Where(e => e.ProjectId == project);
        }

        if (query.ActorId is { } actorId)
        {
            events = events.Where(e => e.ActorId == actorId);
        }

        if (query.ActorKind is { } actorKind)
        {
            events = events.Where(e => e.ActorKind == actorKind);
        }

        if (query.Origin is { } origin)
        {
            events = events.Where(e => e.Origin == origin);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var action = query.Action.Trim();
            events = events.Where(e => e.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            var entityType = query.EntityType.Trim();
            events = events.Where(e => e.EntityType == entityType);
        }

        if (query.TaskId is { } taskId)
        {
            events = events.Where(e => e.TaskId == taskId);
        }

        if (query.From is { } from)
        {
            events = events.Where(e => e.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            events = events.Where(e => e.OccurredAt <= to);
        }

        return events;
    }

    private IQueryable<ActivityLog> FilterLogs(Guid? projectId, ActivityListQuery query)
    {
        var logs = dbContext.ActivityLogs.AsNoTracking();
        if (projectId is { } project)
        {
            logs = logs.Where(l => l.ProjectId == project);
        }

        if (query.ActorId is { } actorId)
        {
            logs = logs.Where(l => l.AuthorId == actorId);
        }

        if (query.ActorKind is { } actorKind)
        {
            logs = logs.Where(l => l.ActorKind == actorKind);
        }

        if (query.Origin is { } origin)
        {
            logs = logs.Where(l => l.Origin == origin);
        }

        if (query.TaskId is { } taskId)
        {
            logs = logs.Where(l => l.TaskId == taskId);
        }

        if (query.From is { } from)
        {
            logs = logs.Where(l => l.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            logs = logs.Where(l => l.OccurredAt <= to);
        }

        return logs;
    }

    private async Task<Dictionary<Guid, string>> FindNamesAsync(IEnumerable<Guid?> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.OfType<Guid>().Distinct().ToList();
        return ids.Count == 0
            ? []
            : await dbContext.Users.AsNoTracking()
                .Where(user => ids.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
    }

    private static ActivityItem ToItem(ActivityEvent activity, Dictionary<Guid, string> names) =>
        new(
            activity.Id,
            ActivitySource.Event,
            activity.ProjectId,
            activity.TaskId,
            activity.ActorId,
            activity.ActorId is { } actorId ? names.GetValueOrDefault(actorId) : null,
            activity.ActorKind,
            activity.Origin,
            activity.RequestId,
            activity.Action,
            activity.EntityType,
            activity.EntityId,
            Kind: null,
            activity.Summary,
            Details: null,
            activity.Changes is null ? null : ParseChanges(activity.Changes),
            activity.OccurredAt,
            RecordedAt: null);

    private static ActivityItem ToItem(ActivityLog log, Dictionary<Guid, string> names) =>
        new(
            log.Id,
            ActivitySource.Log,
            log.ProjectId,
            log.TaskId,
            log.AuthorId,
            log.AuthorId is { } authorId ? names.GetValueOrDefault(authorId) : null,
            log.ActorKind,
            log.Origin,
            log.RequestId,
            Action: null,
            EntityType: null,
            EntityId: null,
            log.Kind,
            log.Summary,
            log.Details,
            Changes: null,
            log.OccurredAt,
            log.RecordedAt);

    private static JsonElement ParseChanges(string changes)
    {
        using var document = JsonDocument.Parse(changes);
        return document.RootElement.Clone();
    }
}
