using Microsoft.EntityFrameworkCore;
using ProjectManager.Application.Abstractions.Authentication;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Agents;

/// <summary>
/// Busca a credencial pelo SHA-256 do token (ADR-016). A organização do agente só é conhecida depois desta busca;
/// por isso ela ignora o filtro por organização, sempre restrita à igualdade do hash, que tem índice único.
/// </summary>
internal sealed class AgentCredentialDirectory(ProjectManagerDbContext dbContext) : IAgentCredentialDirectory
{
    public async Task<AgentCredentialMatch?> FindByTokenAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        var hash = AgentCredentialTokens.Hash(token);
        var match = await (
            from credential in dbContext.AgentCredentials.IgnoreQueryFilters().AsNoTracking()
            join agent in dbContext.Agents.IgnoreQueryFilters().AsNoTracking()
                on new { credential.OrganizationId, credential.AgentId } equals new { agent.OrganizationId, AgentId = agent.Id }
            where credential.TokenHash == hash
            select new
            {
                credential.Id,
                credential.AgentId,
                credential.OrganizationId,
                Scopes = EF.Property<string[]>(credential, "_scopes"),
                credential.ExpiresAt,
                IsRevoked = credential.RevokedAt != null,
                AgentStatus = agent.Status,
            }).SingleOrDefaultAsync(cancellationToken);

        return match is null
            ? null
            : new AgentCredentialMatch(
                match.Id,
                match.AgentId,
                match.OrganizationId,
                match.Scopes,
                match.ExpiresAt,
                match.IsRevoked,
                match.AgentStatus == Domain.Agents.AgentStatus.Active);
    }
}
