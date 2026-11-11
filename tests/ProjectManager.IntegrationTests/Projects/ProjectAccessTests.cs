using System.Net;
using Microsoft.EntityFrameworkCore;
using ProjectManager.Application.Common;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Projects;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Organizations;

namespace ProjectManager.IntegrationTests.Projects;

/// <summary>
/// Visibilidade, papéis de projeto, equipe e cabeçalho de organização (ADR-010), com usuários reais.
/// </summary>
public sealed class ProjectAccessTests : IDisposable
{
    private readonly PostgresDatabaseFixture _database;
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly ProjectTestKit _kit;

    public ProjectAccessTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _database = database;
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _client = _factory.CreateClient();
        _kit = new ProjectTestKit(_client, mailpit);
    }

    [Fact]
    public async Task OrganizationMember_SeesOnlyProjectsOfTheirTeams_WhileAdministratorsSeeAll()
    {
        var owner = await _kit.Organizations.CreateUserAsync();
        var admin = await _kit.Organizations.CreateUserAsync();
        var member = await _kit.Organizations.CreateUserAsync();
        var organization = await _kit.Organizations.CreateOrganizationAsync(owner);
        await _kit.Organizations.AddMemberAsync(owner, organization.Id, admin, "Administrator");
        await _kit.Organizations.AddMemberAsync(owner, organization.Id, member, "Member");
        var restricted = await _kit.CreateProjectAsync(owner, organization.Id, new { name = "Restrito" });
        var shared = await _kit.CreateProjectAsync(owner, organization.Id, new { name = "Compartilhado" });
        await _kit.AddToTeamAsync(owner, organization.Id, shared.Id, member, "Viewer");

        using var memberList = await _kit.ListAsync(member, organization.Id);
        using var adminList = await _kit.ListAsync(admin, organization.Id);
        using var memberGetsRestricted = await _kit.SendToProjectAsync(HttpMethod.Get, restricted.Id, "", member, organization.Id);
        using var memberEditsRestricted = await _kit.SendToProjectAsync(HttpMethod.Patch, restricted.Id, "", member, organization.Id, new { name = "x" });
        using var adminGetsRestricted = await _kit.SendToProjectAsync(HttpMethod.Get, restricted.Id, "", admin, organization.Id);
        using var adminArchivesRestricted = await _kit.SendToProjectAsync(HttpMethod.Delete, restricted.Id, "", admin, organization.Id);

        var memberProjects = await ProjectTestKit.ReadAsync<PagedResult<ProjectSummary>>(memberList);
        Assert.Equal(["Compartilhado"], memberProjects.Items.Select(p => p.Name));
        Assert.Equal(1, memberProjects.TotalCount);
        Assert.Equal(ProjectRole.Viewer, memberProjects.Items[0].MyRole);
        Assert.Equal(["Compartilhado", "Restrito"], (await ProjectTestKit.ReadAsync<PagedResult<ProjectSummary>>(adminList)).Items.Select(p => p.Name));
        Assert.Equal(HttpStatusCode.NotFound, memberGetsRestricted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, memberEditsRestricted.StatusCode);
        var adminView = await ProjectTestKit.ReadAsync<ProjectDetails>(adminGetsRestricted);
        Assert.Null(adminView.MyRole);
        Assert.Equal(ProjectPermissions.All.Order(StringComparer.Ordinal), adminView.MyPermissions);
        Assert.Equal(HttpStatusCode.NoContent, adminArchivesRestricted.StatusCode);
    }

    [Fact]
    public async Task ProjectRoles_GrantTheirPermissions_InEveryEndpoint()
    {
        var owner = await _kit.Organizations.CreateUserAsync();
        var viewer = await _kit.Organizations.CreateUserAsync();
        var contributor = await _kit.Organizations.CreateUserAsync();
        var manager = await _kit.Organizations.CreateUserAsync();
        var organization = await _kit.Organizations.CreateOrganizationAsync(owner);
        foreach (var user in new[] { viewer, contributor, manager })
        {
            await _kit.Organizations.AddMemberAsync(owner, organization.Id, user, "Member");
        }

        var project = await _kit.CreateProjectAsync(owner, organization.Id);
        await _kit.AddToTeamAsync(owner, organization.Id, project.Id, viewer, "Viewer");
        await _kit.AddToTeamAsync(owner, organization.Id, project.Id, contributor, "Contributor");
        await _kit.AddToTeamAsync(owner, organization.Id, project.Id, manager, "Manager");

        // Leitura, edição, gestão da equipe e arquivamento: o que cada papel pode (true) ou recebe 403.
        (TestUser User, bool Update, bool ManageTeam, bool Archive)[] matrix =
        [
            (viewer, false, false, false),
            (contributor, true, false, false),
            (manager, true, true, true),
        ];

        foreach (var (user, canUpdate, canManageTeam, canArchive) in matrix)
        {
            using var read = await _kit.SendToProjectAsync(HttpMethod.Get, project.Id, "", user, organization.Id);
            using var readTeam = await _kit.SendToProjectAsync(HttpMethod.Get, project.Id, "/members", user, organization.Id);
            using var readRepositories = await _kit.SendToProjectAsync(HttpMethod.Get, project.Id, "/repositories", user, organization.Id);
            using var update = await _kit.SendToProjectAsync(HttpMethod.Patch, project.Id, "", user, organization.Id, new { description = "Alterado" });
            using var addRepository = await _kit.SendToProjectAsync(
                HttpMethod.Post, project.Id, "/repositories", user, organization.Id, new { url = $"https://github.com/gestalyn/{user.Id}" });
            using var changeTeam = await _kit.SendToProjectAsync(
                HttpMethod.Patch, project.Id, $"/members/{viewer.Id}", user, organization.Id, new { role = "Viewer" });

            var name = user.Email;
            Assert.True(read.StatusCode == HttpStatusCode.OK, $"{name}: leitura");
            Assert.True(readTeam.StatusCode == HttpStatusCode.OK, $"{name}: equipe");
            Assert.True(readRepositories.StatusCode == HttpStatusCode.OK, $"{name}: repositórios");
            Assert.Equal(canUpdate ? HttpStatusCode.OK : HttpStatusCode.Forbidden, update.StatusCode);
            Assert.Equal(canUpdate ? HttpStatusCode.Created : HttpStatusCode.Forbidden, addRepository.StatusCode);
            Assert.Equal(canManageTeam ? HttpStatusCode.OK : HttpStatusCode.Forbidden, changeTeam.StatusCode);

            if (!canArchive)
            {
                using var archive = await _kit.SendToProjectAsync(HttpMethod.Delete, project.Id, "", user, organization.Id);
                Assert.Equal(HttpStatusCode.Forbidden, archive.StatusCode);
            }
        }

        using var managerArchives = await _kit.SendToProjectAsync(HttpMethod.Delete, project.Id, "", manager, organization.Id);
        Assert.Equal(HttpStatusCode.NoContent, managerArchives.StatusCode);
    }

    [Fact]
    public async Task ProjectCreation_RequiresThePermission()
    {
        var owner = await _kit.Organizations.CreateUserAsync();
        var member = await _kit.Organizations.CreateUserAsync();
        var viewer = await _kit.Organizations.CreateUserAsync();
        var organization = await _kit.Organizations.CreateOrganizationAsync(owner);
        await _kit.Organizations.AddMemberAsync(owner, organization.Id, member, "Member");
        await _kit.Organizations.AddMemberAsync(owner, organization.Id, viewer, "Viewer");

        using var byMember = await _kit.CreateAsync(member, organization.Id, new { name = "Do membro" });
        using var byViewer = await _kit.CreateAsync(viewer, organization.Id, new { name = "Do leitor" });
        using var viewerList = await _kit.ListAsync(viewer, organization.Id);

        Assert.Equal(HttpStatusCode.Created, byMember.StatusCode);
        Assert.Equal(ProjectRole.Manager, (await ProjectTestKit.ReadAsync<ProjectDetails>(byMember)).MyRole);
        Assert.Equal(HttpStatusCode.Forbidden, byViewer.StatusCode);
        Assert.Empty((await ProjectTestKit.ReadAsync<PagedResult<ProjectSummary>>(viewerList)).Items);
    }

    [Fact]
    public async Task Team_AcceptsOnlyOrganizationMembers_AndKeepsASingleResponsible()
    {
        var owner = await _kit.Organizations.CreateUserAsync();
        var first = await _kit.Organizations.CreateUserAsync();
        var second = await _kit.Organizations.CreateUserAsync();
        var outsider = await _kit.Organizations.CreateUserAsync();
        var organization = await _kit.Organizations.CreateOrganizationAsync(owner);
        await _kit.Organizations.AddMemberAsync(owner, organization.Id, first, "Member");
        await _kit.Organizations.AddMemberAsync(owner, organization.Id, second, "Member");
        var project = await _kit.CreateProjectAsync(owner, organization.Id);

        using var addOutsider = await AddToTeamAsync(owner, organization.Id, project.Id, outsider.Id, "Contributor");
        using var addFirst = await AddToTeamAsync(owner, organization.Id, project.Id, first.Id, "Contributor", isResponsible: true);
        using var addFirstAgain = await AddToTeamAsync(owner, organization.Id, project.Id, first.Id, "Viewer");
        using var addSecond = await AddToTeamAsync(owner, organization.Id, project.Id, second.Id, "Contributor");
        using var makeSecondResponsible = await _kit.SendToProjectAsync(
            HttpMethod.Patch, project.Id, $"/members/{second.Id}", owner, organization.Id, new { isResponsible = true });
        using var details = await _kit.SendToProjectAsync(HttpMethod.Get, project.Id, "", owner, organization.Id);
        using var team = await _kit.SendToProjectAsync(HttpMethod.Get, project.Id, "/members", owner, organization.Id);

        Assert.Equal(HttpStatusCode.NotFound, addOutsider.StatusCode);
        Assert.True((await ProjectTestKit.ReadAsync<ProjectMemberDetails>(addFirst)).IsResponsible);
        Assert.Equal(HttpStatusCode.Conflict, addFirstAgain.StatusCode);
        Assert.Equal(HttpStatusCode.Created, addSecond.StatusCode);
        Assert.True((await ProjectTestKit.ReadAsync<ProjectMemberDetails>(makeSecondResponsible)).IsResponsible);
        Assert.Equal(second.Id, (await ProjectTestKit.ReadAsync<ProjectDetails>(details)).Responsible?.UserId);
        Assert.Equal([second.Id], (await ProjectTestKit.ReadAsync<List<ProjectMemberDetails>>(team)).Where(m => m.IsResponsible).Select(m => m.UserId));

        using var remove = await _kit.SendToProjectAsync(HttpMethod.Delete, project.Id, $"/members/{second.Id}", owner, organization.Id);
        using var afterRemoval = await _kit.SendToProjectAsync(HttpMethod.Get, project.Id, "", owner, organization.Id);
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.Null((await ProjectTestKit.ReadAsync<ProjectDetails>(afterRemoval)).Responsible);
    }

    [Fact]
    public async Task LeavingTheOrganization_RemovesThePersonFromProjectTeams()
    {
        var owner = await _kit.Organizations.CreateUserAsync();
        var member = await _kit.Organizations.CreateUserAsync();
        var organization = await _kit.Organizations.CreateOrganizationAsync(owner);
        await _kit.Organizations.AddMemberAsync(owner, organization.Id, member, "Member");
        var project = await _kit.CreateProjectAsync(owner, organization.Id);
        await _kit.AddToTeamAsync(owner, organization.Id, project.Id, member, "Manager", isResponsible: true);

        using var leave = await _kit.Organizations.SendAsync(HttpMethod.Delete, $"/api/v1/organizations/{organization.Id}/members/{member.Id}", member);
        using var team = await _kit.SendToProjectAsync(HttpMethod.Get, project.Id, "/members", owner, organization.Id);

        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        Assert.Equal([owner.Id], (await ProjectTestKit.ReadAsync<List<ProjectMemberDetails>>(team)).Select(m => m.UserId));

        await using var context = _database.CreateContext(organization.Id);
        Assert.False(await context.ProjectMembers.AnyAsync(m => m.UserId == member.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OrganizationHeader_IsRequired_AndValidatedAgainstTheMembership()
    {
        var alice = await _kit.Organizations.CreateUserAsync();
        var bruno = await _kit.Organizations.CreateUserAsync();
        var aliceOrganization = await _kit.Organizations.CreateOrganizationAsync(alice);
        var aliceSecondOrganization = await _kit.Organizations.CreateOrganizationAsync(alice);
        var brunoOrganization = await _kit.Organizations.CreateOrganizationAsync(bruno);
        var project = await _kit.CreateProjectAsync(alice, aliceOrganization.Id);
        var path = $"/api/v1/projects/{project.Id}";

        using var missing = await _kit.SendAsync(HttpMethod.Get, path, alice);
        using var invalid = await _kit.SendAsync(HttpMethod.Get, path, alice, organizationHeader: "não-é-um-guid");
        using var notAMember = await _kit.SendAsync(HttpMethod.Get, path, alice, organizationHeader: brunoOrganization.Id.ToString());
        using var otherOwnOrganization = await _kit.SendAsync(HttpMethod.Get, path, alice, organizationHeader: aliceSecondOrganization.Id.ToString());
        using var correct = await _kit.SendAsync(HttpMethod.Get, path, alice, organizationHeader: aliceOrganization.Id.ToString());
        using var anonymous = await _kit.SendAsync(HttpMethod.Get, path, null, organizationHeader: aliceOrganization.Id.ToString());

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notAMember.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherOwnOrganization.StatusCode);
        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private Task<HttpResponseMessage> AddToTeamAsync(TestUser manager, Guid organizationId, Guid projectId, Guid userId, string role, bool isResponsible = false) =>
        _kit.SendToProjectAsync(HttpMethod.Post, projectId, "/members", manager, organizationId, new { userId, role, isResponsible });

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
