using ProjectManager.Application.Organizations;

namespace ProjectManager.Application.Abstractions.MultiTenancy;

/// <summary>
/// Único ponto autorizado a consultar associações sem a organização ativa (ADR-008). Toda consulta é
/// restrita ao usuário autenticado: nunca revela dados de organizações das quais ele não participa.
/// </summary>
public interface IMembershipDirectory
{
    Task<ActiveMembership?> FindMembershipAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizationSummary>> ListOrganizationsAsync(Guid userId, CancellationToken cancellationToken);
}
