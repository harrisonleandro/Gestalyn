using System.Net;
using Microsoft.EntityFrameworkCore;
using ProjectManager.Application.Common;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Projects;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Organizations;

namespace ProjectManager.IntegrationTests.Projects;

/// <summary>Fluxos de projeto pela API: criação, edição, listagem, arquivamento e repositórios.</summary>
public sealed class ProjectTests : IDisposable
{
    private readonly PostgresDatabaseFixture _database;
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly ProjectTestKit _kit;

    public ProjectTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _database = database;
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _client = _factory.CreateClient();
        _kit = new ProjectTestKit(_client, mailpit);
    }

    [Fact]
    public async Task Create_MakesTheCreatorManager_WithDatesPriorityAndTags()
    {
        var owner = await _kit.Organizations.CreateUserAsync();
        var organization = await _kit.Organizations.CreateOrganizationAsync(owner);

        var project = await _kit.CreateProjectAsync(owner, organization.Id, new
        {
            name = " Portal do cliente ",
            description = "Novo portal",
            priority = "High",
            startDate = "2026-12-01",
            dueDate = "2027-03-31",
            tags = new List<string> { "Web", "web", "Cliente" },
        });

        Assert.Equal("Portal do cliente", project.Name);
        Assert.Equal(Priority.High, project.Priority);
        Assert.Equal(new DateOnly(2026, 12, 1), project.StartDate);
        Assert.Equal(new DateOnly(2027, 3, 31), project.DueDate);
        Assert.Equal(["Cliente", "Web"], project.Tags);
        Assert.Equal(ProjectRole.Manager, project.MyRole);
        Assert.Equal(ProjectPermissions.All.Order(StringComparer.Ordinal), project.MyPermissions);

        using var members = await _kit.SendToProjectAsync(HttpMethod.Get, project.Id, "/members", owner, organization.Id);
        var member = Assert.Single(await ProjectTestKit.ReadAsync<List<ProjectMemberDetails>>(members));
        Assert.Equal((owner.Id, ProjectRole.Manager, false), (member.UserId, member.Role, member.IsResponsible));
    }

    [Fact]
    public async Task Create_DefaultsToMediumPriority_AndRejectsInvalidData()
    {
        var owner = await _kit.Organizations.CreateUserAsync();
        var organization = await _kit.Organizations.CreateOrganizationAsync(owner);

        var project = await _kit.CreateProjectAsync(owner, organization.Id, new { name = "Sem prioridade" });
        using var dueBeforeStart = await _kit.CreateAsync(owner, organization.Id, new { name = "Datas", startDate = "2027-01-10", dueDate = "2027-01-09" });
        using var tooManyTags = await _kit.CreateAsync(owner, organization.Id, new { name = "Etiquetas", tags = Enumerable.Range(1, 21).Select(i => $"t{i}") });
        using var unknownPriority = await _kit.CreateAsync(owner, organization.Id, new { name = "Prioridade", priority = "Urgentíssima" });
        using var blankName = await _kit.CreateAsync(owner, organization.Id, new { name = "" });

        Assert.Equal(Priority.Medium, project.Priority);
        Assert.Equal(HttpStatusCode.BadRequest, dueBeforeStart.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooManyTags.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownPriority.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blankName.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesOnlyTheSentFields_AndNullClearsOptionalValues()
    {
        var owner = await _kit.Organizations.CreateUserAsync();
        var organization = await _kit.Organizations.CreateOrganizationAsync(owner);
        var project = await _kit.CreateProjectAsync(owner, organization.Id, new
        {
            name = "Original",
            description = "Descrição",
            startDate = "2027-01-01",
            dueDate = "2027-02-01",
            tags = new List<string> { "a", "b" },
        });

        using var rename = await _kit.SendToProjectAsync(HttpMethod.Patch, project.Id, "", owner, organization.Id, new { name = "Renomeado" });
        var renamed = await ProjectTestKit.ReadAsync<ProjectDetails>(rename);
        using var clear = await _kit.SendToProjectAsync(HttpMethod.Patch, project.Id, "", owner, organization.Id, new
        {
            description = (string?)null,
            dueDate = (string?)null,
            priority = "Critical",
            tags = new List<string> { "B", "c" },
        });
        var cleared = await ProjectTestKit.ReadAsync<ProjectDetails>(clear);
        using var invalidSchedule = await _kit.SendToProjectAsync(HttpMethod.Patch, project.Id, "", owner, organization.Id, new { dueDate = "2026-12-31" });

        Assert.Equal(("Renomeado", "Descrição", new DateOnly(2027, 2, 1)), (renamed.Name, renamed.Description, renamed.DueDate));
        Assert.Equal("Renomeado", cleared.Name);
        Assert.Null(cleared.Description);
        Assert.Null(cleared.DueDate);
        Assert.Equal(new DateOnly(2027, 1, 1), cleared.StartDate);
        Assert.Equal(Priority.Critical, cleared.Priority);
        Assert.Equal(["B", "c"], cleared.Tags);
        Assert.Equal(HttpStatusCode.BadRequest, invalidSchedule.StatusCode);
    }

    [Fact]
    public async Task Archive_IsLogical_ReadOnly_AndHiddenFromTheDefaultList()
    {
        var owner = await _kit.Organizations.CreateUserAsync();
        var organization = await _kit.Organizations.CreateOrganizationAsync(owner);
        var project = await _kit.CreateProjectAsync(owner, organization.Id, new { name = "Antigo" });

        using var archive = await _kit.SendToProjectAsync(HttpMethod.Delete, project.Id, "", owner, organization.Id);
        using var archiveAgain = await _kit.SendToProjectAsync(HttpMethod.Delete, project.Id, "", owner, organization.Id);
        using var details = await _kit.SendToProjectAsync(HttpMethod.Get, project.Id, "", owner, organization.Id);
        using var edit = await _kit.SendToProjectAsync(HttpMethod.Patch, project.Id, "", owner, organization.Id, new { name = "Novo" });
        using var addRepository = await _kit.SendToProjectAsync(HttpMethod.Post, project.Id, "/repositories", owner, organization.Id, new { url = "https://github.com/x/y" });
        using var changeTeam = await _kit.SendToProjectAsync(HttpMethod.Patch, project.Id, $"/members/{owner.Id}", owner, organization.Id, new { role = "Viewer" });
        using var defaultList = await _kit.ListAsync(owner, organization.Id);
        using var fullList = await _kit.ListAsync(owner, organization.Id, "?includeArchived=true");

        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, archiveAgain.StatusCode);
        var archived = await ProjectTestKit.ReadAsync<ProjectDetails>(details);
        Assert.NotNull(archived.ArchivedAt);
        Assert.Equal(owner.Id, archived.ArchivedByUserId);
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, addRepository.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, changeTeam.StatusCode);
        Assert.Empty((await ProjectTestKit.ReadAsync<PagedResult<ProjectSummary>>(defaultList)).Items);
        Assert.Equal(project.Id, Assert.Single((await ProjectTestKit.ReadAsync<PagedResult<ProjectSummary>>(fullList)).Items).Id);

        await using var context = _database.CreateContext(organization.Id);
        var stored = await context.Projects.SingleAsync(p => p.Id == project.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Antigo", stored.Name);
        Assert.Equal(archived.ArchivedAt, stored.ArchivedAt);
    }

    [Fact]
    public async Task List_FiltersByTagAndPriority_AndPaginatesByName()
    {
        var owner = await _kit.Organizations.CreateUserAsync();
        var organization = await _kit.Organizations.CreateOrganizationAsync(owner);
        await _kit.CreateProjectAsync(owner, organization.Id, new { name = "Charlie", priority = "High", tags = new List<string> { "Mobile" } });
        await _kit.CreateProjectAsync(owner, organization.Id, new { name = "Alfa", priority = "Low", tags = new List<string> { "mobile", "web" } });
        await _kit.CreateProjectAsync(owner, organization.Id, new { name = "Bravo", priority = "High" });

        using var firstPage = await _kit.ListAsync(owner, organization.Id, "?pageSize=2");
        using var secondPage = await _kit.ListAsync(owner, organization.Id, "?pageSize=2&page=2");
        using var byTag = await _kit.ListAsync(owner, organization.Id, "?tag=MOBILE");
        using var byPriority = await _kit.ListAsync(owner, organization.Id, "?priority=High");
        using var pageTooLarge = await _kit.ListAsync(owner, organization.Id, "?pageSize=101");

        var first = await ProjectTestKit.ReadAsync<PagedResult<ProjectSummary>>(firstPage);
        Assert.Equal((3, 1, 2), (first.TotalCount, first.Page, first.PageSize));
        Assert.Equal(["Alfa", "Bravo"], first.Items.Select(p => p.Name));
        Assert.Equal(["Charlie"], (await ProjectTestKit.ReadAsync<PagedResult<ProjectSummary>>(secondPage)).Items.Select(p => p.Name));
        Assert.Equal(["Alfa", "Charlie"], (await ProjectTestKit.ReadAsync<PagedResult<ProjectSummary>>(byTag)).Items.Select(p => p.Name));
        Assert.Equal(["Bravo", "Charlie"], (await ProjectTestKit.ReadAsync<PagedResult<ProjectSummary>>(byPriority)).Items.Select(p => p.Name));
        Assert.All(first.Items, item => Assert.Equal(ProjectRole.Manager, item.MyRole));
        Assert.Equal(HttpStatusCode.BadRequest, pageTooLarge.StatusCode);
    }

    [Fact]
    public async Task Repositories_AreNormalized_Unique_AndValidated()
    {
        var owner = await _kit.Organizations.CreateUserAsync();
        var organization = await _kit.Organizations.CreateOrganizationAsync(owner);
        var project = await _kit.CreateProjectAsync(owner, organization.Id);

        using var add = await AddRepositoryAsync(owner, organization.Id, project.Id, "https://GitHub.com/gestalyn/api.git/", "API");
        using var duplicate = await AddRepositoryAsync(owner, organization.Id, project.Id, "https://github.com/gestalyn/api");
        using var other = await AddRepositoryAsync(owner, organization.Id, project.Id, "https://git.exemplo.com.br/time/app");
        using var plainHttp = await AddRepositoryAsync(owner, organization.Id, project.Id, "http://github.com/gestalyn/api");
        using var withCredentials = await AddRepositoryAsync(owner, organization.Id, project.Id, "https://usuario:segredo@github.com/gestalyn/api");
        using var withQuery = await AddRepositoryAsync(owner, organization.Id, project.Id, "https://github.com/gestalyn/api?token=abc");

        var repository = await ProjectTestKit.ReadAsync<ProjectRepositoryDetails>(add);
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        Assert.Equal(("https://github.com/gestalyn/api", "API", RepositoryProvider.GitHub), (repository.Url, repository.Name, repository.Provider));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(RepositoryProvider.Other, (await ProjectTestKit.ReadAsync<ProjectRepositoryDetails>(other)).Provider);
        Assert.Equal(HttpStatusCode.BadRequest, plainHttp.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withCredentials.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withQuery.StatusCode);

        using var remove = await _kit.SendToProjectAsync(HttpMethod.Delete, project.Id, $"/repositories/{repository.Id}", owner, organization.Id);
        using var removeAgain = await _kit.SendToProjectAsync(HttpMethod.Delete, project.Id, $"/repositories/{repository.Id}", owner, organization.Id);
        using var list = await _kit.SendToProjectAsync(HttpMethod.Get, project.Id, "/repositories", owner, organization.Id);

        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removeAgain.StatusCode);
        Assert.Equal(["https://git.exemplo.com.br/time/app"], (await ProjectTestKit.ReadAsync<List<ProjectRepositoryDetails>>(list)).Select(r => r.Url));
    }

    private Task<HttpResponseMessage> AddRepositoryAsync(TestUser user, Guid organizationId, Guid projectId, string url, string? name = null) =>
        _kit.SendToProjectAsync(HttpMethod.Post, projectId, "/repositories", user, organizationId, new { url, name });

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
