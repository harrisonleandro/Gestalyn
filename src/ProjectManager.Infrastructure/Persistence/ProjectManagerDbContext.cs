using System.Reflection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Organizations;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Tasks;
using ProjectManager.Infrastructure.Identity;
using ProjectManager.Infrastructure.Organizations;
using ProjectManager.Infrastructure.Persistence.ChangeTracking;

namespace ProjectManager.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência. Inclui as tabelas de usuários do ASP.NET Core Identity (sem papéis do
/// Identity) e as sessões. Toda consulta a dados de organizações é filtrada pela organização ativa,
/// e toda gravação é validada contra ela antes de chegar ao banco.
/// </summary>
public sealed class ProjectManagerDbContext(
    DbContextOptions<ProjectManagerDbContext> options,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : IdentityUserContext<ApplicationUser, Guid>(options)
{
    private static readonly MethodInfo ApplyOrganizationFilterMethod =
        typeof(ProjectManagerDbContext).GetMethod(nameof(ApplyOrganizationFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectPhase> ProjectPhases => Set<ProjectPhase>();

    public DbSet<Milestone> Milestones => Set<Milestone>();

    public DbSet<ProjectTask> Tasks => Set<ProjectTask>();

    public DbSet<TaskAssignment> TaskAssignments => Set<TaskAssignment>();

    public DbSet<TaskLabel> TaskLabels => Set<TaskLabel>();

    public DbSet<TaskComment> TaskComments => Set<TaskComment>();

    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();

    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();

    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    public DbSet<ProjectTag> ProjectTags => Set<ProjectTag>();

    public DbSet<ProjectRepository> ProjectRepositories => Set<ProjectRepository>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<PermissionDefinition> Permissions => Set<PermissionDefinition>();

    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<UserRefreshToken> UserRefreshTokens => Set<UserRefreshToken>();

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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Modelo do Identity primeiro; as configurações da solução ajustam nomes de tabelas e chaves.
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ProjectManagerDbContext).Assembly);

        // A própria organização só é visível quando é a organização ativa.
        builder.Entity<Organization>().HasQueryFilter(organization => organization.Id == CurrentOrganizationId);

        var organizationOwnedTypes = builder.Model.GetEntityTypes()
            .Select(entityType => entityType.ClrType)
            .Where(clrType => typeof(IOrganizationOwned).IsAssignableFrom(clrType))
            .ToList();

        foreach (var clrType in organizationOwnedTypes)
        {
            ApplyOrganizationFilterMethod.MakeGenericMethod(clrType).Invoke(this, [builder]);
        }
    }

    private void ApplyOrganizationFilter<TEntity>(ModelBuilder builder)
        where TEntity : class, IOrganizationOwned =>
        builder.Entity<TEntity>().HasQueryFilter(entity => entity.OrganizationId == CurrentOrganizationId);

    private void PrepareChanges()
    {
        ChangeTracker.DetectChanges();
        TenantIsolationRules.Enforce(ChangeTracker, CurrentOrganizationId);
        AuditingRules.Apply(ChangeTracker, timeProvider.GetUtcNow());
    }
}
