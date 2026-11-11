using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Projects;

/// <summary>
/// Participação de um membro da organização na equipe de um projeto. O banco garante que o usuário é
/// membro da mesma organização e remove a participação quando ele sai dela.
/// </summary>
public sealed class ProjectMember : Entity, IOrganizationOwned, IAuditable
{
    public ProjectMember(Project project, Guid userId, ProjectRole role)
    {
        ArgumentNullException.ThrowIfNull(project);

        OrganizationId = project.OrganizationId;
        ProjectId = project.Id;
        UserId = Guard.RequiredId(userId, nameof(userId));
        Role = EnsureDefined(role);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private ProjectMember()
    {
    }

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid UserId { get; private set; }

    public ProjectRole Role { get; private set; }

    /// <summary>Responsável pelo projeto. Há no máximo um por projeto (índice único parcial).</summary>
    public bool IsResponsible { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<string> Permissions => ProjectPermissions.For(Role);

    public void ChangeRole(ProjectRole role) => Role = EnsureDefined(role);

    public void MarkAsResponsible() => IsResponsible = true;

    public void UnmarkAsResponsible() => IsResponsible = false;

    private static ProjectRole EnsureDefined(ProjectRole role) =>
        Enum.IsDefined(role) ? role : throw new ArgumentOutOfRangeException(nameof(role), role, "Papel de projeto desconhecido.");
}
