using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ProjectManager.Infrastructure.Persistence;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Projects;

/// <summary>
/// Migração de dados da Fase 05 (ADR-010): organizações criadas antes dela recebem as permissões de
/// projeto nos papéis de sistema, e projetos antigos ficam com prioridade Média.
/// </summary>
public sealed class ProjectMigrationTests(PostgresDatabaseFixture database)
{
    private const string MigrationBeforeProjects = "20261111202458_AddOrganizationsAndPermissions";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExistingOrganizations_ReceiveProjectPermissions_AndOldProjectsDefaultToMedium()
    {
        // Banco próprio no mesmo contêiner, para migrar até antes da Fase 05.
        var connectionString = new NpgsqlConnectionStringBuilder(database.ConnectionString)
        {
            Database = $"migration_{Guid.NewGuid():N}",
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<ProjectManagerDbContext>();
        ProjectManagerDatabase.Configure(options, connectionString);
        await using var context = new ProjectManagerDbContext(options.Options, new TestTenantContext(organizationId: null), TimeProvider.System);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeProjects, cancellationToken: CancellationToken);

        var organizationId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(CancellationToken);
            await using var seed = new NpgsqlCommand(
                """
                INSERT INTO organizations (id, name, created_at, updated_at) VALUES (@organization, 'Antiga', now(), now());
                INSERT INTO roles (id, organization_id, name, system_role, created_at, updated_at) VALUES
                    (gen_random_uuid(), @organization, 'Proprietário', 'Owner', now(), now()),
                    (gen_random_uuid(), @organization, 'Administrador', 'Administrator', now(), now()),
                    (gen_random_uuid(), @organization, 'Membro', 'Member', now(), now()),
                    (gen_random_uuid(), @organization, 'Leitor', 'Viewer', now(), now());
                INSERT INTO projects (id, organization_id, name, created_at, updated_at) VALUES (@project, @organization, 'Projeto antigo', now(), now());
                """,
                connection);
            seed.Parameters.AddWithValue("organization", organizationId);
            seed.Parameters.AddWithValue("project", projectId);
            await seed.ExecuteNonQueryAsync(CancellationToken);
        }

        await migrator.MigrateAsync(cancellationToken: CancellationToken);

        var granted = await (
            from permission in context.RolePermissions.IgnoreQueryFilters()
            join role in context.Roles.IgnoreQueryFilters() on permission.RoleId equals role.Id
            where role.OrganizationId == organizationId && permission.PermissionCode.StartsWith("projects.")
            select new { role.SystemRole, permission.PermissionCode }).ToListAsync(CancellationToken);
        var priority = await context.Projects.IgnoreQueryFilters()
            .Where(p => p.Id == projectId)
            .Select(p => p.Priority)
            .SingleAsync(CancellationToken);

        string[] all = ["projects.create", "projects.manage_all", "projects.read_all"];
        Assert.Equal(all, PermissionsOf("Owner"));
        Assert.Equal(all, PermissionsOf("Administrator"));
        Assert.Equal(["projects.create"], PermissionsOf("Member"));
        Assert.Empty(PermissionsOf("Viewer"));
        Assert.Equal(Domain.Projects.ProjectPriority.Medium, priority);

        await context.Database.EnsureDeletedAsync(CancellationToken);

        IEnumerable<string> PermissionsOf(string systemRole) =>
            granted.Where(g => g.SystemRole?.ToString() == systemRole).Select(g => g.PermissionCode).Order(StringComparer.Ordinal);
    }
}
