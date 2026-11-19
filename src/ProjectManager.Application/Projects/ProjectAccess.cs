using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Domain.Organizations;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Workflows;

namespace ProjectManager.Application.Projects;

/// <summary>Acesso efetivo de uma pessoa ou de um agente a um projeto da organização ativa.</summary>
public sealed record ProjectAccessGrant(Guid ProjectId, ProjectRole? Role, IReadOnlySet<string> Permissions)
{
    /// <summary>Usuário a quem o acesso foi concedido (o do token). Vazio quando o ator é um agente.</summary>
    public Guid UserId { get; init; }

    /// <summary>Agente a quem o acesso foi concedido (ADR-016), calculado só por <see cref="AgentAccessGrant"/>.</summary>
    public Guid? AgentId { get; init; }

    public ActorKind ActorKind => AgentId is null ? ActorKind.User : ActorKind.Agent;

    public bool HasPermission(string permission) => Permissions.Contains(permission);

    /// <summary>
    /// Combina o papel na organização com o papel na equipe (ADR-010): <c>projects.manage_all</c> concede
    /// todas as permissões de projeto, <c>projects.read_all</c> concede a leitura, e o papel na equipe
    /// soma as dele. Sem nenhuma permissão, o projeto é invisível (<c>null</c>).
    /// </summary>
    public static ProjectAccessGrant? Compute(ActiveMembership membership, Guid projectId, ProjectRole? role)
    {
        ArgumentNullException.ThrowIfNull(membership);

        var permissions = new HashSet<string>(StringComparer.Ordinal);
        if (membership.HasPermission(OrganizationPermissions.ProjectsManageAll))
        {
            permissions.UnionWith(ProjectPermissions.All);
        }

        if (membership.HasPermission(OrganizationPermissions.ProjectsReadAll))
        {
            permissions.Add(ProjectPermissions.Read);
        }

        if (role is not null)
        {
            permissions.UnionWith(ProjectPermissions.For(role.Value));
        }

        return permissions.Count == 0 ? null : new ProjectAccessGrant(projectId, role, permissions) { UserId = membership.UserId };
    }
}

/// <summary>Resolve o acesso do usuário a um projeto. É o único ponto que decide a visibilidade.</summary>
public interface IProjectAccess
{
    /// <summary>
    /// Acesso ao projeto da organização ativa, ou <c>null</c> se o projeto não existe nela ou não é
    /// visível para o usuário.
    /// </summary>
    Task<ProjectAccessGrant?> FindAsync(ActiveMembership membership, Guid projectId, CancellationToken cancellationToken);

    /// <summary>Acesso ao projeto da tarefa, ou <c>null</c> se a tarefa não existe ou o projeto é invisível.</summary>
    Task<ProjectAccessGrant?> FindByTaskAsync(ActiveMembership membership, Guid taskId, CancellationToken cancellationToken);

    /// <summary>Acesso ao projeto do estado de workflow (ADR-013).</summary>
    Task<ProjectAccessGrant?> FindByWorkflowStateAsync(ActiveMembership membership, Guid stateId, CancellationToken cancellationToken);

    /// <summary>Acesso ao projeto da transição de workflow (ADR-013).</summary>
    Task<ProjectAccessGrant?> FindByWorkflowTransitionAsync(ActiveMembership membership, Guid transitionId, CancellationToken cancellationToken);

    /// <summary>Acesso ao projeto do documento (ADR-014).</summary>
    Task<ProjectAccessGrant?> FindByDocumentAsync(ActiveMembership membership, Guid documentId, CancellationToken cancellationToken);

    /// <summary>Acesso ao projeto da decisão (ADR-014).</summary>
    Task<ProjectAccessGrant?> FindByDecisionAsync(ActiveMembership membership, Guid decisionId, CancellationToken cancellationToken);
}
