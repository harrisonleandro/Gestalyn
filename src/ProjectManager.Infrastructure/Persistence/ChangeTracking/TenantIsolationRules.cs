using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Organizations;

namespace ProjectManager.Infrastructure.Persistence.ChangeTracking;

/// <summary>
/// Bloqueia gravações fora da organização ativa. Criar uma nova organização é permitido; alterar ou
/// excluir uma organização exige que ela seja a ativa.
/// </summary>
internal static class TenantIsolationRules
{
    public static void Enforce(ChangeTracker changeTracker, Guid? currentOrganizationId)
    {
        foreach (var entry in changeTracker.Entries())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            switch (entry.Entity)
            {
                case IOrganizationOwned owned:
                    EnsureActiveOrganization(owned.OrganizationId, currentOrganizationId, entry);
                    EnsureOrganizationUnchanged(entry);
                    break;

                case Organization organization when entry.State is not EntityState.Added:
                    EnsureActiveOrganization(organization.Id, currentOrganizationId, entry);
                    break;
            }
        }
    }

    private static void EnsureActiveOrganization(Guid organizationId, Guid? currentOrganizationId, EntityEntry entry)
    {
        if (currentOrganizationId is null || organizationId != currentOrganizationId)
        {
            throw new CrossTenantAccessException(
                $"Gravação de '{entry.Metadata.ClrType.Name}' bloqueada: a entidade não pertence à organização ativa.");
        }
    }

    private static void EnsureOrganizationUnchanged(EntityEntry entry)
    {
        if (entry.State is not EntityState.Modified)
        {
            return;
        }

        var organizationId = entry.Property(nameof(IOrganizationOwned.OrganizationId));
        if (!Equals(organizationId.OriginalValue, organizationId.CurrentValue))
        {
            throw new CrossTenantAccessException(
                $"Gravação de '{entry.Metadata.ClrType.Name}' bloqueada: a organização de uma entidade não pode ser alterada.");
        }
    }
}
