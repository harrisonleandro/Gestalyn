using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Agents;

/// <summary>Resultado da autorização de uma requisição de agente (ADR-016, D10).</summary>
public enum AgentAuthorizationResult
{
    Allowed,
    CredentialRevoked,
    CredentialExpired,
    AgentDisabled,
    OrganizationMismatch,
    ProjectNotVisible,
    ScopeMissing,
    PermissionMissing,
    BusinessRuleDenied,
    RateLimited,
}

/// <summary>
/// Uma requisição de agente com credencial identificada (ADR-016, D10), inclusive as negadas, as de credenciais
/// revogadas ou expiradas e a primeira bloqueada por limite em cada janela. Nunca guarda token, prefixo, hash,
/// cabeçalhos, corpos ou e-mails. Imutável no banco; só a limpeza de retenção apaga, depois de 180 dias.
/// </summary>
public sealed class AgentOperation : Entity, IOrganizationOwned
{
    public const int MethodMaxLength = 10;
    public const int RouteTemplateMaxLength = 200;
    public const int DenialCodeMaxLength = 64;
    public const int CorrelationIdMaxLength = 100;

    /// <summary>Prazo mínimo de retenção, o mesmo do gatilho do banco: mudá-lo exige uma migração (D12).</summary>
    public const int MinRetentionDays = 180;

    public AgentOperation(
        Guid organizationId,
        Guid agentId,
        Guid credentialId,
        DateTimeOffset occurredAt,
        int durationMs,
        string httpMethod,
        string routeTemplate,
        Guid? projectId,
        Guid? taskId,
        int statusCode,
        AgentAuthorizationResult authorizationResult,
        string? denialCode,
        string? correlationId,
        bool idempotentReplay,
        ActivityOrigin clientOrigin)
    {
        OrganizationId = Guard.RequiredId(organizationId, nameof(organizationId));
        AgentId = Guard.RequiredId(agentId, nameof(agentId));
        CredentialId = Guard.RequiredId(credentialId, nameof(credentialId));
        OccurredAt = Timestamps.TruncateToMicroseconds(occurredAt);
        DurationMs = Math.Max(0, durationMs);
        HttpMethod = Guard.RequiredText(httpMethod, MethodMaxLength, nameof(httpMethod));
        RouteTemplate = Truncate(Guard.RequiredText(routeTemplate, int.MaxValue, nameof(routeTemplate)), RouteTemplateMaxLength)!;
        ProjectId = projectId;
        TaskId = taskId;
        StatusCode = statusCode;
        AuthorizationResult = authorizationResult;
        DenialCode = Truncate(denialCode, DenialCodeMaxLength);
        CorrelationId = Truncate(correlationId, CorrelationIdMaxLength);
        IdempotentReplay = idempotentReplay;
        ClientOrigin = clientOrigin;
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private AgentOperation(string httpMethod, string routeTemplate)
    {
        HttpMethod = httpMethod;
        RouteTemplate = routeTemplate;
    }

    public Guid OrganizationId { get; private set; }

    public Guid AgentId { get; private set; }

    public Guid CredentialId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public int DurationMs { get; private set; }

    public string HttpMethod { get; private set; }

    /// <summary>O modelo da rota (<c>/api/v1/agents/tasks/{taskId}</c>), nunca a URL com valores nem a query.</summary>
    public string RouteTemplate { get; private set; }

    public Guid? ProjectId { get; private set; }

    public Guid? TaskId { get; private set; }

    public int StatusCode { get; private set; }

    public AgentAuthorizationResult AuthorizationResult { get; private set; }

    public string? DenialCode { get; private set; }

    /// <summary>O <c>TraceIdentifier</c> da requisição: o mesmo dos logs e do <c>RequestId</c> dos eventos.</summary>
    public string? CorrelationId { get; private set; }

    public bool IdempotentReplay { get; private set; }

    /// <summary>Origem informada em <c>X-Gestalyn-Client</c>; só informativa.</summary>
    public ActivityOrigin ClientOrigin { get; private set; }

    private static string? Truncate(string? value, int maxLength) =>
        value is { Length: > 0 } text ? (text.Length > maxLength ? text[..maxLength] : text) : null;
}

/// <summary>Contador de requisições por credencial numa janela fixa (ADR-016, D11). Dado operacional, apagado em 1 dia.</summary>
public sealed class AgentRateLimitCounter : IOrganizationOwned
{
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização; as linhas são gravadas só pelo upsert do rate limiting.")]
    private AgentRateLimitCounter()
    {
    }

    public Guid CredentialId { get; private set; }

    public Guid OrganizationId { get; private set; }

    public DateTimeOffset WindowStart { get; private set; }

    public int RequestCount { get; private set; }
}
