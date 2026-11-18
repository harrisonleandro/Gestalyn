using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Domain.Agents;
using ProjectManager.Domain.Organizations;
using ProjectManager.Domain.Projects;
using ProjectManager.Infrastructure.Persistence;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Agents;

/// <summary>
/// Persistência de agentes (ADR-016): migração da permissão nova, filtro e gravação por organização, FKs compostas
/// e as restrições do banco (papel, escopos, hash e nome únicos).
/// </summary>
public sealed class AgentPersistenceTests(PostgresDatabaseFixture database)
{
    private const string MigrationBeforeAgents = "20261115030314_AddStatusHistoryCategory";

    private static readonly string[] Scopes = [AgentScopes.ContextRead, AgentScopes.TasksWrite];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExistingOwnersAndAdministrators_ReceiveAgentsManage()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(database.ConnectionString)
        {
            Database = $"migration_{Guid.NewGuid():N}",
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<ProjectManagerDbContext>();
        ProjectManagerDatabase.Configure(options, connectionString);
        await using var context = new ProjectManagerDbContext(options.Options, new TestTenantContext(organizationId: null), TimeProvider.System);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeAgents, cancellationToken: CancellationToken);

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
            where role.OrganizationId == organizationId && permission.PermissionCode == OrganizationPermissions.AgentsManage
            select role.Name).ToListAsync(CancellationToken);

        Assert.Equal(["Administrador", "Proprietário"], granted.Order(StringComparer.Ordinal));

        await context.Database.EnsureDeletedAsync(CancellationToken);
    }

    [Fact]
    public async Task ReadFilter_HidesAgentsCredentialsAndAccessOfOtherOrganizations()
    {
        var own = await CreateAgentWithCredentialAndAccessAsync("Org agentes A");
        var other = await TestData.CreateOrganizationAsync(database, "Org agentes B");

        await using var ownContext = database.CreateContext(own.OrganizationId);
        await using var otherContext = database.CreateContext(other.Id);
        await using var noContext = database.CreateContext(organizationId: null);

        Assert.True(await ownContext.Agents.AnyAsync(a => a.Id == own.AgentId, CancellationToken));
        Assert.True(await ownContext.AgentCredentials.AnyAsync(c => c.Id == own.CredentialId, CancellationToken));
        Assert.True(await ownContext.AgentProjectAccess.AnyAsync(a => a.AgentId == own.AgentId, CancellationToken));
        foreach (var context in new[] { otherContext, noContext })
        {
            Assert.False(await context.Agents.AnyAsync(a => a.Id == own.AgentId, CancellationToken));
            Assert.False(await context.AgentCredentials.AnyAsync(c => c.Id == own.CredentialId, CancellationToken));
            Assert.False(await context.AgentProjectAccess.AnyAsync(a => a.AgentId == own.AgentId, CancellationToken));
        }
    }

    [Fact]
    public async Task Credential_RoundTripsScopesAndHash()
    {
        var own = await CreateAgentWithCredentialAndAccessAsync("Org agentes leitura");

        await using var context = database.CreateContext(own.OrganizationId);
        var credential = await context.AgentCredentials.SingleAsync(c => c.Id == own.CredentialId, CancellationToken);

        Assert.Equal(Scopes, credential.Scopes);
        Assert.Equal(own.Hash, credential.TokenHash);
        Assert.Equal("ABCDEFGH", credential.Prefix);
    }

    [Fact]
    public async Task Insert_ForAnotherOrganization_IsBlocked()
    {
        var userId = await TestData.CreateUserAsync(database);
        var organizationA = await TestData.CreateOrganizationAsync(database, "Org agentes inserção A");
        var organizationB = await TestData.CreateOrganizationAsync(database, "Org agentes inserção B");
        var intruder = new Agent(organizationB.Id, "Intruso", null, userId);

        await using (var context = database.CreateContext(organizationA.Id))
        {
            context.Agents.Add(intruder);
            await Assert.ThrowsAsync<CrossTenantAccessException>(() => context.SaveChangesAsync(CancellationToken));
        }

        await using var verification = database.CreateContext(organizationB.Id);
        Assert.False(await verification.Agents.AnyAsync(a => a.Id == intruder.Id, CancellationToken));
    }

    [Fact]
    public async Task CompositeForeignKeys_KeepCredentialsAndAccessInsideTheAgentOrganization()
    {
        var own = await CreateAgentWithCredentialAndAccessAsync("Org agentes FK A");
        var foreign = await CreateAgentWithCredentialAndAccessAsync("Org agentes FK B");

        var credentialForForeignAgent = await ExecuteAsync(
            """
            INSERT INTO agent_credentials (id, organization_id, agent_id, name, token_hash, prefix, scopes, expires_at, created_by_user_id, created_at, updated_at)
            VALUES (gen_random_uuid(), @organization, @agent, 'x', @hash, 'ZZZZZZZZ', ARRAY['tasks:read'], now() + interval '1 day', @user, now(), now())
            """,
            ("organization", own.OrganizationId), ("agent", foreign.AgentId), ("hash", NewHash()), ("user", own.UserId));
        var accessToForeignProject = await ExecuteAsync(
            """
            INSERT INTO agent_project_access (id, organization_id, agent_id, project_id, role, granted_by_user_id, created_at, updated_at)
            VALUES (gen_random_uuid(), @organization, @agent, @project, 'Viewer', @user, now(), now())
            """,
            ("organization", own.OrganizationId), ("agent", own.AgentId), ("project", foreign.ProjectId), ("user", own.UserId));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, credentialForForeignAgent);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, accessToForeignProject);
    }

    [Fact]
    public async Task Database_RejectsManagerRole_EmptyScopes_DuplicateHash_AndDuplicateName()
    {
        var own = await CreateAgentWithCredentialAndAccessAsync("Org agentes restrições");

        var managerRole = await ExecuteAsync(
            "UPDATE agent_project_access SET role = 'Manager' WHERE agent_id = @agent",
            ("agent", own.AgentId));
        var emptyScopes = await ExecuteAsync(
            "UPDATE agent_credentials SET scopes = ARRAY[]::text[] WHERE id = @credential",
            ("credential", own.CredentialId));
        var duplicateHash = await ExecuteAsync(
            """
            INSERT INTO agent_credentials (id, organization_id, agent_id, name, token_hash, prefix, scopes, expires_at, created_by_user_id, created_at, updated_at)
            VALUES (gen_random_uuid(), @organization, @agent, 'Cópia', @hash, 'ZZZZZZZZ', ARRAY['tasks:read'], now() + interval '1 day', @user, now(), now())
            """,
            ("organization", own.OrganizationId), ("agent", own.AgentId), ("hash", own.Hash), ("user", own.UserId));

        await using var context = database.CreateContext(own.OrganizationId);
        context.Agents.Add(new Agent(own.OrganizationId, "AGENTE DE TESTE", null, own.UserId));
        var duplicateName = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(CancellationToken));

        Assert.Equal(PostgresErrorCodes.CheckViolation, managerRole);
        Assert.Equal(PostgresErrorCodes.CheckViolation, emptyScopes);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateHash);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicateName.InnerException).SqlState);
    }

    private static string NewHash() => Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());

    private async Task<SeededAgent> CreateAgentWithCredentialAndAccessAsync(string organizationName)
    {
        var userId = await TestData.CreateUserAsync(database);
        var organization = await TestData.CreateOrganizationAsync(database, organizationName);
        var project = await TestData.CreateProjectAsync(database, organization, "Projeto");
        var agent = new Agent(organization.Id, "Agente de teste", "Revisa código", userId);
        var hash = NewHash();
        var now = DateTimeOffset.UtcNow;
        var credential = new AgentCredential(agent, "CI", Scopes, hash, "ABCDEFGH", now.AddDays(90), userId, now);

        await using var context = database.CreateContext(organization.Id);
        context.Agents.Add(agent);
        context.AgentCredentials.Add(credential);
        context.AgentProjectAccess.Add(new AgentProjectAccess(agent, project, ProjectRole.Contributor, userId));
        await context.SaveChangesAsync(CancellationToken);

        return new SeededAgent(organization.Id, userId, agent.Id, credential.Id, project.Id, hash);
    }

    private async Task<string?> ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
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

    private sealed record SeededAgent(Guid OrganizationId, Guid UserId, Guid AgentId, Guid CredentialId, Guid ProjectId, string Hash);
}
