using Microsoft.EntityFrameworkCore;
using ProjectManager.Application.Agents;
using ProjectManager.Application.Common;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Agents;

/// <summary>Leitura das operações de um agente da organização ativa (ADR-016, D10), com filtros por resultado e período.</summary>
internal sealed class AgentOperationService(ProjectManagerDbContext dbContext) : IAgentOperationService
{
    public async Task<Result<PagedResult<AgentOperationItem>>> ListAsync(Guid agentId, AgentOperationListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await dbContext.Agents.AnyAsync(agent => agent.Id == agentId, cancellationToken))
        {
            return Result.Failure<PagedResult<AgentOperationItem>>(ErrorKind.NotFound, "Agente não encontrado.");
        }

        var operations = dbContext.AgentOperations.AsNoTracking().Where(operation => operation.AgentId == agentId);
        if (query.Result is { } result)
        {
            operations = operations.Where(operation => operation.AuthorizationResult == result);
        }

        if (query.From is { } from)
        {
            operations = operations.Where(operation => operation.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            operations = operations.Where(operation => operation.OccurredAt <= to);
        }

        var total = await operations.CountAsync(cancellationToken);
        var items = await operations
            .OrderByDescending(operation => operation.OccurredAt)
            .ThenByDescending(operation => operation.Id)
            .Skip(query.CalculateOffset())
            .Take(query.PageSize)
            .Select(operation => new AgentOperationItem(
                operation.Id,
                operation.CredentialId,
                dbContext.AgentCredentials.Where(c => c.Id == operation.CredentialId).Select(c => c.Name).FirstOrDefault(),
                operation.OccurredAt,
                operation.DurationMs,
                operation.HttpMethod,
                operation.RouteTemplate,
                operation.ProjectId,
                operation.TaskId,
                operation.StatusCode,
                operation.AuthorizationResult,
                operation.DenialCode,
                operation.CorrelationId,
                operation.IdempotentReplay,
                operation.ClientOrigin))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<AgentOperationItem>(items, query.Page, query.PageSize, total));
    }
}
