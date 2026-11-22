using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Agents;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Agents;

/// <summary>
/// Rate limiting por credencial no PostgreSQL (ADR-016, D11). Um único upsert por requisição, atômico sob
/// concorrência (o <c>ON CONFLICT</c> trava a linha) e correto com várias instâncias, porque a janela usa o relógio do
/// banco. SQL bruto autorizado pelo ADR-016: o EF Core não expressa o upsert com <c>RETURNING</c> de forma atômica.
/// A primeira requisição da janela grava o último uso da credencial: no máximo uma gravação por janela.
/// </summary>
internal sealed class AgentRateLimiter(ProjectManagerDbContext dbContext, IOptions<AgentRateLimitOptions> options) : IAgentRateLimiter
{
    public async Task<AgentRateLimitHit> HitAsync(ActiveAgent agent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);

        var settings = options.Value;
        var window = settings.WindowSeconds;
        var counter = await dbContext.Database
            .SqlQuery<CounterRow>($"""
                INSERT INTO agent_rate_limit_counters (credential_id, organization_id, window_start, request_count)
                VALUES ({agent.CredentialId}, {agent.OrganizationId}, to_timestamp(floor(extract(epoch FROM now()) / {window}) * {window}), 1)
                ON CONFLICT (credential_id, window_start)
                DO UPDATE SET request_count = agent_rate_limit_counters.request_count + 1
                RETURNING request_count, window_start, now() AS database_now
                """)
            .ToListAsync(cancellationToken);
        var row = counter.Single();

        if (row.RequestCount == 1)
        {
            await dbContext.AgentCredentials
                .Where(credential => credential.Id == agent.CredentialId)
                .ExecuteUpdateAsync(set => set.SetProperty(credential => credential.LastUsedAt, row.DatabaseNow), cancellationToken);
        }

        return new AgentRateLimitHit(row.RequestCount, settings.PermitLimit, row.WindowStart.AddSeconds(window), row.DatabaseNow);
    }

    private sealed class CounterRow
    {
        public int RequestCount { get; init; }

        public DateTimeOffset WindowStart { get; init; }

        public DateTimeOffset DatabaseNow { get; init; }
    }
}
