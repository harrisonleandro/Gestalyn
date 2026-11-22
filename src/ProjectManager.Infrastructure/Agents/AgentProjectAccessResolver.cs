using Microsoft.EntityFrameworkCore;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Agents;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Agents;

/// <summary>
/// Lê a concessão do agente no projeto e o estado do agente, sempre pelo filtro da organização da credencial, e decide
/// por <see cref="AgentAccessGrant.Compute"/>. Projeto de outra organização, ou sem concessão, fica invisível.
/// </summary>
internal sealed class AgentProjectAccessResolver(ProjectManagerDbContext dbContext) : IAgentProjectAccess
{
    public async Task<AgentAccessDecision> FindAsync(ActiveAgent agent, Guid projectId, AgentRequirement requirement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);

        var access = await (
            from grant in dbContext.AgentProjectAccess.AsNoTracking()
            join project in dbContext.Projects on grant.ProjectId equals project.Id
            where grant.AgentId == agent.AgentId && grant.ProjectId == projectId
            select new { grant.Role }).SingleOrDefaultAsync(cancellationToken);
        var agentIsActive = await dbContext.Agents.AnyAsync(a => a.Id == agent.AgentId && a.Status == AgentStatus.Active, cancellationToken);

        return AgentAccessGrant.Compute(agent, agentIsActive, projectId, access?.Role, requirement);
    }

    public async Task<AgentAccessDecision> FindByTaskAsync(ActiveAgent agent, Guid taskId, AgentRequirement requirement, CancellationToken cancellationToken)
    {
        var projectId = await dbContext.Tasks.Where(task => task.Id == taskId).Select(task => (Guid?)task.ProjectId).SingleOrDefaultAsync(cancellationToken);

        return projectId is { } id
            ? await FindAsync(agent, id, requirement, cancellationToken)
            : new AgentAccessDecision(AgentAccessOutcome.ProjectNotVisible, Grant: null);
    }
}
