using System.Net;
using ProjectManager.Application.Organizations;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Organizations;

/// <summary>Convites com e-mail real: envio, aceitação pelo destinatário, uso único, revogação e conflitos.</summary>
public sealed class InvitationTests : IDisposable
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly OrganizationTestKit _kit;

    public InvitationTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _client = _factory.CreateClient();
        _kit = new OrganizationTestKit(_client, mailpit);
    }

    [Fact]
    public async Task Invitation_IsEmailedAndAcceptedByTheInvitee()
    {
        var owner = await _kit.CreateUserAsync();
        var invitee = await _kit.CreateUserAsync();
        var organization = await _kit.CreateOrganizationAsync(owner, "Equipe Convites");
        var adminRoleId = await _kit.RoleIdAsync(owner, organization.Id, "Administrator");

        using (var invitation = await _kit.InviteAsync(owner, organization.Id, invitee.Email.ToUpperInvariant(), adminRoleId))
        {
            Assert.Equal(HttpStatusCode.Created, invitation.StatusCode);
            var details = await OrganizationTestKit.ReadAsync<InvitationDetails>(invitation);
            Assert.Equal(invitee.Email, details.Email);
            Assert.Equal(InvitationStatus.Pending, details.Status);
        }

        using var acceptance = await _kit.AcceptAsync(invitee, await _kit.InvitationTokenAsync(invitee.Email));
        Assert.Equal(HttpStatusCode.OK, acceptance.StatusCode);
        var accepted = await OrganizationTestKit.ReadAsync<AcceptedInvitation>(acceptance);
        Assert.Equal("Equipe Convites", accepted.OrganizationName);
        Assert.Equal("Administrador", accepted.RoleName);

        using var members = await _kit.SendAsync(HttpMethod.Get, $"/api/v1/organizations/{organization.Id}/members", invitee);
        Assert.Contains(await OrganizationTestKit.ReadAsync<List<MemberDetails>>(members), m => m.UserId == invitee.Id && m.RoleName == "Administrador");
    }

    [Fact]
    public async Task Invitation_CannotBeAcceptedByAnotherEmail()
    {
        var owner = await _kit.CreateUserAsync();
        var invitee = await _kit.CreateUserAsync();
        var intruder = await _kit.CreateUserAsync();
        var organization = await _kit.CreateOrganizationAsync(owner);
        var roleId = await _kit.RoleIdAsync(owner, organization.Id, "Member");
        using (var invitation = await _kit.InviteAsync(owner, organization.Id, invitee.Email, roleId))
        {
            invitation.EnsureSuccessStatusCode();
        }

        var token = await _kit.InvitationTokenAsync(invitee.Email);

        using var byIntruder = await _kit.AcceptAsync(intruder, token);
        Assert.Equal(HttpStatusCode.Forbidden, byIntruder.StatusCode);

        // O convite continua válido para o destinatário.
        using var byInvitee = await _kit.AcceptAsync(invitee, token);
        Assert.Equal(HttpStatusCode.OK, byInvitee.StatusCode);
    }

    [Fact]
    public async Task Invitation_IsSingleUse()
    {
        var owner = await _kit.CreateUserAsync();
        var invitee = await _kit.CreateUserAsync();
        var organization = await _kit.CreateOrganizationAsync(owner);
        await _kit.AddMemberAsync(owner, organization.Id, invitee, "Viewer");

        using var reuse = await _kit.AcceptAsync(invitee, await _kit.InvitationTokenAsync(invitee.Email));

        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
    }

    [Fact]
    public async Task RevokedInvitation_CannotBeAccepted()
    {
        var owner = await _kit.CreateUserAsync();
        var invitee = await _kit.CreateUserAsync();
        var organization = await _kit.CreateOrganizationAsync(owner);
        var roleId = await _kit.RoleIdAsync(owner, organization.Id, "Member");
        using var invitation = await _kit.InviteAsync(owner, organization.Id, invitee.Email, roleId);
        var invitationId = (await OrganizationTestKit.ReadAsync<InvitationDetails>(invitation)).Id;

        using (var revoke = await _kit.SendAsync(HttpMethod.Delete, $"/api/v1/organizations/{organization.Id}/invitations/{invitationId}", owner))
        {
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        }

        using var acceptance = await _kit.AcceptAsync(invitee, await _kit.InvitationTokenAsync(invitee.Email));
        Assert.Equal(HttpStatusCode.BadRequest, acceptance.StatusCode);
    }

    [Fact]
    public async Task Invitation_ConflictsForExistingMemberAndDuplicatePendingInvitation()
    {
        var owner = await _kit.CreateUserAsync();
        var member = await _kit.CreateUserAsync();
        var organization = await _kit.CreateOrganizationAsync(owner);
        await _kit.AddMemberAsync(owner, organization.Id, member, "Member");
        var roleId = await _kit.RoleIdAsync(owner, organization.Id, "Member");
        var pendingEmail = $"{Guid.NewGuid():N}@teste.gestalyn";

        using var existingMember = await _kit.InviteAsync(owner, organization.Id, member.Email, roleId);
        using var first = await _kit.InviteAsync(owner, organization.Id, pendingEmail, roleId);
        using var duplicate = await _kit.InviteAsync(owner, organization.Id, pendingEmail, roleId);

        Assert.Equal(HttpStatusCode.Conflict, existingMember.StatusCode);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
