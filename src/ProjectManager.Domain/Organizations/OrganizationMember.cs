using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Organizations;

/// <summary>
/// Associação de um usuário a uma organização, com um papel da mesma organização.
/// </summary>
public sealed class OrganizationMember : Entity, IOrganizationOwned, IAuditable
{
    public OrganizationMember(Guid organizationId, Guid userId, Role role)
    {
        ArgumentNullException.ThrowIfNull(role);

        OrganizationId = Guard.RequiredId(organizationId, nameof(organizationId));
        UserId = Guard.RequiredId(userId, nameof(userId));
        RoleId = EnsureSameOrganization(role);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private OrganizationMember(Guid organizationId, Guid userId, Guid roleId)
    {
        OrganizationId = organizationId;
        UserId = userId;
        RoleId = roleId;
    }

    public Guid OrganizationId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void ChangeRole(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);
        RoleId = EnsureSameOrganization(role);
    }

    private Guid EnsureSameOrganization(Role role) =>
        role.OrganizationId == OrganizationId
            ? role.Id
            : throw new ArgumentException("O papel pertence a outra organização.", nameof(role));
}
