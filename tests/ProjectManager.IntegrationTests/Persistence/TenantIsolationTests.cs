using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Projects;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Persistence;

/// <summary>
/// Isolamento multi-tenant: leituras filtradas, gravações bloqueadas na aplicação e associações
/// entre organizações bloqueadas pelo próprio banco.
/// </summary>
public sealed class TenantIsolationTests(PostgresDatabaseFixture database)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Queries_ReturnOnlyDataOfTheActiveOrganization()
    {
        var (organizationA, projectA) = await CreateOrganizationWithProjectAsync("Org A leitura");
        var (organizationB, projectB) = await CreateOrganizationWithProjectAsync("Org B leitura");

        await using var context = database.CreateContext(organizationA.Id);

        var organizationIds = await context.Organizations.Select(o => o.Id).ToListAsync(CancellationToken);
        var projects = await context.Projects.ToListAsync(CancellationToken);
        var phases = await context.ProjectPhases.ToListAsync(CancellationToken);

        Assert.Equal([organizationA.Id], organizationIds);
        Assert.Contains(projects, p => p.Id == projectA.Id);
        Assert.DoesNotContain(projects, p => p.Id == projectB.Id);
        Assert.All(projects, p => Assert.Equal(organizationA.Id, p.OrganizationId));
        Assert.NotEmpty(phases);
        Assert.All(phases, phase => Assert.Equal(organizationA.Id, phase.OrganizationId));
        Assert.Null(await context.Projects.FindAsync([projectB.Id], CancellationToken));
        Assert.False(await context.Organizations.AnyAsync(o => o.Id == organizationB.Id, CancellationToken));
    }

    [Fact]
    public async Task Queries_WithoutActiveOrganization_ReturnNoOrganizationData()
    {
        await CreateOrganizationWithProjectAsync("Org sem contexto");

        await using var context = database.CreateContext(organizationId: null);

        Assert.False(await context.Organizations.AnyAsync(CancellationToken));
        Assert.False(await context.Projects.AnyAsync(CancellationToken));
        Assert.False(await context.ProjectPhases.AnyAsync(CancellationToken));
    }

    [Fact]
    public async Task Insert_ForAnotherOrganization_IsBlocked()
    {
        var (organizationA, _) = await CreateOrganizationWithProjectAsync("Org A inserção");
        var (organizationB, _) = await CreateOrganizationWithProjectAsync("Org B inserção");
        var intruder = new Project(organizationB.Id, "Projeto intruso");

        await using (var context = database.CreateContext(organizationA.Id))
        {
            context.Projects.Add(intruder);
            await Assert.ThrowsAsync<CrossTenantAccessException>(() => context.SaveChangesAsync(CancellationToken));
        }

        await using var verification = database.CreateContext(organizationB.Id);
        Assert.False(await verification.Projects.AnyAsync(p => p.Id == intruder.Id, CancellationToken));
    }

    [Fact]
    public async Task Insert_WithoutActiveOrganization_IsBlocked()
    {
        var (organization, _) = await CreateOrganizationWithProjectAsync("Org inserção sem contexto");

        await using var context = database.CreateContext(organizationId: null);
        context.Projects.Add(new Project(organization.Id, "Sem contexto"));

        await Assert.ThrowsAsync<CrossTenantAccessException>(() => context.SaveChangesAsync(CancellationToken));
    }

    [Fact]
    public async Task Update_OfAnotherOrganizationEntity_IsBlocked()
    {
        var (organizationA, _) = await CreateOrganizationWithProjectAsync("Org A alteração");
        var (organizationB, projectB) = await CreateOrganizationWithProjectAsync("Org B alteração");

        await using (var context = database.CreateContext(organizationA.Id))
        {
            // Simula uma entidade obtida por outro caminho (ex.: identificador enviado pelo cliente).
            context.Projects.Attach(projectB);
            projectB.Rename("Alterado por outra organização");

            await Assert.ThrowsAsync<CrossTenantAccessException>(() => context.SaveChangesAsync(CancellationToken));
        }

        await using var verification = database.CreateContext(organizationB.Id);
        var stored = await verification.Projects.AsNoTracking().SingleAsync(p => p.Id == projectB.Id, CancellationToken);
        Assert.Equal("Projeto", stored.Name);
    }

    [Fact]
    public async Task Delete_OfAnotherOrganization_IsBlocked()
    {
        var (organizationA, _) = await CreateOrganizationWithProjectAsync("Org A exclusão");
        var (organizationB, _) = await CreateOrganizationWithProjectAsync("Org B exclusão");

        await using var context = database.CreateContext(organizationA.Id);
        context.Organizations.Remove(organizationB);

        await Assert.ThrowsAsync<CrossTenantAccessException>(() => context.SaveChangesAsync(CancellationToken));
    }

    [Fact]
    public async Task Database_RejectsPhaseLinkedToProjectOfAnotherOrganization()
    {
        var (organizationA, _) = await CreateOrganizationWithProjectAsync("Org A FK composta");
        var (_, projectB) = await CreateOrganizationWithProjectAsync("Org B FK composta");

        // Contorna a aplicação: SQL direto, sem filtros nem validação do DbContext.
        await using var context = database.CreateContext(organizationId: null);
        var now = DateTimeOffset.UtcNow;
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO project_phases (id, organization_id, project_id, name, position, created_at, updated_at)
            VALUES ({Guid.CreateVersion7()}, {organizationA.Id}, {projectB.Id}, {"Fase cruzada"}, {99}, {now}, {now})
            """,
            CancellationToken));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Fact]
    public async Task Model_EveryOrganizationOwnedEntityHasOrganizationQueryFilter()
    {
        await using var context = database.CreateContext(organizationId: null);

        var organizationOwnedTypes = context.Model.GetEntityTypes()
            .Where(entityType => typeof(IOrganizationOwned).IsAssignableFrom(entityType.ClrType))
            .ToList();

        Assert.NotEmpty(organizationOwnedTypes);
        Assert.All(organizationOwnedTypes, entityType =>
            Assert.True(entityType.GetDeclaredQueryFilters().Count > 0, $"{entityType.ClrType.Name} não possui filtro por organização."));
    }

    private async Task<(Domain.Organizations.Organization Organization, Project Project)> CreateOrganizationWithProjectAsync(string name)
    {
        var organization = await TestData.CreateOrganizationAsync(database, name);
        var project = await TestData.CreateProjectAsync(database, organization, "Projeto", "Fase inicial");

        return (organization, project);
    }
}
