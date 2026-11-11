namespace ProjectManager.Application.Abstractions.MultiTenancy;

/// <summary>
/// Organização ativa da operação em curso, resolvida no backend a partir da identidade autenticada.
/// Nunca deve ser preenchida com um valor enviado pelo cliente sem validação de pertencimento.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Organização ativa, ou <c>null</c> quando nenhuma foi resolvida. Sem organização ativa,
    /// nenhum dado pertencente a organizações pode ser lido ou gravado.
    /// </summary>
    Guid? OrganizationId { get; }

    /// <summary>
    /// Associação do usuário autenticado à organização ativa (papel e permissões), quando resolvida.
    /// </summary>
    ActiveMembership? Membership { get; }
}

/// <summary>Associação validada de um usuário a uma organização.</summary>
public sealed record ActiveMembership(
    Guid OrganizationId,
    Guid MemberId,
    Guid UserId,
    Guid RoleId,
    bool IsOwner,
    IReadOnlySet<string> Permissions)
{
    public bool HasPermission(string permission) => Permissions.Contains(permission);
}
