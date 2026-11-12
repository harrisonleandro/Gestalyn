using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Common;
using ProjectManager.Application.Projects;

namespace ProjectManager.Application.Tasks;

/// <summary>
/// Casos de uso de tarefas na organização ativa. As operações pressupõem que o acesso ao projeto da
/// tarefa já foi verificado (<see cref="IProjectAccess"/>).
/// </summary>
public interface ITaskService
{
    Task<Result<TaskDetails>> CreateAsync(ActiveMembership actor, ProjectAccessGrant access, CreateTaskRequest request, CancellationToken cancellationToken);

    Task<PagedResult<TaskSummary>> ListAsync(ActiveMembership actor, Guid projectId, TaskListQuery query, CancellationToken cancellationToken);

    Task<Result<TaskDetails>> GetAsync(ProjectAccessGrant access, Guid taskId, CancellationToken cancellationToken);

    Task<Result<TaskDetails>> UpdateAsync(ProjectAccessGrant access, Guid taskId, UpdateTaskRequest request, CancellationToken cancellationToken);

    Task<Result> ArchiveAsync(ActiveMembership actor, Guid taskId, CancellationToken cancellationToken);

    Task<Result<TaskDetails>> AddAssignmentAsync(ProjectAccessGrant access, Guid taskId, AddAssignmentRequest request, CancellationToken cancellationToken);

    Task<Result> RemoveAssignmentAsync(Guid taskId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CommentDetails>> ListCommentsAsync(Guid taskId, CancellationToken cancellationToken);

    Task<Result<CommentDetails>> AddCommentAsync(ActiveMembership actor, Guid taskId, CommentRequest request, CancellationToken cancellationToken);

    Task<Result<CommentDetails>> UpdateCommentAsync(ActiveMembership actor, Guid taskId, Guid commentId, CommentRequest request, CancellationToken cancellationToken);

    Task<Result> DeleteCommentAsync(ActiveMembership actor, ProjectAccessGrant access, Guid taskId, Guid commentId, CancellationToken cancellationToken);

    Task<Result<TaskDetails>> AddDependencyAsync(
        ActiveMembership actor,
        ProjectAccessGrant access,
        Guid taskId,
        AddDependencyRequest request,
        CancellationToken cancellationToken);

    Task<Result> RemoveDependencyAsync(Guid taskId, Guid dependsOnTaskId, CancellationToken cancellationToken);
}
