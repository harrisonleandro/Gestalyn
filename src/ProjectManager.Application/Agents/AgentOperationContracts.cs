using System.ComponentModel.DataAnnotations;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Common;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Agents;

namespace ProjectManager.Application.Agents;

/// <summary>O que fica de uma requisição de agente (ADR-016, D10). Nada de token, cabeçalhos ou corpos.</summary>
public sealed record AgentOperationEntry(
    Guid OrganizationId,
    Guid AgentId,
    Guid CredentialId,
    DateTimeOffset OccurredAt,
    int DurationMs,
    string HttpMethod,
    string RouteTemplate,
    Guid? ProjectId,
    Guid? TaskId,
    int StatusCode,
    AgentAuthorizationResult AuthorizationResult,
    string? DenialCode,
    string? CorrelationId,
    bool IdempotentReplay,
    ActivityOrigin ClientOrigin);

/// <summary>
/// Grava a operação com um escopo e um <c>DbContext</c> próprios, fora da transação da requisição: a linha
/// sobrevive a um rollback. Uma falha nunca derruba a requisição: fica um log de erro e o health check
/// <c>agents-audit</c> fica <c>Degraded</c>.
/// </summary>
public interface IAgentOperationWriter
{
    Task WriteAsync(AgentOperationEntry entry, CancellationToken cancellationToken);
}

/// <summary>Contagem da credencial na janela atual, pelo relógio do banco (D11).</summary>
public sealed record AgentRateLimitHit(int RequestCount, int PermitLimit, DateTimeOffset WindowEnd, DateTimeOffset DatabaseNow)
{
    public bool IsLimited => RequestCount > PermitLimit;

    /// <summary>Só a primeira rejeição da janela vai para a auditoria, para não multiplicar gravações numa enxurrada.</summary>
    public bool IsFirstRejection => RequestCount == PermitLimit + 1;

    public int RetryAfterSeconds => Math.Max(1, (int)Math.Ceiling((WindowEnd - DatabaseNow).TotalSeconds));
}

/// <summary>
/// Rate limiting por credencial no PostgreSQL (D11): um único upsert atômico por requisição. A primeira requisição
/// da janela grava também o último uso da credencial, no máximo uma vez por janela.
/// </summary>
public interface IAgentRateLimiter
{
    Task<AgentRateLimitHit> HitAsync(ActiveAgent agent, CancellationToken cancellationToken);
}

/// <summary>Operação na leitura por pessoas com <c>agents.manage</c>.</summary>
public sealed record AgentOperationItem(
    Guid Id,
    Guid CredentialId,
    string? CredentialName,
    DateTimeOffset OccurredAt,
    int DurationMs,
    string HttpMethod,
    string RouteTemplate,
    Guid? ProjectId,
    Guid? TaskId,
    int StatusCode,
    AgentAuthorizationResult AuthorizationResult,
    string? DenialCode,
    string? CorrelationId,
    bool IdempotentReplay,
    ActivityOrigin ClientOrigin);

public sealed class AgentOperationListQuery : PageQuery, IValidatableObject
{
    public AgentAuthorizationResult? Result { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From is { } from && To is { } to && from > to)
        {
            yield return new ValidationResult("O início do período deve vir antes do fim.", [nameof(From), nameof(To)]);
        }
    }
}

public interface IAgentOperationService
{
    /// <summary>Operações do agente, da mais recente para a mais antiga. Agente de outra organização: 404.</summary>
    Task<Result<PagedResult<AgentOperationItem>>> ListAsync(Guid agentId, AgentOperationListQuery query, CancellationToken cancellationToken);
}
