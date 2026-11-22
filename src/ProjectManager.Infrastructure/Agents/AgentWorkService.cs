using Microsoft.EntityFrameworkCore;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Activity;
using ProjectManager.Application.Agents;
using ProjectManager.Application.Dashboards;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Documents;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Workflows;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Agents;

/// <summary>
/// Operações do próprio agente (ADR-016). As consultas passam pelo filtro da organização da credencial; o progresso
/// vem da regra única (<see cref="IDashboardService"/>) e as atividades, do serviço de atividades.
/// </summary>
internal sealed class AgentWorkService(
    ProjectManagerDbContext dbContext,
    IDashboardService dashboards,
    IActivityService activities) : IAgentWorkService
{
    public async Task<AgentMe> GetMeAsync(ActiveAgent agent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);

        var found = await dbContext.Agents.AsNoTracking().SingleAsync(a => a.Id == agent.AgentId, cancellationToken);
        var credential = await dbContext.AgentCredentials.AsNoTracking().SingleAsync(c => c.Id == agent.CredentialId, cancellationToken);

        return new AgentMe(
            found.Id,
            found.Name,
            found.OrganizationId,
            new AgentMeCredential(credential.Id, credential.Prefix, credential.Scopes, credential.ExpiresAt));
    }

    public async Task<IReadOnlyList<AgentProjectSummary>> ListProjectsAsync(ActiveAgent agent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);

        return await (
            from grant in dbContext.AgentProjectAccess.AsNoTracking()
            join project in dbContext.Projects on grant.ProjectId equals project.Id
            where grant.AgentId == agent.AgentId
            orderby project.Name, project.Id
            select new AgentProjectSummary(project.Id, project.Name, grant.Role, project.ArchivedAt != null)).ToListAsync(cancellationToken);
    }

    public async Task<AgentProjectContext> GetContextAsync(ProjectAccessGrant access, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);

        var projectId = access.ProjectId;
        var project = await dbContext.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new AgentContextProject(p.Id, p.Name, p.Description, p.ArchivedAt != null))
            .SingleAsync(cancellationToken);

        var states = await dbContext.WorkflowStates.AsNoTracking()
            .Where(state => state.ProjectId == projectId)
            .OrderBy(state => state.Position)
            .ToListAsync(cancellationToken);
        var requireIndependentApproval = await dbContext.Workflows
            .Where(workflow => workflow.ProjectId == projectId)
            .Select(workflow => workflow.RequireIndependentApproval)
            .SingleAsync(cancellationToken);
        var transitions = await dbContext.WorkflowTransitions.AsNoTracking()
            .Where(transition => transition.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        // Mesma regra das transições disponíveis (TransitionRules): agentes nunca aprovam nem entram em Concluída.
        var actor = new TransitionActor(access.ActorKind, CanApprove: access.HasPermission(ProjectPermissions.TasksApprove), IsAssignee: false);
        var canMove = !project.IsArchived && access.HasPermission(ProjectPermissions.TasksWrite);
        var categories = states.ToDictionary(state => state.Id, state => state.Category);

        var decisions = await dbContext.Decisions.AsNoTracking()
            .Where(decision => decision.ProjectId == projectId && decision.Status == DecisionStatus.Accepted)
            .OrderByDescending(decision => decision.DecidedAt)
            .Take(AgentProjectContext.MaxDecisions)
            .Select(decision => new AgentContextDecision(
                decision.Id,
                decision.Title,
                decision.Outcome.Length > AgentProjectContext.OutcomeMaxLength
                    ? decision.Outcome.Substring(0, AgentProjectContext.OutcomeMaxLength)
                    : decision.Outcome,
                decision.TaskId,
                decision.DecidedAt))
            .ToListAsync(cancellationToken);

        var documents = await dbContext.ProjectDocuments.AsNoTracking()
            .Where(document => document.ProjectId == projectId && document.ArchivedAt == null)
            .OrderBy(document => document.Title)
            .Take(AgentProjectContext.MaxDocuments)
            .Select(document => new AgentContextDocument(document.Id, document.Title, document.CurrentVersion, document.UpdatedAt))
            .ToListAsync(cancellationToken);

        var recent = await activities.ListProjectAsync(projectId, new ActivityListQuery { PageSize = AgentProjectContext.MaxActivities }, cancellationToken);

        return new AgentProjectContext(
            project,
            access.Role!.Value,
            access.Permissions.Order(StringComparer.Ordinal).ToList(),
            states.ConvertAll(state => new AgentContextState(state.Id, state.Name, state.Category, state.Position, state.IsInitial, state.IsFinal)),
            transitions.ConvertAll(transition => new AgentContextTransition(
                transition.Id,
                transition.FromStateId,
                transition.ToStateId,
                transition.RequiresApproval,
                canMove && TransitionRules.IsOffered(transition.RequiresApproval, categories[transition.ToStateId], actor, requireIndependentApproval))),
            await dashboards.GetProgressAsync(projectId, cancellationToken),
            decisions,
            documents,
            recent.Items.Select(item => new AgentContextActivity(item.OccurredAt, item.ActorKind, item.ActorName, item.Action, item.Summary)).ToList());
    }
}
