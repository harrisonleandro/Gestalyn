using System.Reflection;
using Microsoft.EntityFrameworkCore;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Organizations;
using ProjectManager.Domain.Projects;
using ProjectManager.Infrastructure.Persistence.ChangeTracking;

namespace ProjectManager.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência. Toda consulta a dados de organizações é filtrada pela organização ativa,
/// e toda gravação é validada contra ela antes de chegar ao banco.
/// </summary>
public sealed class ProjectManagerDbContext(
    DbContextOptions<ProjectManagerDbContext> options,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : DbContext(options)
{
    private static readonly MethodInfo ApplyOrganizationFilterMethod =
        typeof(ProjectManagerDbContext).GetMethod(nameof(ApplyOrganizationFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectPhase> ProjectPhases => Set<ProjectPhase>();

    // Lido pelo EF Core a cada consulta: os filtros globais usam o valor do momento da execução.
    private Guid? CurrentOrganizationId => tenantContext.OrganizationId;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProjectManagerDbContext).Assembly);

        // A própria organização só é visível quando é a organização ativa.
        modelBuilder.Entity<Organization>().HasQueryFilter(organization => organization.Id == CurrentOrganizationId);

        var organizationOwnedTypes = modelBuilder.Model.GetEntityTypes()
            .Select(entityType => entityType.ClrType)
            .Where(clrType => typeof(IOrganizationOwned).IsAssignableFrom(clrType))
            .ToList();

        foreach (var clrType in organizationOwnedTypes)
        {
            ApplyOrganizationFilterMethod.MakeGenericMethod(clrType).Invoke(this, [modelBuilder]);
        }
    }

    private void ApplyOrganizationFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IOrganizationOwned =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity => entity.OrganizationId == CurrentOrganizationId);

    private void PrepareChanges()
    {
        ChangeTracker.DetectChanges();
        TenantIsolationRules.Enforce(ChangeTracker, CurrentOrganizationId);
        AuditingRules.Apply(ChangeTracker, timeProvider.GetUtcNow());
    }
}
