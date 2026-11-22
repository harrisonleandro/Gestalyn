using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Agents;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Workflows;

namespace ProjectManager.UnitTests.Application;

/// <summary>
/// Regra única de acesso de agentes (ADR-016, D4): papel do projeto ∩ permissões possíveis para agentes ∩ escopos da
/// credencial, com o motivo de cada recusa. <c>tasks.approve</c> nunca entra.
/// </summary>
public sealed class AgentAccessGrantTests
{
    private static readonly Guid ProjectId = Guid.CreateVersion7();

    private static readonly string[] ManagementPermissions =
    [
        ProjectPermissions.TasksApprove,
        ProjectPermissions.Update,
        ProjectPermissions.Archive,
        ProjectPermissions.MembersManage,
        ProjectPermissions.Plan,
        ProjectPermissions.TasksComment,
        ProjectPermissions.TasksArchive,
        ProjectPermissions.WorkflowManage,
        ProjectPermissions.DocumentsManage,
    ];

    [Fact]
    public void RequirementTable_MatchesTheApprovedScopes()
    {
        Assert.Equal(
            [
                (AgentScopes.ContextRead, ProjectPermissions.Read, AgentOperationKind.Read),
                (AgentScopes.TasksRead, ProjectPermissions.Read, AgentOperationKind.Read),
                (AgentScopes.TasksWrite, ProjectPermissions.TasksWrite, AgentOperationKind.Write),
                (AgentScopes.ActivitiesWrite, ProjectPermissions.DocumentsWrite, AgentOperationKind.Write),
                (AgentScopes.ReportsWrite, ProjectPermissions.TasksWrite, AgentOperationKind.Write),
            ],
            AgentRequirement.All.Select(r => (r.Scope, r.Permission, r.Kind)));
        Assert.Equal(AgentScopes.All, AgentRequirement.All.Select(r => r.Scope));
    }

    [Fact]
    public void Contributor_WithAllScopes_HasExactlyTheThreeAgentPermissions()
    {
        var agent = Agent(AgentScopes.All);

        foreach (var requirement in AgentRequirement.All)
        {
            var decision = AgentAccessGrant.Compute(agent, agentIsActive: true, ProjectId, ProjectRole.Contributor, requirement);

            Assert.Equal(AgentAccessOutcome.Allowed, decision.Outcome);
            Assert.Equal(
                [ProjectPermissions.DocumentsWrite, ProjectPermissions.Read, ProjectPermissions.TasksWrite],
                decision.Grant!.Permissions.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void Viewer_ReadsButNeverWrites_WhateverTheScopes()
    {
        var agent = Agent(AgentScopes.All);

        Assert.Equal(AgentAccessOutcome.Allowed, Outcome(agent, ProjectRole.Viewer, AgentRequirement.ContextRead));
        Assert.Equal(AgentAccessOutcome.Allowed, Outcome(agent, ProjectRole.Viewer, AgentRequirement.TasksRead));
        Assert.Equal(AgentAccessOutcome.PermissionMissing, Outcome(agent, ProjectRole.Viewer, AgentRequirement.TasksWrite));
        Assert.Equal(AgentAccessOutcome.PermissionMissing, Outcome(agent, ProjectRole.Viewer, AgentRequirement.ActivitiesWrite));
        Assert.Equal(AgentAccessOutcome.PermissionMissing, Outcome(agent, ProjectRole.Viewer, AgentRequirement.ReportsWrite));
        Assert.Equal([ProjectPermissions.Read], AgentAccessGrant.PermissionsFor(ProjectRole.Viewer, agent.Scopes));
    }

    [Fact]
    public void MissingScope_IsRefused_AndLimitsThePermissions()
    {
        var readOnly = Agent([AgentScopes.TasksRead]);
        var writeOnly = Agent([AgentScopes.TasksWrite]);

        Assert.Equal(AgentAccessOutcome.Allowed, Outcome(readOnly, ProjectRole.Contributor, AgentRequirement.TasksRead));
        Assert.Equal(AgentAccessOutcome.ScopeMissing, Outcome(readOnly, ProjectRole.Contributor, AgentRequirement.ContextRead));
        Assert.Equal(AgentAccessOutcome.ScopeMissing, Outcome(readOnly, ProjectRole.Contributor, AgentRequirement.TasksWrite));
        Assert.Equal([ProjectPermissions.Read], AgentAccessGrant.PermissionsFor(ProjectRole.Contributor, readOnly.Scopes));

        Assert.Equal(AgentAccessOutcome.Allowed, Outcome(writeOnly, ProjectRole.Contributor, AgentRequirement.TasksWrite));
        Assert.Equal(AgentAccessOutcome.ScopeMissing, Outcome(writeOnly, ProjectRole.Contributor, AgentRequirement.TasksRead));
        Assert.Equal([ProjectPermissions.TasksWrite], AgentAccessGrant.PermissionsFor(ProjectRole.Contributor, writeOnly.Scopes));
    }

    [Fact]
    public void ProjectWithoutGrant_OrWithAManagerRole_IsNotVisible_BeforeAnyScopeCheck()
    {
        var noScopes = Agent([AgentScopes.ContextRead]);

        foreach (var requirement in AgentRequirement.All)
        {
            Assert.Equal(AgentAccessOutcome.ProjectNotVisible, Outcome(Agent(AgentScopes.All), role: null, requirement));
            Assert.Equal(AgentAccessOutcome.ProjectNotVisible, Outcome(Agent(AgentScopes.All), ProjectRole.Manager, requirement));
            Assert.Equal(AgentAccessOutcome.ProjectNotVisible, Outcome(noScopes, role: null, requirement));
        }
    }

    [Fact]
    public void DisabledAgent_IsRefusedFirst()
    {
        var decision = AgentAccessGrant.Compute(Agent(AgentScopes.All), agentIsActive: false, ProjectId, ProjectRole.Contributor, AgentRequirement.TasksRead);

        Assert.Equal(AgentAccessOutcome.AgentDisabled, decision.Outcome);
        Assert.Null(decision.Grant);
    }

    [Fact]
    public void ManagementPermissions_NeverReachAnAgent_ForAnyRoleOrScopeSet()
    {
        // Cada camada recusa sozinha: as permissões elegíveis não incluem nenhuma de gestão, mesmo que um escopo novo
        // um dia apontasse para uma delas.
        Assert.Empty(AgentAccessGrant.EligiblePermissions.Intersect(ManagementPermissions));
        Assert.DoesNotContain(AgentRequirement.All, requirement => ManagementPermissions.Contains(requirement.Permission));

        var everyScopeSet = Enumerable.Range(0, 1 << AgentScopes.All.Count)
            .Select(mask => AgentScopes.All.Where((_, index) => (mask & (1 << index)) != 0).ToHashSet(StringComparer.Ordinal));

        foreach (var scopes in everyScopeSet)
        {
            foreach (var role in Enum.GetValues<ProjectRole>())
            {
                var permissions = AgentAccessGrant.PermissionsFor(role, scopes);
                Assert.Empty(permissions.Intersect(ManagementPermissions));
                Assert.Subset(AgentAccessGrant.EligiblePermissions.ToHashSet(), permissions.ToHashSet());
            }
        }
    }

    [Fact]
    public void Grant_IdentifiesTheAgentAsTheActor()
    {
        var agent = Agent(AgentScopes.All);

        var grant = AgentAccessGrant.Compute(agent, agentIsActive: true, ProjectId, ProjectRole.Contributor, AgentRequirement.TasksWrite).Grant!;

        Assert.Equal((ProjectId, ProjectRole.Contributor, agent.AgentId, ActorKind.Agent, Guid.Empty), (grant.ProjectId, grant.Role, grant.AgentId, grant.ActorKind, grant.UserId));
        Assert.False(grant.HasPermission(ProjectPermissions.TasksApprove));
    }

    private static AgentAccessOutcome Outcome(ActiveAgent agent, ProjectRole? role, AgentRequirement requirement) =>
        AgentAccessGrant.Compute(agent, agentIsActive: true, ProjectId, role, requirement).Outcome;

    private static ActiveAgent Agent(IEnumerable<string> scopes) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), scopes.ToHashSet(StringComparer.Ordinal));
}
