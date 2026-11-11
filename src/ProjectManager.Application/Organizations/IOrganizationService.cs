using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Common;

namespace ProjectManager.Application.Organizations;

/// <summary>
/// Casos de uso de organizações, membros, papéis e convites. Operações sobre uma organização existente
/// atuam sobre a organização ativa (<see cref="ITenantContext"/>), já validada pela associação do usuário.
/// </summary>
public interface IOrganizationService
{
    /// <summary>Cria a organização com os papéis de sistema; o criador vira Proprietário.</summary>
    Task<Result<OrganizationSummary>> CreateAsync(Guid userId, OrganizationNameRequest request, CancellationToken cancellationToken);

    Task<Result<OrganizationDetails>> GetActiveAsync(CancellationToken cancellationToken);

    Task<Result<OrganizationDetails>> RenameActiveAsync(OrganizationNameRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<MemberDetails>> ListMembersAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<RoleDetails>> ListRolesAsync(CancellationToken cancellationToken);

    Task<Result<MemberDetails>> ChangeMemberRoleAsync(ActiveMembership actor, Guid userId, ChangeMemberRoleRequest request, CancellationToken cancellationToken);

    /// <summary>Remove um membro; qualquer membro pode remover a si mesmo (sair da organização).</summary>
    Task<Result> RemoveMemberAsync(ActiveMembership actor, Guid userId, CancellationToken cancellationToken);

    Task<Result<InvitationDetails>> CreateInvitationAsync(ActiveMembership actor, CreateInvitationRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<InvitationDetails>> ListInvitationsAsync(CancellationToken cancellationToken);

    Task<Result> RevokeInvitationAsync(Guid invitationId, CancellationToken cancellationToken);

    /// <summary>Aceita o convite em nome do usuário autenticado, cujo e-mail confirmado deve ser o convidado.</summary>
    Task<Result<AcceptedInvitation>> AcceptInvitationAsync(Guid userId, AcceptInvitationRequest request, CancellationToken cancellationToken);
}
