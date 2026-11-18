using Microsoft.EntityFrameworkCore;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Agents;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Agents;

/// <summary>Operações do próprio agente (ADR-016). As consultas passam pelo filtro da organização da credencial.</summary>
internal sealed class AgentWorkService(ProjectManagerDbContext dbContext) : IAgentWorkService
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
}
