using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Persistence;

public sealed class PersistenceTests(PostgresDatabaseFixture database)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migrations_AreFullyAppliedAndMatchTheModel()
    {
        await using var context = database.CreateContext(organizationId: null);

        var pending = await context.Database.GetPendingMigrationsAsync(CancellationToken);
        var applied = await context.Database.GetAppliedMigrationsAsync(CancellationToken);

        Assert.Empty(pending);
        Assert.Equal(context.Database.GetMigrations(), applied);
        Assert.False(context.Database.HasPendingModelChanges(), "O modelo tem alterações sem migration correspondente.");
    }

    [Fact]
    public async Task Project_WithPhases_RoundTripsInPositionOrder()
    {
        var organization = await TestData.CreateOrganizationAsync(database, "Org round-trip");
        var project = await TestData.CreateProjectAsync(database, organization, "Projeto", "Descoberta", "Construção", "Entrega");

        await using var context = database.CreateContext(organization.Id);
        var loaded = await context.Projects
            .Include(p => p.Phases)
            .SingleAsync(p => p.Id == project.Id, CancellationToken);

        Assert.Equal("Projeto", loaded.Name);
        Assert.Equal(organization.Id, loaded.OrganizationId);
        Assert.Equal(["Descoberta", "Construção", "Entrega"], loaded.Phases.Select(phase => phase.Name));
        Assert.Equal([1, 2, 3], loaded.Phases.Select(phase => phase.Position));
        Assert.All(loaded.Phases, phase => Assert.Equal(organization.Id, phase.OrganizationId));
    }

    [Fact]
    public async Task Auditing_SetsTimestampsOnInsertAndOnlyUpdatedAtOnUpdate()
    {
        var createdAt = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
        var updatedAt = createdAt.AddHours(5);
        var clock = new ManualTimeProvider(createdAt);
        var organization = await TestData.CreateOrganizationAsync(database, "Org auditoria");

        var project = new Domain.Projects.Project(organization.Id, "Auditado");
        await using (var context = database.CreateContext(organization.Id, clock))
        {
            context.Projects.Add(project);
            await context.SaveChangesAsync(CancellationToken);
        }

        clock.UtcNow = updatedAt;
        await using (var context = database.CreateContext(organization.Id, clock))
        {
            var tracked = await context.Projects.SingleAsync(p => p.Id == project.Id, CancellationToken);
            tracked.Rename("Auditado e renomeado");
            await context.SaveChangesAsync(CancellationToken);
        }

        await using var verification = database.CreateContext(organization.Id);
        var stored = await verification.Projects.AsNoTracking().SingleAsync(p => p.Id == project.Id, CancellationToken);
        Assert.Equal(createdAt, stored.CreatedAt);
        Assert.Equal(updatedAt, stored.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, stored.UpdatedAt.Offset);
    }

    [Fact]
    public async Task ConcurrentUpdate_IsRejectedByConcurrencyToken()
    {
        var organization = await TestData.CreateOrganizationAsync(database, "Org concorrência");
        var project = await TestData.CreateProjectAsync(database, organization, "Original");

        await using var first = database.CreateContext(organization.Id);
        await using var second = database.CreateContext(organization.Id);
        var firstCopy = await first.Projects.SingleAsync(p => p.Id == project.Id, CancellationToken);
        var secondCopy = await second.Projects.SingleAsync(p => p.Id == project.Id, CancellationToken);

        firstCopy.Rename("Primeira alteração");
        await first.SaveChangesAsync(CancellationToken);

        secondCopy.Rename("Alteração concorrente");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(CancellationToken));
    }

    [Fact]
    public async Task DeletingOrganizationWithProjects_IsRestrictedByDatabase()
    {
        var organization = await TestData.CreateOrganizationAsync(database, "Org exclusão");
        await TestData.CreateProjectAsync(database, organization, "Projeto dependente");

        await using var context = database.CreateContext(organization.Id);
        var tracked = await context.Organizations.SingleAsync(o => o.Id == organization.Id, CancellationToken);
        context.Organizations.Remove(tracked);

        // ON DELETE RESTRICT gera restrict_violation (23001), não foreign_key_violation (23503).
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(CancellationToken));
        AssertPostgresError(exception.InnerException, PostgresErrorCodes.RestrictViolation);
    }

    [Fact]
    public async Task DuplicatePhasePosition_IsRejectedByUniqueIndex()
    {
        var organization = await TestData.CreateOrganizationAsync(database, "Org posição duplicada");
        var project = await TestData.CreateProjectAsync(database, organization, "Projeto", "Fase 1");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertPhaseWithSqlAsync(organization.Id, project.Id, position: 1));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task NonPositivePhasePosition_IsRejectedByCheckConstraint()
    {
        var organization = await TestData.CreateOrganizationAsync(database, "Org posição inválida");
        var project = await TestData.CreateProjectAsync(database, organization, "Projeto");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertPhaseWithSqlAsync(organization.Id, project.Id, position: 0));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    private async Task InsertPhaseWithSqlAsync(Guid organizationId, Guid projectId, int position)
    {
        await using var context = database.CreateContext(organizationId: null);
        var now = DateTimeOffset.UtcNow;

        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO project_phases (id, organization_id, project_id, name, position, created_at, updated_at)
            VALUES ({Guid.CreateVersion7()}, {organizationId}, {projectId}, {"Inserida via SQL"}, {position}, {now}, {now})
            """,
            CancellationToken);
    }

    private static void AssertPostgresError(Exception? exception, string expectedSqlState)
    {
        var postgresException = Assert.IsType<PostgresException>(exception);
        Assert.Equal(expectedSqlState, postgresException.SqlState);
    }
}
