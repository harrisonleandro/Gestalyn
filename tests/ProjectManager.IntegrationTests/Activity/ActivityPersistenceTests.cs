using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ProjectManager.Infrastructure.Persistence;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Tasks;

namespace ProjectManager.IntegrationTests.Activity;

/// <summary>
/// Persistência das atividades (ADR-014): migração da permissão nova para os papéis existentes, filtro de leitura por
/// organização e FKs compostas que mantêm cada registro no próprio projeto.
/// </summary>
public sealed class ActivityPersistenceTests : IDisposable
{
    private const string MigrationBeforeActivity = "20261115012750_AddWorkflows";

    private readonly PostgresDatabaseFixture _database;
    private readonly ApiFactory _factory;
    private readonly TaskTestKit _kit;

    public ActivityPersistenceTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _database = database;
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _kit = new TaskTestKit(_factory.CreateClient(), mailpit);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExistingOwnersAndAdministrators_ReceiveActivityReadAll()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_database.ConnectionString)
        {
            Database = $"migration_{Guid.NewGuid():N}",
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<ProjectManagerDbContext>();
        ProjectManagerDatabase.Configure(options, connectionString);
        await using var context = new ProjectManagerDbContext(options.Options, new TestTenantContext(organizationId: null), TimeProvider.System);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeActivity, cancellationToken: CancellationToken);

        var organizationId = Guid.NewGuid();
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
                    (gen_random_uuid(), @organization, 'Leitor', 'Viewer', now(), now()),
                    (gen_random_uuid(), @organization, 'Personalizado', NULL, now(), now());
                """,
                connection);
            seed.Parameters.AddWithValue("organization", organizationId);
            await seed.ExecuteNonQueryAsync(CancellationToken);
        }

        await migrator.MigrateAsync(cancellationToken: CancellationToken);

        var granted = await (
            from permission in context.RolePermissions.IgnoreQueryFilters()
            join role in context.Roles.IgnoreQueryFilters() on permission.RoleId equals role.Id
            where role.OrganizationId == organizationId && permission.PermissionCode == "activity.read_all"
            select role.Name).ToListAsync(CancellationToken);

        Assert.Equal(["Administrador", "Proprietário"], granted.Order(StringComparer.Ordinal));

        await context.Database.EnsureDeletedAsync(CancellationToken);
    }

    [Fact]
    public async Task ReadFilter_HidesActivitiesOfOtherOrganizations()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var otherOrganization = await _kit.Organizations.CreateOrganizationAsync(workspace.Owner);
        using (var log = await _kit.SendToProjectAsync(HttpMethod.Post, workspace, "/activities", workspace.Owner, new { kind = "Note", summary = "Nota" }))
        {
            log.EnsureSuccessStatusCode();
        }

        await using var own = _database.CreateContext(workspace.OrganizationId);
        await using var other = _database.CreateContext(otherOrganization.Id);

        Assert.True(await own.ActivityEvents.AnyAsync(e => e.ProjectId == workspace.ProjectId, CancellationToken));
        Assert.True(await own.ActivityLogs.AnyAsync(l => l.ProjectId == workspace.ProjectId, CancellationToken));
        Assert.False(await other.ActivityEvents.AnyAsync(e => e.ProjectId == workspace.ProjectId, CancellationToken));
        Assert.False(await other.ActivityLogs.AnyAsync(l => l.ProjectId == workspace.ProjectId, CancellationToken));
        Assert.False(await other.ActivityEvents.AnyAsync(e => e.OrganizationId == workspace.OrganizationId, CancellationToken));
    }

    [Fact]
    public async Task CompositeForeignKeys_KeepActivitiesInsideTheirProjectAndOrganization()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var otherProject = await _kit.CreateSecondProjectAsync(workspace);
        var foreignTask = await _kit.CreateTaskAsync(new Workspace(workspace.Owner, workspace.OrganizationId, otherProject.Id), "Fora");
        var otherWorkspace = await _kit.CreateWorkspaceAsync("Outra organização");

        var eventWithTaskOfOtherProject = await ExecuteAsync(
            """
            INSERT INTO activity_events (id, organization_id, project_id, task_id, actor_kind, origin, action, entity_type, entity_id, summary, occurred_at)
            VALUES (gen_random_uuid(), @organization, @project, @task, 'User', 'Api', 'task.updated', 'task', @task, 'x', now())
            """,
            ("organization", workspace.OrganizationId), ("project", workspace.ProjectId), ("task", foreignTask.Id));
        var logInProjectOfOtherOrganization = await ExecuteAsync(
            """
            INSERT INTO activity_logs (id, organization_id, project_id, actor_kind, origin, kind, summary, occurred_at, recorded_at)
            VALUES (gen_random_uuid(), @organization, @project, 'User', 'Api', 'Note', 'x', now(), now())
            """,
            ("organization", workspace.OrganizationId), ("project", otherWorkspace.ProjectId));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, eventWithTaskOfOtherProject);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, logInProjectOfOtherOrganization);
    }

    public void Dispose() => _factory.Dispose();

    private async Task<string?> ExecuteAsync(string sql, params (string Name, Guid Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        try
        {
            await command.ExecuteNonQueryAsync(CancellationToken);
            return null;
        }
        catch (PostgresException exception)
        {
            return exception.SqlState;
        }
    }
}
