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

    /// <summary>
    /// Agente autenticado por credencial própria (ADR-016), quando a requisição é de um agente. A organização dele
    /// vem da credencial, nunca de cabeçalho ou rota. Exclusivo com <see cref="Membership"/>.
    /// </summary>
    ActiveAgent? Agent { get; }
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

/// <summary>Agente autenticado, com a organização e os escopos da credencial usada (ADR-016).</summary>
public sealed record ActiveAgent(Guid OrganizationId, Guid AgentId, Guid CredentialId, IReadOnlySet<string> Scopes)
{
    public bool HasScope(string scope) => Scopes.Contains(scope);
}
