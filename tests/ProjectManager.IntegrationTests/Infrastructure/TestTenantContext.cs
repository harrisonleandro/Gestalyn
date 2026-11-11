using ProjectManager.Application.Abstractions.MultiTenancy;

namespace ProjectManager.IntegrationTests.Infrastructure;

/// <summary>Organização ativa fixa, para testes de persistência sem a camada HTTP.</summary>
internal sealed class TestTenantContext(Guid? organizationId) : ITenantContext
{
    public Guid? OrganizationId { get; } = organizationId;

    public ActiveMembership? Membership => null;
}
