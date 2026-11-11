using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ProjectManager.Domain.Common;

namespace ProjectManager.Infrastructure.Persistence.ChangeTracking;

/// <summary>
/// Preenche as datas de auditoria (UTC). A data de criação nunca é alterada depois da inserção.
/// </summary>
internal static class AuditingRules
{
    public static void Apply(ChangeTracker changeTracker, DateTimeOffset now)
    {
        foreach (var entry in changeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(entity => entity.CreatedAt).CurrentValue = now;
                    entry.Property(entity => entity.UpdatedAt).CurrentValue = now;
                    break;

                case EntityState.Modified:
                    entry.Property(entity => entity.CreatedAt).IsModified = false;
                    entry.Property(entity => entity.UpdatedAt).CurrentValue = now;
                    break;
            }
        }
    }
}
