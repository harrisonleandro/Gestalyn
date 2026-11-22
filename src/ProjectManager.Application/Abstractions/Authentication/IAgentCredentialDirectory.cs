namespace ProjectManager.Application.Abstractions.Authentication;

/// <summary>Por que uma credencial de agente encontrada não autentica (ADR-016).</summary>
public enum AgentCredentialVerdict
{
    Valid,
    Revoked,
    Expired,
    AgentDisabled,
}

/// <summary>Credencial encontrada pelo token, com o necessário para decidir se ainda vale.</summary>
public sealed record AgentCredentialMatch(
    Guid CredentialId,
    Guid AgentId,
    Guid OrganizationId,
    IReadOnlyList<string> Scopes,
    DateTimeOffset ExpiresAt,
    bool IsRevoked,
    bool AgentIsActive)
{
    /// <summary>Revogação primeiro, depois o agente e por fim a validade: a resposta ao cliente é a mesma em todos os casos.</summary>
    public AgentCredentialVerdict Evaluate(DateTimeOffset now) =>
        IsRevoked ? AgentCredentialVerdict.Revoked
        : !AgentIsActive ? AgentCredentialVerdict.AgentDisabled
        : now >= ExpiresAt ? AgentCredentialVerdict.Expired
        : AgentCredentialVerdict.Valid;
}

/// <summary>
/// Busca da credencial de um agente pelo token, antes de existir organização ativa. É a exceção ao filtro por
/// organização autorizada pelo ADR-016: restrita à igualdade do hash do token.
/// </summary>
public interface IAgentCredentialDirectory
{
    Task<AgentCredentialMatch?> FindByTokenAsync(string token, CancellationToken cancellationToken);
}
