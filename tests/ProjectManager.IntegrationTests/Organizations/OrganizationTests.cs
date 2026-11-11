using System.Net;
using ProjectManager.Application.Organizations;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Organizations;

/// <summary>Criação, listagem, consulta, edição e associação a múltiplas organizações.</summary>
public sealed class OrganizationTests : IDisposable
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly OrganizationTestKit _kit;

    public OrganizationTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _client = _factory.CreateClient();
        _kit = new OrganizationTestKit(_client, mailpit);
    }

    [Fact]
    public async Task Create_MakesTheCreatorOwnerWithSystemRoles()
    {
        var owner = await _kit.CreateUserAsync();

        var organization = await _kit.CreateOrganizationAsync(owner, "Acme");

        Assert.Equal("Acme", organization.Name);
        Assert.Equal("Proprietário", organization.RoleName);

        using var roles = await _kit.SendAsync(HttpMethod.Get, $"/api/v1/organizations/{organization.Id}/roles", owner);
        var roleNames = (await OrganizationTestKit.ReadAsync<List<RoleDetails>>(roles)).Select(role => role.Name);
        Assert.Equal(["Proprietário", "Administrador", "Membro", "Leitor"], roleNames);

        using var members = await _kit.SendAsync(HttpMethod.Get, $"/api/v1/organizations/{organization.Id}/members", owner);
        var member = Assert.Single(await OrganizationTestKit.ReadAsync<List<MemberDetails>>(members));
        Assert.Equal(owner.Id, member.UserId);
        Assert.Equal("Proprietário", member.RoleName);
    }

    [Fact]
    public async Task Create_RequiresAuthentication()
    {
        using var response = await _kit.SendAsync(HttpMethod.Post, "/api/v1/organizations", user: null, new { name = "Sem sessão" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task User_BelongsToSeveralOrganizationsWithDifferentRoles()
    {
        var alice = await _kit.CreateUserAsync();
        var bruno = await _kit.CreateUserAsync();
        var aliceOrganization = await _kit.CreateOrganizationAsync(alice, "Organização da Alice");
        var brunoOrganization = await _kit.CreateOrganizationAsync(bruno, "Organização do Bruno");

        await _kit.AddMemberAsync(alice, aliceOrganization.Id, bruno, "Member");

        using var response = await _kit.SendAsync(HttpMethod.Get, "/api/v1/organizations", bruno);
        var organizations = await OrganizationTestKit.ReadAsync<List<OrganizationSummary>>(response);

        Assert.Equal(2, organizations.Count);
        Assert.Equal("Membro", organizations.Single(o => o.Id == aliceOrganization.Id).RoleName);
        Assert.Equal("Proprietário", organizations.Single(o => o.Id == brunoOrganization.Id).RoleName);
    }

    [Fact]
    public async Task Rename_RequiresUpdatePermission()
    {
        var owner = await _kit.CreateUserAsync();
        var member = await _kit.CreateUserAsync();
        var organization = await _kit.CreateOrganizationAsync(owner, "Nome antigo");
        await _kit.AddMemberAsync(owner, organization.Id, member, "Member");

        using var byMember = await _kit.SendAsync(HttpMethod.Patch, $"/api/v1/organizations/{organization.Id}", member, new { name = "Tentativa" });
        using var byOwner = await _kit.SendAsync(HttpMethod.Patch, $"/api/v1/organizations/{organization.Id}", owner, new { name = "Nome novo" });

        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
        Assert.Equal("application/problem+json", byMember.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, byOwner.StatusCode);
        Assert.Equal("Nome novo", (await OrganizationTestKit.ReadAsync<OrganizationDetails>(byOwner)).Name);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
