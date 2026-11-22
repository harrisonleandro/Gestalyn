using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Agents;

/// <summary>Resultado de um ciclo de limpeza.</summary>
public sealed record AgentRetentionResult(bool Skipped, int Operations, int IdempotencyRecords, int RateLimitCounters);

/// <summary>
/// Um ciclo de limpeza (ADR-016, D12). Exceção de isolamento autorizada pelo ADR-016: atravessa organizações, só com
/// <c>DELETE</c> em lotes dos registros vencidos de <c>agent_operations</c>, <c>idempotency_records</c> e
/// <c>agent_rate_limit_counters</c>, e a trava consultiva que garante uma instância por vez. O gatilho de
/// <c>agent_operations</c> recusa qualquer exclusão de linha com menos de 180 dias, mesmo que este código erre.
/// </summary>
public sealed partial class AgentRetentionCleaner(
    ProjectManagerDbContext dbContext,
    IOptions<AgentRetentionOptions> options,
    ILogger<AgentRetentionCleaner> logger)
{
    /// <summary>Chave fixa da trava consultiva da limpeza (ASCII de "gst-rtn").</summary>
    private const long LockKey = 0x6773742D72746E;

    public async Task<AgentRetentionResult> RunAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var stopwatch = Stopwatch.StartNew();
        var database = dbContext.Database;

        await database.OpenConnectionAsync(cancellationToken);
        try
        {
            var locked = await database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({LockKey}) AS \"Value\"").SingleAsync(cancellationToken);
            if (!locked)
            {
                LogSkipped(logger);
                return new AgentRetentionResult(Skipped: true, 0, 0, 0);
            }

            try
            {
                var operations = await DeleteInBatchesAsync(
                    "agent_operations", "occurred_at", settings.OperationRetentionDays, settings.BatchSize, cancellationToken);
                var idempotency = await DeleteInBatchesAsync(
                    "idempotency_records", "created_at", settings.IdempotencyRetentionDays, settings.BatchSize, cancellationToken);
                var counters = await DeleteInBatchesAsync(
                    "agent_rate_limit_counters", "window_start", settings.CounterRetentionDays, settings.BatchSize, cancellationToken);

                LogCompleted(logger, operations, idempotency, counters, stopwatch.ElapsedMilliseconds);
                return new AgentRetentionResult(Skipped: false, operations, idempotency, counters);
            }
            finally
            {
                await database.SqlQuery<bool>($"SELECT pg_advisory_unlock({LockKey}) AS \"Value\"").SingleAsync(CancellationToken.None);
            }
        }
        finally
        {
            await database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Lotes de até <paramref name="batchSize"/> linhas, cada um na própria transação e com <c>statement_timeout</c> de
    /// 30 segundos. Tabela e coluna são constantes deste arquivo, nunca entrada externa.
    /// </summary>
    [SuppressMessage("Security", "EF1002:Risk of vulnerability to SQL injection", Justification = "Tabela e coluna são constantes internas; prazo e lote vão como parâmetros.")]
    private async Task<int> DeleteInBatchesAsync(string table, string column, int days, int batchSize, CancellationToken cancellationToken)
    {
        var total = 0;
        int deleted;
        do
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync("SET LOCAL statement_timeout = '30s'", cancellationToken);
            deleted = await dbContext.Database.ExecuteSqlRawAsync(
                $$"""
                DELETE FROM {{table}} WHERE ctid IN (
                    SELECT ctid FROM {{table}} WHERE {{column}} < now() - make_interval(days => {0}) LIMIT {1})
                """,
                [days, batchSize],
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            total += deleted;
        }
        while (deleted == batchSize);

        return total;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Limpeza de agentes pulada: outra instância está limpando.")]
    private static partial void LogSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Limpeza de agentes: {Operations} operações, {IdempotencyRecords} registros de idempotência e {RateLimitCounters} contadores em {ElapsedMs} ms.")]
    private static partial void LogCompleted(ILogger logger, int operations, int idempotencyRecords, int rateLimitCounters, long elapsedMs);
}

/// <summary>
/// Agenda da limpeza (ADR-016, D12): <c>PeriodicTimer</c> hospedado na API, com atraso inicial. Cada ciclo usa um
/// escopo próprio; uma falha é registrada e o próximo ciclo tenta de novo, sem derrubar a API.
/// </summary>
internal sealed partial class AgentRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<AgentRetentionOptions> options,
    AgentRetentionHealth health,
    TimeProvider timeProvider,
    ILogger<AgentRetentionService> logger) : BackgroundService
{
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A limpeza nunca derruba a API (D12): a falha é registrada e o próximo ciclo tenta de novo.")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        try
        {
            await Task.Delay(settings.InitialDelay, timeProvider, stoppingToken);
            using var timer = new PeriodicTimer(settings.Interval, timeProvider);
            do
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<AgentRetentionCleaner>().RunAsync(stoppingToken);
                    health.CycleCompleted(timeProvider.GetUtcNow());
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogCycleFailed(logger, exception);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento da API.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha no ciclo de limpeza de agentes; o próximo ciclo tenta de novo.")]
    private static partial void LogCycleFailed(ILogger logger, Exception exception);
}

/// <summary>Health checks dos agentes (ADR-016): auditoria (<c>agents-audit</c>) e limpeza (<c>agents-retention</c>).</summary>
internal sealed class AgentAuditHealthCheck(AgentAuditHealth health) : IHealthCheck
{
    public const string Name = "agents-audit";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(health.IsDegraded
            ? HealthCheckResult.Degraded("A última gravação da auditoria de agentes falhou.")
            : HealthCheckResult.Healthy("A auditoria de agentes está gravando."));
}

internal sealed class AgentRetentionHealthCheck(AgentRetentionHealth health, IOptions<AgentRetentionOptions> options, TimeProvider timeProvider) : IHealthCheck
{
    public const string Name = "agents-retention";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Limpeza de agentes desligada nesta instância."));
        }

        return Task.FromResult(health.IsDegraded(settings, timeProvider.GetUtcNow())
            ? HealthCheckResult.Degraded("Nenhum ciclo de limpeza de agentes há mais de 3 intervalos.")
            : HealthCheckResult.Healthy("A limpeza de agentes está em dia."));
    }
}
