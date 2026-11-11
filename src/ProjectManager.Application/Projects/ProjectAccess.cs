using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Domain.Organizations;
using ProjectManager.Domain.Projects;

namespace ProjectManager.Application.Projects;

/// <summary>Acesso efetivo do usuário a um projeto da organização ativa.</summary>
public sealed record ProjectAccessGrant(Guid ProjectId, ProjectRole? Role, IReadOnlySet<string> Permissions)
{
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

        return permissions.Count == 0 ? null : new ProjectAccessGrant(projectId, role, permissions);
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
}
