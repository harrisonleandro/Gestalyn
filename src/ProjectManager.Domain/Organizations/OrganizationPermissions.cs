namespace ProjectManager.Domain.Organizations;

/// <summary>
/// Catálogo de permissões da organização. A autorização é sempre feita por permissão, nunca pelo nome
/// do papel, para que papéis personalizados funcionem sem mudar código.
/// </summary>
public static class OrganizationPermissions
{
    public const int CodeMaxLength = 64;

    public const string OrganizationRead = "organization.read";
    public const string OrganizationUpdate = "organization.update";
    public const string MembersRead = "members.read";
    public const string MembersInvite = "members.invite";
    public const string MembersUpdateRole = "members.update_role";
    public const string MembersRemove = "members.remove";

    public static IReadOnlyDictionary<string, string> Catalog { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [OrganizationRead] = "Consultar os dados da organização",
        [OrganizationUpdate] = "Alterar os dados da organização",
        [MembersRead] = "Consultar membros e papéis",
        [MembersInvite] = "Convidar pessoas e gerenciar convites",
        [MembersUpdateRole] = "Alterar o papel de membros",
        [MembersRemove] = "Remover membros",
    };
}
