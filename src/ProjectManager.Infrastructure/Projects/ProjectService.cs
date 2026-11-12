using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Common;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Organizations;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Projects;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Projects;

/// <summary>
/// Casos de uso de projetos. Leituras e gravações passam pelos filtros e pela validação da organização
/// ativa do <see cref="ProjectManagerDbContext"/>; a visibilidade por equipe é aplicada aqui na listagem
/// e pelo <see cref="IProjectAccess"/> nas rotas por ID.
/// </summary>
internal sealed partial class ProjectService(
    ProjectManagerDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<ProjectService> logger) : IProjectService
{
    private const string NotFoundMessage = "Projeto não encontrado.";
    private const string ConcurrencyMessage = "O projeto foi alterado por outra pessoa. Recarregue e tente novamente.";
    private const string MemberNotFoundMessage = "Membro do projeto não encontrado.";

    public async Task<Result<ProjectDetails>> CreateAsync(ActiveMembership actor, CreateProjectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        if (!Project.IsValidSchedule(request.StartDate, request.DueDate))
        {
            return Result.Failure<ProjectDetails>(ErrorKind.Validation, Project.ScheduleMessage);
        }

        if (request.Tags is not null && Project.NormalizeTags(request.Tags, out var tagError) is null)
        {
            return Result.Failure<ProjectDetails>(ErrorKind.Validation, tagError!);
        }

        var project = new Project(actor.OrganizationId, request.Name, request.Description);
        project.ChangePriority(request.Priority ?? Priority.Medium);
        project.Schedule(request.StartDate, request.DueDate);
        project.SetTags(request.Tags ?? []);

        // Quem cria o projeto entra na equipe como Gerente.
        var creator = new ProjectMember(project, actor.UserId, ProjectRole.Manager);
        dbContext.Projects.Add(project);
        dbContext.ProjectMembers.Add(creator);
        await dbContext.SaveChangesAsync(cancellationToken);

        LogProjectCreated(logger, project.Id, actor.OrganizationId, actor.UserId);

        var access = ProjectAccessGrant.Compute(actor, project.Id, ProjectRole.Manager)!;
        return Result.Success(ToDetails(project, responsible: null, access));
    }

    public async Task<PagedResult<ProjectSummary>> ListAsync(ActiveMembership actor, ProjectListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(query);

        var userId = actor.UserId;
        var projects = dbContext.Projects.AsNoTracking();

        // Visibilidade (ADR-010): sem ver todos os projetos, só os projetos em cuja equipe o usuário está.
        var readsAll = actor.HasPermission(OrganizationPermissions.ProjectsReadAll)
            || actor.HasPermission(OrganizationPermissions.ProjectsManageAll);
        if (!readsAll)
        {
            projects = projects.Where(p => dbContext.ProjectMembers.Any(member => member.ProjectId == p.Id && member.UserId == userId));
        }

        if (!query.IncludeArchived)
        {
            projects = projects.Where(p => p.ArchivedAt == null);
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var normalizedTag = ProjectTag.Normalize(query.Tag);
            projects = projects.Where(p => p.Tags.Any(tag => tag.NormalizedName == normalizedTag));
        }

        if (query.Priority is { } priority)
        {
            projects = projects.Where(p => p.Priority == priority);
        }

        var totalCount = await projects.CountAsync(cancellationToken);
        var rows = await projects
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Priority,
                p.StartDate,
                p.DueDate,
                p.ArchivedAt,
                Tags = p.Tags.OrderBy(tag => tag.NormalizedName).Select(tag => tag.Name).ToList(),
                MyRole = dbContext.ProjectMembers
                    .Where(member => member.ProjectId == p.Id && member.UserId == userId)
                    .Select(member => (ProjectRole?)member.Role)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new ProjectSummary(row.Id, row.Name, row.Priority, row.StartDate, row.DueDate, row.Tags, row.ArchivedAt, row.MyRole))
            .ToList();

        return new PagedResult<ProjectSummary>(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<Result<ProjectDetails>> GetAsync(ProjectAccessGrant access, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);

        var project = await dbContext.Projects
            .AsNoTracking()
            .Include(p => p.Tags)
            .SingleOrDefaultAsync(p => p.Id == access.ProjectId, cancellationToken);

        return project is null
            ? Result.Failure<ProjectDetails>(ErrorKind.NotFound, NotFoundMessage)
            : Result.Success(ToDetails(project, await FindResponsibleAsync(project.Id, cancellationToken), access));
    }

    public async Task<Result<ProjectDetails>> UpdateAsync(ProjectAccessGrant access, UpdateProjectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(request);

        var project = await dbContext.Projects
            .Include(p => p.Tags)
            .SingleOrDefaultAsync(p => p.Id == access.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result.Failure<ProjectDetails>(ErrorKind.NotFound, NotFoundMessage);
        }

        if (project.IsArchived)
        {
            return Result.Failure<ProjectDetails>(ErrorKind.Conflict, Project.ArchivedMessage);
        }

        var startDate = request.HasStartDate ? request.StartDate : project.StartDate;
        var dueDate = request.HasDueDate ? request.DueDate : project.DueDate;
        if (!Project.IsValidSchedule(startDate, dueDate))
        {
            return Result.Failure<ProjectDetails>(ErrorKind.Validation, Project.ScheduleMessage);
        }

        if (request.Tags is not null && Project.NormalizeTags(request.Tags, out var tagError) is null)
        {
            return Result.Failure<ProjectDetails>(ErrorKind.Validation, tagError!);
        }

        if (request.Name is not null)
        {
            project.Rename(request.Name);
        }

        if (request.HasDescription)
        {
            project.ChangeDescription(request.Description);
        }

        if (request.Priority is { } priority)
        {
            project.ChangePriority(priority);
        }

        project.Schedule(startDate, dueDate);

        if (request.Tags is not null)
        {
            project.SetTags(request.Tags);
        }

        // A alteração das etiquetas não muda a linha do projeto; marcá-la como alterada faz o xmin
        // detectar edições simultâneas do mesmo projeto.
        dbContext.Entry(project).State = EntityState.Modified;

        return await TrySaveAsync(cancellationToken) is { } error
            ? Result.Failure<ProjectDetails>(ErrorKind.Conflict, error)
            : Result.Success(ToDetails(project, await FindResponsibleAsync(project.Id, cancellationToken), access));
    }

    public async Task<Result> ArchiveAsync(ActiveMembership actor, Guid projectId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var project = await dbContext.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return Result.Failure(ErrorKind.NotFound, NotFoundMessage);
        }

        if (project.IsArchived)
        {
            return Result.Success();
        }

        project.Archive(actor.UserId, timeProvider.GetUtcNow());
        if (await TrySaveAsync(cancellationToken) is { } error)
        {
            return Result.Failure(ErrorKind.Conflict, error);
        }

        LogProjectArchived(logger, project.Id, actor.OrganizationId, actor.UserId);
        return Result.Success();
    }

    public async Task<IReadOnlyList<ProjectMemberDetails>> ListMembersAsync(Guid projectId, CancellationToken cancellationToken) =>
        await QueryMembers(projectId).ToListAsync(cancellationToken);

    public async Task<Result<ProjectMemberDetails>> AddMemberAsync(Guid projectId, AddProjectMemberRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var project = await FindWritableProjectAsync(projectId, cancellationToken);
        if (project.Error is not null)
        {
            return Result.Failure<ProjectMemberDetails>(project.Error.Kind, project.Error.Message);
        }

        if (!await dbContext.OrganizationMembers.AnyAsync(member => member.UserId == request.UserId, cancellationToken))
        {
            return Result.Failure<ProjectMemberDetails>(ErrorKind.NotFound, "A pessoa não é membro da organização.");
        }

        if (await dbContext.ProjectMembers.AnyAsync(member => member.ProjectId == projectId && member.UserId == request.UserId, cancellationToken))
        {
            return Result.Failure<ProjectMemberDetails>(ErrorKind.Conflict, "A pessoa já está na equipe do projeto.");
        }

        var member = new ProjectMember(project.Value!, request.UserId, request.Role);
        dbContext.ProjectMembers.Add(member);

        var error = await SaveMemberAsync(member, request.IsResponsible, cancellationToken);

        return error is not null
            ? Result.Failure<ProjectMemberDetails>(ErrorKind.Conflict, error)
            : Result.Success(await QueryMembers(projectId, request.UserId).SingleAsync(cancellationToken));
    }

    public async Task<Result<ProjectMemberDetails>> UpdateMemberAsync(
        Guid projectId,
        Guid userId,
        UpdateProjectMemberRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var project = await FindWritableProjectAsync(projectId, cancellationToken);
        if (project.Error is not null)
        {
            return Result.Failure<ProjectMemberDetails>(project.Error.Kind, project.Error.Message);
        }

        var member = await dbContext.ProjectMembers.SingleOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return Result.Failure<ProjectMemberDetails>(ErrorKind.NotFound, MemberNotFoundMessage);
        }

        if (request.Role is { } role)
        {
            member.ChangeRole(role);
        }

        if (request.IsResponsible == false)
        {
            member.UnmarkAsResponsible();
        }

        var error = await SaveMemberAsync(member, request.IsResponsible == true, cancellationToken);

        return error is not null
            ? Result.Failure<ProjectMemberDetails>(ErrorKind.Conflict, error)
            : Result.Success(await QueryMembers(projectId, userId).SingleAsync(cancellationToken));
    }

    public async Task<Result> RemoveMemberAsync(Guid projectId, Guid userId, CancellationToken cancellationToken)
    {
        var project = await FindWritableProjectAsync(projectId, cancellationToken);
        if (project.Error is not null)
        {
            return Result.Failure(project.Error.Kind, project.Error.Message);
        }

        var member = await dbContext.ProjectMembers.SingleOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return Result.Failure(ErrorKind.NotFound, MemberNotFoundMessage);
        }

        dbContext.ProjectMembers.Remove(member);

        return await TrySaveAsync(cancellationToken) is { } error
            ? Result.Failure(ErrorKind.Conflict, error)
            : Result.Success();
    }

    public async Task<IReadOnlyList<ProjectRepositoryDetails>> ListRepositoriesAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var repositories = await dbContext.ProjectRepositories
            .AsNoTracking()
            .Where(repository => repository.ProjectId == projectId)
            .OrderBy(repository => repository.Url)
            .ToListAsync(cancellationToken);

        return repositories.Select(ToDetails).ToList();
    }

    public async Task<Result<ProjectRepositoryDetails>> AddRepositoryAsync(
        Guid projectId,
        AddProjectRepositoryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!ProjectRepository.TryNormalizeUrl(request.Url, out var url, out var urlError))
        {
            return Result.Failure<ProjectRepositoryDetails>(ErrorKind.Validation, urlError);
        }

        var project = await FindWritableProjectAsync(projectId, cancellationToken, includeRepositories: true);
        if (project.Error is not null)
        {
            return Result.Failure<ProjectRepositoryDetails>(project.Error.Kind, project.Error.Message);
        }

        var repositories = project.Value!.Repositories;
        if (repositories.Any(repository => repository.Url == url.AbsoluteUri))
        {
            return Result.Failure<ProjectRepositoryDetails>(ErrorKind.Conflict, "Este repositório já está associado ao projeto.");
        }

        if (repositories.Count >= ProjectRepository.MaxPerProject)
        {
            return Result.Failure<ProjectRepositoryDetails>(
                ErrorKind.Validation,
                $"Um projeto pode ter no máximo {ProjectRepository.MaxPerProject} repositórios.");
        }

        var repository = project.Value.AddRepository(url, request.Name);

        return await TrySaveAsync(cancellationToken) is { } error
            ? Result.Failure<ProjectRepositoryDetails>(ErrorKind.Conflict, error)
            : Result.Success(ToDetails(repository));
    }

    public async Task<Result> RemoveRepositoryAsync(Guid projectId, Guid repositoryId, CancellationToken cancellationToken)
    {
        var project = await FindWritableProjectAsync(projectId, cancellationToken, includeRepositories: true);
        if (project.Error is not null)
        {
            return Result.Failure(project.Error.Kind, project.Error.Message);
        }

        if (!project.Value!.RemoveRepository(repositoryId))
        {
            return Result.Failure(ErrorKind.NotFound, "Repositório não encontrado.");
        }

        return await TrySaveAsync(cancellationToken) is { } error
            ? Result.Failure(ErrorKind.Conflict, error)
            : Result.Success();
    }

    /// <summary>Projeto rastreado para alteração; arquivado responde conflito (somente leitura).</summary>
    private async Task<Result<Project>> FindWritableProjectAsync(Guid projectId, CancellationToken cancellationToken, bool includeRepositories = false)
    {
        var query = dbContext.Projects.AsQueryable();
        if (includeRepositories)
        {
            query = query.Include(p => p.Repositories);
        }

        var project = await query.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        return project switch
        {
            null => Result.Failure<Project>(ErrorKind.NotFound, NotFoundMessage),
            { IsArchived: true } => Result.Failure<Project>(ErrorKind.Conflict, Project.ArchivedMessage),
            _ => Result.Success(project),
        };
    }

    /// <summary>
    /// Grava a participação. Para torná-la responsável, o responsável anterior é desmarcado antes, na
    /// mesma transação: o índice único parcial não admite dois responsáveis nem por um instante.
    /// </summary>
    private async Task<string?> SaveMemberAsync(ProjectMember member, bool makeResponsible, CancellationToken cancellationToken)
    {
        if (!makeResponsible)
        {
            return await TrySaveAsync(cancellationToken);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.ProjectMembers
            .Where(m => m.ProjectId == member.ProjectId && m.IsResponsible && m.Id != member.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(m => m.IsResponsible, false)
                    .SetProperty(m => m.UpdatedAt, timeProvider.GetUtcNow()),
                cancellationToken);

        member.MarkAsResponsible();
        if (await TrySaveAsync(cancellationToken) is { } error)
        {
            return error;
        }

        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    private IQueryable<ProjectMemberDetails> QueryMembers(Guid projectId, Guid? userId = null) =>
        from member in dbContext.ProjectMembers.AsNoTracking()
        join user in dbContext.Users on member.UserId equals user.Id
        where member.ProjectId == projectId && (userId == null || member.UserId == userId)
        orderby user.DisplayName, member.CreatedAt
        select new ProjectMemberDetails(member.UserId, user.DisplayName, user.Email!, member.Role, member.IsResponsible, member.CreatedAt);

    private Task<ProjectResponsible?> FindResponsibleAsync(Guid projectId, CancellationToken cancellationToken) =>
        (from member in dbContext.ProjectMembers.AsNoTracking()
         join user in dbContext.Users on member.UserId equals user.Id
         where member.ProjectId == projectId && member.IsResponsible
         select new ProjectResponsible(member.UserId, user.DisplayName)).SingleOrDefaultAsync(cancellationToken);

    private async Task<string?> TrySaveAsync(CancellationToken cancellationToken) =>
        await dbContext.TrySaveChangesAsync(cancellationToken) ? null : ConcurrencyMessage;

    private static ProjectDetails ToDetails(Project project, ProjectResponsible? responsible, ProjectAccessGrant access) =>
        new(
            project.Id,
            project.Name,
            project.Description,
            project.Priority,
            project.StartDate,
            project.DueDate,
            project.Tags.Select(tag => tag.Name).ToList(),
            responsible,
            project.ArchivedAt,
            project.ArchivedByUserId,
            project.CreatedAt,
            project.UpdatedAt,
            access.Role,
            access.Permissions.Order(StringComparer.Ordinal).ToList());

    private static ProjectRepositoryDetails ToDetails(ProjectRepository repository) =>
        new(repository.Id, repository.Url, repository.Name, repository.Provider, repository.CreatedAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Projeto {ProjectId} criado na organização {OrganizationId} pelo usuário {UserId}.")]
    private static partial void LogProjectCreated(ILogger logger, Guid projectId, Guid organizationId, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Projeto {ProjectId} da organização {OrganizationId} arquivado pelo usuário {UserId}.")]
    private static partial void LogProjectArchived(ILogger logger, Guid projectId, Guid organizationId, Guid userId);
}
