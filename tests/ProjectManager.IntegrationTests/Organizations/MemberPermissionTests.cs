using System.Net;
using ProjectManager.Application.Organizations;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Organizations;

/// <summary>
/// Permissões por papel e regras de proprietário, validadas no backend.
/// </summary>
public sealed class MemberPermissionTests : IDisposable
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly OrganizationTestKit _kit;

    public MemberPermissionTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _client = _factory.CreateClient();
        _kit = new OrganizationTestKit(_client, mailpit);
    }

    [Fact]
    public async Task Member_CanReadButNotManage()
    {
        var owner = await _kit.CreateUserAsync();
        var member = await _kit.CreateUserAsync();
        var organization = await _kit.CreateOrganizationAsync(owner);
        await _kit.AddMemberAsync(owner, organization.Id, member, "Member");
        var memberRoleId = await _kit.RoleIdAsync(owner, organization.Id, "Member");
        var basePath = $"/api/v1/organizations/{organization.Id}";

        (HttpMethod Method, string Path, object? Body, HttpStatusCode Expected)[] cases =
        [
            (HttpMethod.Get, basePath, null, HttpStatusCode.OK),
            (HttpMethod.Get, $"{basePath}/members", null, HttpStatusCode.OK),
            (HttpMethod.Get, $"{basePath}/roles", null, HttpStatusCode.OK),
            (HttpMethod.Patch, basePath, new { name = "Novo nome" }, HttpStatusCode.Forbidden),
            (HttpMethod.Get, $"{basePath}/invitations", null, HttpStatusCode.Forbidden),
            (HttpMethod.Post, $"{basePath}/invitations", new { email = "x@teste.gestalyn", roleId = memberRoleId }, HttpStatusCode.Forbidden),
            (HttpMethod.Patch, $"{basePath}/members/{owner.Id}", new { roleId = memberRoleId }, HttpStatusCode.Forbidden),
            (HttpMethod.Delete, $"{basePath}/members/{owner.Id}", null, HttpStatusCode.Forbidden),
        ];

        foreach (var (method, path, body, expected) in cases)
        {
            using var response = await _kit.SendAsync(method, path, member, body);
            Assert.True(expected == response.StatusCode, $"{method} {path}: esperado {expected}, obtido {response.StatusCode}.");
        }
    }

    [Fact]
    public async Task Administrator_ManagesMembersButNotOwners()
    {
        var owner = await _kit.CreateUserAsync();
        var admin = await _kit.CreateUserAsync();
        var member = await _kit.CreateUserAsync();
        var organization = await _kit.CreateOrganizationAsync(owner);
        await _kit.AddMemberAsync(owner, organization.Id, admin, "Administrator");
        await _kit.AddMemberAsync(owner, organization.Id, member, "Member");
        var viewerRoleId = await _kit.RoleIdAsync(owner, organization.Id, "Viewer");
        var ownerRoleId = await _kit.RoleIdAsync(owner, organization.Id, "Owner");
        var membersPath = $"/api/v1/organizations/{organization.Id}/members";

        using var demoteMember = await _kit.SendAsync(HttpMethod.Patch, $"{membersPath}/{member.Id}", admin, new { roleId = viewerRoleId });
        using var promoteToOwner = await _kit.SendAsync(HttpMethod.Patch, $"{membersPath}/{member.Id}", admin, new { roleId = ownerRoleId });
        using var demoteOwner = await _kit.SendAsync(HttpMethod.Patch, $"{membersPath}/{owner.Id}", admin, new { roleId = viewerRoleId });
        using var removeOwner = await _kit.SendAsync(HttpMethod.Delete, $"{membersPath}/{owner.Id}", admin);
        using var inviteOwner = await _kit.InviteAsync(admin, organization.Id, "novo@teste.gestalyn", ownerRoleId);

        Assert.Equal(HttpStatusCode.OK, demoteMember.StatusCode);
        Assert.Equal("Leitor", (await OrganizationTestKit.ReadAsync<MemberDetails>(demoteMember)).RoleName);
        Assert.Equal(HttpStatusCode.Forbidden, promoteToOwner.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, demoteOwner.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, removeOwner.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, inviteOwner.StatusCode);
    }

    [Fact]
    public async Task LastOwner_CannotBeDemotedOrLeave_UntilAnotherOwnerExists()
    {
        var owner = await _kit.CreateUserAsync();
        var second = await _kit.CreateUserAsync();
        var organization = await _kit.CreateOrganizationAsync(owner);
        var memberRoleId = await _kit.RoleIdAsync(owner, organization.Id, "Member");
        var membersPath = $"/api/v1/organizations/{organization.Id}/members";

        using (var demoteSelf = await _kit.SendAsync(HttpMethod.Patch, $"{membersPath}/{owner.Id}", owner, new { roleId = memberRoleId }))
        using (var leave = await _kit.SendAsync(HttpMethod.Delete, $"{membersPath}/{owner.Id}", owner))
        {
            Assert.Equal(HttpStatusCode.Conflict, demoteSelf.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, leave.StatusCode);
        }

        await _kit.AddMemberAsync(owner, organization.Id, second, "Owner");

        using var leaveWithAnotherOwner = await _kit.SendAsync(HttpMethod.Delete, $"{membersPath}/{owner.Id}", owner);
        using var afterLeaving = await _kit.SendAsync(HttpMethod.Get, $"/api/v1/organizations/{organization.Id}", owner);

        Assert.Equal(HttpStatusCode.NoContent, leaveWithAnotherOwner.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterLeaving.StatusCode);
    }

    [Fact]
    public async Task Member_CanLeaveTheOrganization()
    {
        var owner = await _kit.CreateUserAsync();
        var member = await _kit.CreateUserAsync();
        var organization = await _kit.CreateOrganizationAsync(owner);
        await _kit.AddMemberAsync(owner, organization.Id, member, "Viewer");

        using var leave = await _kit.SendAsync(HttpMethod.Delete, $"/api/v1/organizations/{organization.Id}/members/{member.Id}", member);
        using var list = await _kit.SendAsync(HttpMethod.Get, "/api/v1/organizations", member);

        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        Assert.Empty(await OrganizationTestKit.ReadAsync<List<OrganizationSummary>>(list));
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
