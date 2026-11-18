using ProjectManager.Application.Abstractions.MultiTenancy;

namespace ProjectManager.Infrastructure.MultiTenancy;

/// <summary>
/// Organização ativa da requisição (escopo por requisição). Começa vazia — sem organização ativa,
/// nenhum dado de organização é lido ou gravado — e só é preenchida depois de validar a associação do
/// usuário autenticado (ADR-008).
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? OrganizationId { get; private set; }

    public ActiveMembership? Membership { get; private set; }

    public ActiveAgent? Agent { get; private set; }

    /// <summary>Define a organização ativa a partir de uma associação já validada.</summary>
    public void SetActiveMembership(ActiveMembership membership)
    {
        ArgumentNullException.ThrowIfNull(membership);

        OrganizationId = membership.OrganizationId;
        Membership = membership;
        Agent = null;
    }

    /// <summary>Define a organização ativa a partir da credencial de um agente já validada (ADR-016).</summary>
    public void SetActiveAgent(ActiveAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        OrganizationId = agent.OrganizationId;
        Agent = agent;
        Membership = null;
    }

    /// <summary>
    /// Ativa uma organização sem associação, para operações que provam o vínculo de outra forma:
    /// criação da organização (o usuário é o criador) e aceitação de convite (o token prova o convite).
    /// </summary>
    internal void UseOrganization(Guid organizationId)
    {
        OrganizationId = organizationId;
        Membership = null;
        Agent = null;
    }
}
