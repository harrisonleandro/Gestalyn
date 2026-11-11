using ProjectManager.Domain.Organizations;

namespace ProjectManager.UnitTests.Domain;

public sealed class OrganizationMembershipTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SystemRoles_GrantTheExpectedPermissions()
    {
        var roles = Role.CreateSystemRoles(OrganizationId).ToDictionary(role => role.SystemRole!.Value);

        Assert.Equal(4, roles.Count);
        Assert.All(OrganizationPermissions.Catalog.Keys, code =>
        {
            Assert.True(roles[SystemRole.Owner].Grants(code));
            Assert.True(roles[SystemRole.Administrator].Grants(code));
        });
        Assert.All([roles[SystemRole.Member], roles[SystemRole.Viewer]], role =>
        {
            Assert.True(role.Grants(OrganizationPermissions.OrganizationRead));
            Assert.True(role.Grants(OrganizationPermissions.MembersRead));
            Assert.False(role.Grants(OrganizationPermissions.MembersInvite));
            Assert.False(role.Grants(OrganizationPermissions.OrganizationUpdate));
        });
        Assert.True(roles[SystemRole.Owner].IsOwner);
        Assert.False(roles[SystemRole.Administrator].IsOwner);
        Assert.All(roles.Values.SelectMany(role => role.Permissions.Select(p => (role, p))), pair =>
        {
            Assert.Equal(OrganizationId, pair.p.OrganizationId);
            Assert.Equal(pair.role.Id, pair.p.RoleId);
        });
    }

    [Fact]
    public void Member_RejectsRoleFromAnotherOrganization()
    {
        var foreignRole = Role.CreateSystemRoles(Guid.CreateVersion7())[0];
        var member = new OrganizationMember(OrganizationId, UserId, Role.CreateSystemRoles(OrganizationId)[2]);

        Assert.Throws<ArgumentException>(() => new OrganizationMember(OrganizationId, UserId, foreignRole));
        Assert.Throws<ArgumentException>(() => member.ChangeRole(foreignRole));
    }

    [Fact]
    public void Invitation_NormalizesEmailAndExpiresAfterSevenDays()
    {
        var invitation = NewInvitation("Pessoa@Exemplo.COM");

        Assert.Equal("pessoa@exemplo.com", invitation.Email);
        Assert.True(invitation.IsFor("PESSOA@exemplo.com"));
        Assert.False(invitation.IsFor("outra@exemplo.com"));
        Assert.True(invitation.IsPending(Now.AddDays(7).AddSeconds(-1)));
        Assert.False(invitation.IsPending(Now.AddDays(7)));
    }

    [Fact]
    public void Invitation_IsSingleUse()
    {
        var invitation = NewInvitation("pessoa@exemplo.com");

        invitation.Accept(UserId, Now);

        Assert.False(invitation.IsPending(Now));
        Assert.Throws<InvalidOperationException>(() => invitation.Accept(Guid.CreateVersion7(), Now));
        Assert.Throws<InvalidOperationException>(() => invitation.Revoke(Now));
    }

    [Fact]
    public void Invitation_RevokedCannotBeAccepted()
    {
        var invitation = NewInvitation("pessoa@exemplo.com");

        invitation.Revoke(Now);

        Assert.False(invitation.IsPending(Now));
        Assert.Throws<InvalidOperationException>(() => invitation.Accept(UserId, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("sem-arroba")]
    public void Invitation_RejectsInvalidEmail(string email)
    {
        Assert.ThrowsAny<ArgumentException>(() => NewInvitation(email));
    }

    private static Invitation NewInvitation(string email) =>
        new(OrganizationId, email, Role.CreateSystemRoles(OrganizationId)[2], UserId, "hash", Now);
}
