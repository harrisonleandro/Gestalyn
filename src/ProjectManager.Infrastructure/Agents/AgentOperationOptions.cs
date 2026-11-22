using ProjectManager.Domain.Agents;

namespace ProjectManager.Infrastructure.Agents;

/// <summary>Rate limiting por credencial (seção <c>RateLimiting:Agents</c>, ADR-016, D11): janela fixa no relógio do banco.</summary>
public sealed class AgentRateLimitOptions
{
    public const string SectionName = "RateLimiting:Agents";

    public int PermitLimit { get; set; } = 120;

    public int WindowSeconds { get; set; } = 60;

    internal bool IsValid() => PermitLimit > 0 && WindowSeconds is > 0 and <= 3600;
}

/// <summary>Retenção dos registros de agentes (seção <c>Agents:Retention</c>, ADR-016, D12).</summary>
public sealed class AgentRetentionOptions
{
    public const string SectionName = "Agents:Retention";

    /// <summary>Desligado só em testes, que executam o ciclo diretamente.</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Nunca menor que o prazo do gatilho do banco (<see cref="AgentOperation.MinRetentionDays"/>).</summary>
    public int OperationRetentionDays { get; set; } = AgentOperation.MinRetentionDays;

    public int IdempotencyRetentionDays { get; set; } = 7;

    public int CounterRetentionDays { get; set; } = 1;

    public int BatchSize { get; set; } = 1_000;

    internal bool IsValid() =>
        Interval > TimeSpan.Zero
        && InitialDelay >= TimeSpan.Zero
        && OperationRetentionDays >= AgentOperation.MinRetentionDays
        && IdempotencyRetentionDays >= 1
        && CounterRetentionDays >= 1
        && BatchSize is > 0 and <= 10_000;
}

/// <summary>Estado da gravação da auditoria, lido pelo health check <c>agents-audit</c>.</summary>
public sealed class AgentAuditHealth
{
    private long _lastFailureTicks;
    private long _lastSuccessTicks;

    public DateTimeOffset? LastFailureAt => Read(ref _lastFailureTicks);

    public DateTimeOffset? LastSuccessAt => Read(ref _lastSuccessTicks);

    /// <summary>Degradado enquanto a última falha for mais recente que o último sucesso.</summary>
    public bool IsDegraded => LastFailureAt is { } failure && (LastSuccessAt is not { } success || failure >= success);

    internal void Succeeded(DateTimeOffset at) => Interlocked.Exchange(ref _lastSuccessTicks, at.UtcTicks);

    internal void Failed(DateTimeOffset at) => Interlocked.Exchange(ref _lastFailureTicks, at.UtcTicks);

    private static DateTimeOffset? Read(ref long ticks)
    {
        var value = Interlocked.Read(ref ticks);
        return value == 0 ? null : new DateTimeOffset(value, TimeSpan.Zero);
    }
}

/// <summary>Estado da limpeza, lido pelo health check <c>agents-retention</c>.</summary>
public sealed class AgentRetentionHealth(TimeProvider timeProvider)
{
    private readonly DateTimeOffset _startedAt = timeProvider.GetUtcNow();
    private long _lastCycleTicks;

    /// <summary>Último ciclo bem-sucedido ou pulado porque outra instância estava limpando.</summary>
    public DateTimeOffset? LastCycleAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastCycleTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>Degradado sem ciclo há mais de 3 intervalos (contados da inicialização, enquanto não houver nenhum).</summary>
    public bool IsDegraded(AgentRetentionOptions options, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(options);
        var reference = LastCycleAt ?? _startedAt + options.InitialDelay;
        return now - reference > 3 * options.Interval;
    }

    internal void CycleCompleted(DateTimeOffset at) => Interlocked.Exchange(ref _lastCycleTicks, at.UtcTicks);
}
