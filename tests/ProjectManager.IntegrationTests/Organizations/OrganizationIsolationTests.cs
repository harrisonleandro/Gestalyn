using System.Net;
using ProjectManager.Application.Organizations;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Organizations;

/// <summary>
/// Isolamento entre organizações com usuários reais: quem não é membro não vê nem altera nada, e a
/// resposta (404) não revela que a organização existe.
/// </summary>
public sealed class OrganizationIsolationTests : IDisposable
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly OrganizationTestKit _kit;

    public OrganizationIsolationTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _client = _factory.CreateClient();
        _kit = new OrganizationTestKit(_client, mailpit);
    }

    [Fact]
    public async Task NonMember_GetsNotFoundOnEveryOrganizationEndpoint()
    {
        var owner = await _kit.CreateUserAsync();
        var outsider = await _kit.CreateUserAsync();
        var organization = await _kit.CreateOrganizationAsync(owner, "Organização privada");
        await _kit.CreateOrganizationAsync(outsider, "Organização do intruso");
        var roleId = await _kit.RoleIdAsync(owner, organization.Id, "Member");
        var basePath = $"/api/v1/organizations/{organization.Id}";

        (HttpMethod Method, string Path, object? Body)[] requests =
        [
            (HttpMethod.Get, basePath, null),
            (HttpMethod.Patch, basePath, new { name = "Invadida" }),
            (HttpMethod.Get, $"{basePath}/members", null),
            (HttpMethod.Get, $"{basePath}/roles", null),
            (HttpMethod.Patch, $"{basePath}/members/{owner.Id}", new { roleId }),
            (HttpMethod.Delete, $"{basePath}/members/{owner.Id}", null),
            (HttpMethod.Get, $"{basePath}/invitations", null),
            (HttpMethod.Post, $"{basePath}/invitations", new { email = outsider.Email, roleId }),
        ];

        foreach (var (method, path, body) in requests)
        {
            using var response = await _kit.SendAsync(method, path, outsider, body);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{method} {path}: esperado 404, obtido {response.StatusCode}.");
        }

        using var stillPrivate = await _kit.SendAsync(HttpMethod.Get, basePath, owner);
        Assert.Equal("Organização privada", (await OrganizationTestKit.ReadAsync<OrganizationDetails>(stillPrivate)).Name);
    }

    [Fact]
    public async Task OrganizationList_ShowsOnlyTheUsersOwnOrganizations()
    {
        var alice = await _kit.CreateUserAsync();
        var bruno = await _kit.CreateUserAsync();
        var aliceOrganization = await _kit.CreateOrganizationAsync(alice, "Só da Alice");
        var brunoOrganization = await _kit.CreateOrganizationAsync(bruno, "Só do Bruno");

        using var response = await _kit.SendAsync(HttpMethod.Get, "/api/v1/organizations", alice);
        var organizations = await OrganizationTestKit.ReadAsync<List<OrganizationSummary>>(response);

        Assert.Equal([aliceOrganization.Id], organizations.Select(o => o.Id));
        Assert.DoesNotContain(organizations, o => o.Id == brunoOrganization.Id);
    }

    [Fact]
    public async Task RoleFromAnotherOrganization_CannotBeAssigned()
    {
        var alice = await _kit.CreateUserAsync();
        var bruno = await _kit.CreateUserAsync();
        var member = await _kit.CreateUserAsync();
        var aliceOrganization = await _kit.CreateOrganizationAsync(alice);
        var brunoOrganization = await _kit.CreateOrganizationAsync(bruno);
        await _kit.AddMemberAsync(alice, aliceOrganization.Id, member, "Member");
        var foreignRoleId = await _kit.RoleIdAsync(bruno, brunoOrganization.Id, "Administrator");

        using var assign = await _kit.SendAsync(
            HttpMethod.Patch,
            $"/api/v1/organizations/{aliceOrganization.Id}/members/{member.Id}",
            alice,
            new { roleId = foreignRoleId });
        using var invite = await _kit.InviteAsync(alice, aliceOrganization.Id, "outro@teste.gestalyn", foreignRoleId);

        Assert.Equal(HttpStatusCode.NotFound, assign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, invite.StatusCode);
    }

    [Fact]
    public async Task OwnerOfOneOrganization_CannotManageMembersOfAnother()
    {
        var alice = await _kit.CreateUserAsync();
        var bruno = await _kit.CreateUserAsync();
        await _kit.CreateOrganizationAsync(alice);
        var brunoOrganization = await _kit.CreateOrganizationAsync(bruno);
        var brunoMemberRole = await _kit.RoleIdAsync(bruno, brunoOrganization.Id, "Member");

        using var demote = await _kit.SendAsync(
            HttpMethod.Patch,
            $"/api/v1/organizations/{brunoOrganization.Id}/members/{bruno.Id}",
            alice,
            new { roleId = brunoMemberRole });
        using var remove = await _kit.SendAsync(HttpMethod.Delete, $"/api/v1/organizations/{brunoOrganization.Id}/members/{bruno.Id}", alice);
        using var check = await _kit.SendAsync(HttpMethod.Get, $"/api/v1/organizations/{brunoOrganization.Id}/members", bruno);

        Assert.Equal(HttpStatusCode.NotFound, demote.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
        Assert.Equal("Proprietário", Assert.Single(await OrganizationTestKit.ReadAsync<List<MemberDetails>>(check)).RoleName);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
