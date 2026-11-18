using ProjectManager.Application.Abstractions.MultiTenancy;

namespace ProjectManager.Application.Agents;

/// <summary>A credencial em uso, como o próprio agente a vê: nunca o token.</summary>
public sealed record AgentMeCredential(Guid Id, string Prefix, IReadOnlyList<string> Scopes, DateTimeOffset ExpiresAt);

public sealed record AgentMe(Guid AgentId, string Name, Guid OrganizationId, AgentMeCredential Credential);

/// <summary>Operações do próprio agente, sempre na organização da credencial (ADR-016).</summary>
public interface IAgentWorkService
{
    Task<AgentMe> GetMeAsync(ActiveAgent agent, CancellationToken cancellationToken);
}
