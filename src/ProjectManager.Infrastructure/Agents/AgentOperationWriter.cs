using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Agents;
using ProjectManager.Domain.Agents;
using ProjectManager.Infrastructure.MultiTenancy;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Agents;

/// <summary>
/// Grava <see cref="AgentOperation"/> num escopo de DI e num <c>DbContext</c> próprios (ADR-016, D10): fora da
/// transação da requisição, a linha sobrevive a um rollback da operação. A organização do escopo é a da credencial,
/// e o filtro de isolamento confere a gravação. Uma falha vira log de erro e deixa <c>agents-audit</c> degradado.
/// </summary>
internal sealed partial class AgentOperationWriter(
    IServiceScopeFactory scopeFactory,
    AgentAuditHealth health,
    TimeProvider timeProvider,
    ILogger<AgentOperationWriter> logger) : IAgentOperationWriter
{
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A auditoria nunca derruba a requisição (ADR-016, D10): a falha é registrada e sinalizada no health check.")]
    public async Task WriteAsync(AgentOperationEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetActiveAgent(
                new ActiveAgent(entry.OrganizationId, entry.AgentId, entry.CredentialId, new HashSet<string>(StringComparer.Ordinal)));

            var dbContext = scope.ServiceProvider.GetRequiredService<ProjectManagerDbContext>();
            dbContext.AgentOperations.Add(new AgentOperation(
                entry.OrganizationId,
                entry.AgentId,
                entry.CredentialId,
                entry.OccurredAt,
                entry.DurationMs,
                entry.HttpMethod,
                entry.RouteTemplate,
                entry.ProjectId,
                entry.TaskId,
                entry.StatusCode,
                entry.AuthorizationResult,
                entry.DenialCode,
                entry.CorrelationId,
                entry.IdempotentReplay,
                entry.ClientOrigin));
            await dbContext.SaveChangesAsync(cancellationToken);
            health.Succeeded(timeProvider.GetUtcNow());
        }
        catch (Exception exception)
        {
            health.Failed(timeProvider.GetUtcNow());
            LogWriteFailed(logger, exception, entry.CredentialId, entry.CorrelationId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao gravar a operação da credencial de agente {CredentialId} (requisição {CorrelationId}).")]
    private static partial void LogWriteFailed(ILogger logger, Exception exception, Guid credentialId, string? correlationId);
}
