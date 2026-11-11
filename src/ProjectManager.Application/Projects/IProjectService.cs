using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Common;

namespace ProjectManager.Application.Projects;

/// <summary>
/// Casos de uso de projetos na organização ativa. As operações por ID pressupõem que o acesso ao projeto
/// já foi verificado (<see cref="IProjectAccess"/>).
/// </summary>
public interface IProjectService
{
    Task<Result<ProjectDetails>> CreateAsync(ActiveMembership actor, CreateProjectRequest request, CancellationToken cancellationToken);

    Task<PagedResult<ProjectSummary>> ListAsync(ActiveMembership actor, ProjectListQuery query, CancellationToken cancellationToken);

    Task<Result<ProjectDetails>> GetAsync(ProjectAccessGrant access, CancellationToken cancellationToken);

    Task<Result<ProjectDetails>> UpdateAsync(ProjectAccessGrant access, UpdateProjectRequest request, CancellationToken cancellationToken);

    Task<Result> ArchiveAsync(ActiveMembership actor, Guid projectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProjectMemberDetails>> ListMembersAsync(Guid projectId, CancellationToken cancellationToken);

    Task<Result<ProjectMemberDetails>> AddMemberAsync(Guid projectId, AddProjectMemberRequest request, CancellationToken cancellationToken);

    Task<Result<ProjectMemberDetails>> UpdateMemberAsync(Guid projectId, Guid userId, UpdateProjectMemberRequest request, CancellationToken cancellationToken);

    Task<Result> RemoveMemberAsync(Guid projectId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProjectRepositoryDetails>> ListRepositoriesAsync(Guid projectId, CancellationToken cancellationToken);

    Task<Result<ProjectRepositoryDetails>> AddRepositoryAsync(Guid projectId, AddProjectRepositoryRequest request, CancellationToken cancellationToken);

    Task<Result> RemoveRepositoryAsync(Guid projectId, Guid repositoryId, CancellationToken cancellationToken);
}
