using System.Net;
using ProjectManager.Application.Organizations;
using ProjectManager.Domain.Organizations;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Organizations;

/// <summary>Papel e permissões do usuário na organização (ADR-015): só leitura, sem revelar organizações alheias.</summary>
public sealed class MyMembershipTests : IDisposable
{
    private readonly ApiFactory _factory;
    private readonly OrganizationTestKit _kit;

    public MyMembershipTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _kit = new OrganizationTestKit(_factory.CreateClient(), mailpit);
    }

    [Fact]
    public async Task Members_SeeTheirOwnRoleAndPermissions_AndOutsidersGetNotFound()
    {
        var owner = await _kit.CreateUserAsync();
        var viewer = await _kit.CreateUserAsync();
        var outsider = await _kit.CreateUserAsync();
        var organization = await _kit.CreateOrganizationAsync(owner, "Organização com papéis");
        await _kit.AddMemberAsync(owner, organization.Id, viewer, "Viewer");

        var forOwner = await ReadAsync(owner, organization.Id);
        var forViewer = await ReadAsync(viewer, organization.Id);
        using var forOutsider = await _kit.SendAsync(HttpMethod.Get, $"/api/v1/organizations/{organization.Id}/me", outsider);
        using var anonymous = await _kit.SendAsync(HttpMethod.Get, $"/api/v1/organizations/{organization.Id}/me", null);

        Assert.Equal((organization.Id, "Organização com papéis", true), (forOwner.OrganizationId, forOwner.Name, forOwner.IsOwner));
        Assert.Equal(OrganizationPermissions.Catalog.Keys.Order(StringComparer.Ordinal), forOwner.Permissions);
        Assert.Equal((false, "Leitor"), (forViewer.IsOwner, forViewer.RoleName));
        Assert.Equal([OrganizationPermissions.MembersRead, OrganizationPermissions.OrganizationRead], forViewer.Permissions);
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.Unauthorized), (forOutsider.StatusCode, anonymous.StatusCode));
    }

    public void Dispose() => _factory.Dispose();

    private async Task<MyMembership> ReadAsync(TestUser user, Guid organizationId)
    {
        using var response = await _kit.SendAsync(HttpMethod.Get, $"/api/v1/organizations/{organizationId}/me", user);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await OrganizationTestKit.ReadAsync<MyMembership>(response);
    }
}
