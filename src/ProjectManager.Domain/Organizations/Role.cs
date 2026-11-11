using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Organizations;

/// <summary>Papéis criados automaticamente com cada organização.</summary>
public enum SystemRole
{
    Owner,
    Administrator,
    Member,
    Viewer,
}

/// <summary>
/// Papel de uma organização: um conjunto de permissões do catálogo. Os papéis de sistema são criados
/// com a organização; papéis personalizados usam o mesmo modelo (sem <see cref="SystemRole"/>).
/// </summary>
public sealed class Role : Entity, IOrganizationOwned, IAuditable
{
    public const int NameMaxLength = 100;

    private readonly List<RolePermission> _permissions = [];

    private Role(Guid organizationId, string name, SystemRole? systemRole, IEnumerable<string> permissionCodes)
    {
        OrganizationId = Guard.RequiredId(organizationId, nameof(organizationId));
        Name = Guard.RequiredText(name, NameMaxLength, nameof(name));
        SystemRole = systemRole;

        foreach (var code in permissionCodes.Distinct(StringComparer.Ordinal))
        {
            if (!OrganizationPermissions.Catalog.ContainsKey(code))
            {
                throw new ArgumentException($"Permissão desconhecida: {code}.", nameof(permissionCodes));
            }

            _permissions.Add(new RolePermission(OrganizationId, Id, code));
        }
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private Role(string name) => Name = name;

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; }

    public SystemRole? SystemRole { get; private set; }

    public bool IsOwner => SystemRole == Organizations.SystemRole.Owner;

    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool Grants(string permissionCode) =>
        _permissions.Any(permission => permission.PermissionCode == permissionCode);

    /// <summary>
    /// Papéis de sistema: Proprietário e Administrador têm todas as permissões (somente proprietários
    /// promovem ou rebaixam proprietários); Membro e Leitor têm leitura. A distinção entre Membro e
    /// Leitor ganha efeito com projetos e tarefas (Fases 05 e 06).
    /// </summary>
    public static IReadOnlyList<Role> CreateSystemRoles(Guid organizationId)
    {
        var all = OrganizationPermissions.Catalog.Keys.ToArray();
        string[] read = [OrganizationPermissions.OrganizationRead, OrganizationPermissions.MembersRead];

        return
        [
            new Role(organizationId, "Proprietário", Organizations.SystemRole.Owner, all),
            new Role(organizationId, "Administrador", Organizations.SystemRole.Administrator, all),
            new Role(organizationId, "Membro", Organizations.SystemRole.Member, read),
            new Role(organizationId, "Leitor", Organizations.SystemRole.Viewer, read),
        ];
    }
}

/// <summary>Associação entre um papel e uma permissão do catálogo.</summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Nome da entidade definido na especificação mestre (RolePermission); não é uma permissão de segurança do .NET.")]
public sealed class RolePermission : IOrganizationOwned
{
    internal RolePermission(Guid organizationId, Guid roleId, string permissionCode)
    {
        OrganizationId = organizationId;
        RoleId = roleId;
        PermissionCode = permissionCode;
    }

    public Guid OrganizationId { get; private set; }

    public Guid RoleId { get; private set; }

    public string PermissionCode { get; private set; }
}
