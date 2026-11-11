using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Common;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Projects;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Projects;

/// <summary>
/// Isolamento entre organizações das entidades de projeto: pela API (404 sem revelar o projeto), pelo
/// filtro de leitura, pela validação de gravação e pelas FKs compostas no banco.
/// </summary>
public sealed class ProjectIsolationTests : IDisposable
{
    private readonly PostgresDatabaseFixture _database;
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly ProjectTestKit _kit;

    public ProjectIsolationTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _database = database;
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _client = _factory.CreateClient();
        _kit = new ProjectTestKit(_client, mailpit);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OwnerOfAnotherOrganization_GetsNotFoundOnEveryProjectEndpoint()
    {
        var alice = await _kit.Organizations.CreateUserAsync();
        var bruno = await _kit.Organizations.CreateUserAsync();
        var aliceOrganization = await _kit.Organizations.CreateOrganizationAsync(alice);
        var brunoOrganization = await _kit.Organizations.CreateOrganizationAsync(bruno);
        var project = await _kit.CreateProjectAsync(alice, aliceOrganization.Id, new { name = "Privado" });
        using var repositoryResponse = await _kit.SendToProjectAsync(
            HttpMethod.Post, project.Id, "/repositories", alice, aliceOrganization.Id, new { url = "https://github.com/alice/privado" });
        var repositoryId = (await ProjectTestKit.ReadAsync<ProjectRepositoryDetails>(repositoryResponse)).Id;

        (HttpMethod Method, string SubPath, object? Body)[] requests =
        [
            (HttpMethod.Get, "", null),
            (HttpMethod.Patch, "", new { name = "Invadido" }),
            (HttpMethod.Delete, "", null),
            (HttpMethod.Get, "/members", null),
            (HttpMethod.Post, "/members", new { userId = bruno.Id, role = "Manager" }),
            (HttpMethod.Patch, $"/members/{alice.Id}", new { role = "Viewer" }),
            (HttpMethod.Delete, $"/members/{alice.Id}", null),
            (HttpMethod.Get, "/repositories", null),
            (HttpMethod.Post, "/repositories", new { url = "https://github.com/bruno/x" }),
            (HttpMethod.Delete, $"/repositories/{repositoryId}", null),
        ];

        // O Bruno tenta com a própria organização no cabeçalho e com a organização da Alice.
        foreach (var organizationId in new[] { brunoOrganization.Id, aliceOrganization.Id })
        {
            foreach (var (method, subPath, body) in requests)
            {
                using var response = await _kit.SendToProjectAsync(method, project.Id, subPath, bruno, organizationId, body);
                Assert.True(
                    response.StatusCode == HttpStatusCode.NotFound,
                    $"{method} {subPath} (organização {organizationId}): esperado 404, obtido {response.StatusCode}.");
            }
        }

        using var brunoList = await _kit.ListAsync(bruno, brunoOrganization.Id);
        using var brunoListsAlice = await _kit.ListAsync(bruno, aliceOrganization.Id);
        using var stillPrivate = await _kit.SendToProjectAsync(HttpMethod.Get, project.Id, "", alice, aliceOrganization.Id);

        Assert.Empty((await ProjectTestKit.ReadAsync<PagedResult<ProjectSummary>>(brunoList)).Items);
        Assert.Equal(HttpStatusCode.NotFound, brunoListsAlice.StatusCode);
        Assert.Equal("Privado", (await ProjectTestKit.ReadAsync<ProjectDetails>(stillPrivate)).Name);
    }

    [Fact]
    public async Task ReadFilter_HidesTeamsTagsAndRepositoriesOfOtherOrganizations()
    {
        var owner = await _kit.Organizations.CreateUserAsync();
        var organization = await _kit.Organizations.CreateOrganizationAsync(owner);
        var otherOrganization = await _kit.Organizations.CreateOrganizationAsync(owner);
        var project = await _kit.CreateProjectAsync(owner, organization.Id, new { name = "Com dados", tags = new List<string> { "segredo" } });
        using (var add = await _kit.SendToProjectAsync(HttpMethod.Post, project.Id, "/repositories", owner, organization.Id, new { url = "https://github.com/o/r" }))
        {
            add.EnsureSuccessStatusCode();
        }

        await using var own = _database.CreateContext(organization.Id);
        await using var other = _database.CreateContext(otherOrganization.Id);
        await using var none = _database.CreateContext(organizationId: null);

        Assert.Equal(1, await own.ProjectMembers.CountAsync(m => m.ProjectId == project.Id, CancellationToken));
        Assert.Equal(1, await own.ProjectTags.CountAsync(t => t.ProjectId == project.Id, CancellationToken));
        Assert.Equal(1, await own.ProjectRepositories.CountAsync(r => r.ProjectId == project.Id, CancellationToken));
        foreach (var context in new[] { other, none })
        {
            Assert.False(await context.Projects.AnyAsync(p => p.Id == project.Id, CancellationToken));
            Assert.False(await context.ProjectMembers.AnyAsync(m => m.ProjectId == project.Id, CancellationToken));
            Assert.False(await context.ProjectTags.AnyAsync(t => t.ProjectId == project.Id, CancellationToken));
            Assert.False(await context.ProjectRepositories.AnyAsync(r => r.ProjectId == project.Id, CancellationToken));
        }
    }

    [Fact]
    public async Task WriteValidation_BlocksTeamChangesFromAnotherOrganization()
    {
        var owner = await _kit.Organizations.CreateUserAsync();
        var organization = await _kit.Organizations.CreateOrganizationAsync(owner);
        var otherOrganization = await _kit.Organizations.CreateOrganizationAsync(owner);
        var projectDetails = await _kit.CreateProjectAsync(owner, organization.Id);

        await using var own = _database.CreateContext(organization.Id);
        var project = await own.Projects.SingleAsync(p => p.Id == projectDetails.Id, CancellationToken);

        await using var other = _database.CreateContext(otherOrganization.Id);
        other.ProjectMembers.Add(new ProjectMember(project, owner.Id, ProjectRole.Viewer));

        await Assert.ThrowsAsync<CrossTenantAccessException>(() => other.SaveChangesAsync(CancellationToken));
    }

    [Fact]
    public async Task CompositeForeignKeys_RejectLinksAcrossOrganizations()
    {
        var alice = await _kit.Organizations.CreateUserAsync();
        var bruno = await _kit.Organizations.CreateUserAsync();
        var aliceOrganization = await _kit.Organizations.CreateOrganizationAsync(alice);
        var brunoOrganization = await _kit.Organizations.CreateOrganizationAsync(bruno);
        var aliceProject = await _kit.CreateProjectAsync(alice, aliceOrganization.Id);

        // Projeto da Alice com a organização do Bruno: (organization_id, project_id) não existe.
        var projectFromAnotherOrganization = await InsertTeamMemberAsync(brunoOrganization.Id, aliceProject.Id, bruno.Id);
        // Bruno na equipe da Alice sem ser membro da organização dela: (organization_id, user_id) não existe.
        var userOutsideTheOrganization = await InsertTeamMemberAsync(aliceOrganization.Id, aliceProject.Id, bruno.Id);
        var tagFromAnotherOrganization = await ExecuteAsync(
            "INSERT INTO project_tags (project_id, normalized_name, organization_id, name) VALUES (@project, 'X', @organization, 'x')",
            ("project", aliceProject.Id),
            ("organization", brunoOrganization.Id));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, projectFromAnotherOrganization);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, userOutsideTheOrganization);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, tagFromAnotherOrganization);
    }

    private Task<string?> InsertTeamMemberAsync(Guid organizationId, Guid projectId, Guid userId) =>
        ExecuteAsync(
            """
            INSERT INTO project_members (id, organization_id, project_id, user_id, role, is_responsible, created_at, updated_at)
            VALUES (gen_random_uuid(), @organization, @project, @user, 'Viewer', false, now(), now())
            """,
            ("organization", organizationId),
            ("project", projectId),
            ("user", userId));

    /// <summary>SQL direto, sem a validação da aplicação: só o banco decide. Retorna o SQLSTATE do erro.</summary>
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

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
