using ProjectManager.Application.Abstractions.MultiTenancy;

namespace ProjectManager.IntegrationTests.Infrastructure;

internal sealed class TestTenantContext(Guid? organizationId) : ITenantContext
{
    public Guid? OrganizationId { get; } = organizationId;
}
