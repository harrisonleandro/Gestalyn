using ProjectManager.Domain.Agents;
using ProjectManager.Domain.Organizations;
using ProjectManager.Domain.Projects;

namespace ProjectManager.UnitTests.Domain;

/// <summary>Regras de agentes, credenciais e acesso a projetos (ADR-016).</summary>
public sealed class AgentRulesTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Hash = new('A', AgentTokens.HashLength);
    private static readonly string[] ReadScopes = [AgentScopes.TasksRead];

    [Fact]
    public void Agent_TrimsAndNormalizesTheName_AndRejectsInvalidNames()
    {
        var agent = new Agent(OrganizationId, "  Revisor de código ", null, UserId);

        Assert.Equal(("Revisor de código", "REVISOR DE CÓDIGO", AgentStatus.Active), (agent.Name, agent.NormalizedName, agent.Status));
        Assert.Throws<ArgumentException>(() => new Agent(OrganizationId, " ", null, UserId));
        Assert.Throws<ArgumentException>(() => new Agent(OrganizationId, new string('x', Agent.NameMaxLength + 1), null, UserId));
        Assert.Throws<ArgumentException>(() => new Agent(OrganizationId, "Agente", new string('x', Agent.DescriptionMaxLength + 1), UserId));
        Assert.Throws<ArgumentException>(() => new Agent(Guid.Empty, "Agente", null, UserId));
    }

    [Fact]
    public void Agent_Disable_RecordsWhoAndWhen_AndEnableClearsIt()
    {
        var agent = new Agent(OrganizationId, "Agente", null, UserId);

        agent.Disable(UserId, Now);
        Assert.Equal((AgentStatus.Disabled, Now, UserId), (agent.Status, agent.DisabledAt, agent.DisabledByUserId));
        Assert.False(agent.IsActive);

        agent.Enable();
        Assert.Equal((AgentStatus.Active, (DateTimeOffset?)null, (Guid?)null), (agent.Status, agent.DisabledAt, agent.DisabledByUserId));
    }

    [Fact]
    public void Scopes_AreValidatedDeduplicatedAndOrdered()
    {
        Assert.Equal(
            [AgentScopes.ContextRead, AgentScopes.TasksWrite],
            AgentScopes.Normalize([AgentScopes.TasksWrite, " context:read ", AgentScopes.TasksWrite], out var error));
        Assert.Null(error);

        Assert.Null(AgentScopes.Normalize([], out error));
        Assert.Equal("Informe pelo menos um escopo.", error);

        Assert.Null(AgentScopes.Normalize([AgentScopes.TasksRead, "tasks:approve"], out error));
        Assert.Contains("tasks:approve", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gsk_ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopq", true)]
    [InlineData("gsk_abcdefgh-_0123456789abcdefghijklmnopqrstuvw", true)]
    [InlineData("gsk_ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnop", false)]
    [InlineData("gsk_ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqr", false)]
    [InlineData("gsk_ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmno+q", false)]
    [InlineData("gsx_ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopq", false)]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl", false)]
    [InlineData(null, false)]
    public void Token_Format_IsMarkerPlus43Base64UrlCharacters(string? token, bool expected) =>
        Assert.Equal(expected, AgentTokens.IsWellFormed(token));

    [Fact]
    public void Token_Prefix_IsTheFirstEightCharactersAfterTheMarker()
    {
        Assert.Equal("ABCDEFGH", AgentTokens.PrefixOf("gsk_ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopq"));
        Assert.Throws<ArgumentException>(() => AgentTokens.PrefixOf("gsk_curto"));
    }

    [Fact]
    public void Credential_KeepsOnlyTheHashAndPrefix_WithNormalizedScopes()
    {
        var agent = new Agent(OrganizationId, "Agente", null, UserId);

        var credential = new AgentCredential(agent, "CI", [AgentScopes.TasksWrite, AgentScopes.ContextRead], Hash, "ABCDEFGH", Now.AddDays(90), UserId, Now);

        Assert.Equal((agent.OrganizationId, agent.Id, Hash, "ABCDEFGH"), (credential.OrganizationId, credential.AgentId, credential.TokenHash, credential.Prefix));
        Assert.Equal([AgentScopes.ContextRead, AgentScopes.TasksWrite], credential.Scopes);
        Assert.Throws<ArgumentException>(() => new AgentCredential(agent, "CI", [], Hash, "ABCDEFGH", Now.AddDays(90), UserId, Now));
        Assert.Throws<ArgumentException>(() => new AgentCredential(agent, "CI", ReadScopes, Hash.ToLowerInvariant(), "ABCDEFGH", Now.AddDays(90), UserId, Now));
        Assert.Throws<ArgumentException>(() => new AgentCredential(agent, "CI", ReadScopes, Hash, "ABC", Now.AddDays(90), UserId, Now));
    }

    [Fact]
    public void Credential_Lifetime_MustBeBetweenOneAnd365Days()
    {
        var agent = new Agent(OrganizationId, "Agente", null, UserId);

        _ = new AgentCredential(agent, "Mínima", ReadScopes, Hash, "ABCDEFGH", Now.AddDays(1), UserId, Now);
        _ = new AgentCredential(agent, "Máxima", ReadScopes, Hash, "ABCDEFGH", Now.AddDays(365), UserId, Now);
        Assert.Throws<AgentRuleException>(() => new AgentCredential(agent, "Curta", ReadScopes, Hash, "ABCDEFGH", Now.AddHours(23), UserId, Now));
        Assert.Throws<AgentRuleException>(() => new AgentCredential(agent, "Longa", ReadScopes, Hash, "ABCDEFGH", Now.AddDays(366), UserId, Now));
    }

    [Fact]
    public void Credential_IsActiveUntilExpiredOrRevoked_AndTheFirstRevocationIsKept()
    {
        var agent = new Agent(OrganizationId, "Agente", null, UserId);
        var credential = new AgentCredential(agent, "CI", ReadScopes, Hash, "ABCDEFGH", Now.AddDays(30), UserId, Now);

        Assert.True(credential.IsActiveAt(Now.AddDays(30).AddTicks(-1)));
        Assert.False(credential.IsActiveAt(Now.AddDays(30)));

        var otherUser = Guid.CreateVersion7();
        credential.Revoke(UserId, Now.AddDays(1));
        credential.Revoke(otherUser, Now.AddDays(2));

        Assert.False(credential.IsActiveAt(Now.AddDays(1)));
        Assert.Equal((Now.AddDays(1), UserId), (credential.RevokedAt, credential.RevokedByUserId));
    }

    [Fact]
    public void Credential_CannotBeCreatedForADisabledAgent()
    {
        var agent = new Agent(OrganizationId, "Agente", null, UserId);
        agent.Disable(UserId, Now);

        var error = Assert.Throws<AgentRuleException>(() => new AgentCredential(agent, "CI", ReadScopes, Hash, "ABCDEFGH", Now.AddDays(30), UserId, Now));
        Assert.Equal(AgentCredential.DisabledAgentMessage, error.Message);
    }

    [Fact]
    public void ProjectAccess_AllowsContributorAndViewer_ButNeverManager()
    {
        var agent = new Agent(OrganizationId, "Agente", null, UserId);
        var project = new Project(OrganizationId, "Projeto");

        var access = new AgentProjectAccess(agent, project, ProjectRole.Viewer, UserId);
        access.ChangeRole(ProjectRole.Contributor, UserId);

        Assert.Equal((agent.Id, project.Id, ProjectRole.Contributor), (access.AgentId, access.ProjectId, access.Role));
        Assert.Throws<AgentRuleException>(() => new AgentProjectAccess(agent, project, ProjectRole.Manager, UserId));
        Assert.Throws<AgentRuleException>(() => access.ChangeRole(ProjectRole.Manager, UserId));
        Assert.Equal(ProjectRole.Contributor, access.Role);
    }

    [Fact]
    public void ProjectAccess_RequiresTheAgentAndTheProjectInTheSameOrganization()
    {
        var agent = new Agent(OrganizationId, "Agente", null, UserId);
        var foreignProject = new Project(Guid.CreateVersion7(), "Outra organização");

        Assert.Throws<ArgumentException>(() => new AgentProjectAccess(agent, foreignProject, ProjectRole.Viewer, UserId));
    }

    [Fact]
    public void AgentsManage_IsInTheCatalog_AndOnlyOwnersAndAdministratorsReceiveIt()
    {
        var roles = Role.CreateSystemRoles(OrganizationId);

        Assert.True(OrganizationPermissions.Catalog.ContainsKey(OrganizationPermissions.AgentsManage));
        Assert.Equal(
            [SystemRole.Owner, SystemRole.Administrator],
            roles.Where(role => role.Grants(OrganizationPermissions.AgentsManage)).Select(role => role.SystemRole!.Value));
    }
}
