using ProjectManager.Domain.Organizations;
using ProjectManager.Domain.Projects;

namespace ProjectManager.IntegrationTests.Infrastructure;

/// <summary>
/// Cria dados persistidos pelo mesmo caminho da aplicação (DbContext com a organização ativa).
/// </summary>
internal static class TestData
{
    public static async Task<Organization> CreateOrganizationAsync(PostgresDatabaseFixture database, string name)
    {
        var organization = new Organization(name);

        await using var context = database.CreateContext(organizationId: null);
        context.Organizations.Add(organization);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return organization;
    }

    public static async Task<Project> CreateProjectAsync(
        PostgresDatabaseFixture database,
        Organization organization,
        string name,
        params string[] phaseNames)
    {
        var project = new Project(organization.Id, name);
        foreach (var phaseName in phaseNames)
        {
            project.AddPhase(phaseName);
        }

        await using var context = database.CreateContext(organization.Id);
        context.Projects.Add(project);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return project;
    }
}
