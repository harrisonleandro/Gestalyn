using ProjectManager.Domain.Organizations;
using ProjectManager.Domain.Projects;
using ProjectManager.Infrastructure.Identity;

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

    /// <summary>Usuário mínimo, para autorias com FK para <c>users</c>.</summary>
    public static async Task<Guid> CreateUserAsync(PostgresDatabaseFixture database)
    {
        var user = new ApplicationUser { DisplayName = "Pessoa de teste" };

        await using var context = database.CreateContext(organizationId: null);
        context.Users.Add(user);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return user.Id;
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
