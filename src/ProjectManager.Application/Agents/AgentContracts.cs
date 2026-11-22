using System.ComponentModel.DataAnnotations;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Common;
using ProjectManager.Domain.Agents;
using ProjectManager.Domain.Projects;

namespace ProjectManager.Application.Agents;

public sealed record AgentSummary(
    Guid Id,
    string Name,
    string? Description,
    AgentStatus Status,
    int ActiveCredentials,
    int Projects,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DisabledAt);

public sealed record AgentDetails(
    Guid Id,
    string Name,
    string? Description,
    AgentStatus Status,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DisabledAt,
    IReadOnlyList<AgentCredentialDetails> Credentials,
    IReadOnlyList<AgentProjectAccessDetails> Projects);

public enum AgentCredentialState
{
    Active,
    Expired,
    Revoked,
}

/// <summary>Credencial como aparece nas listas: o prefixo identifica, mas o token nunca volta.</summary>
public sealed record AgentCredentialDetails(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    AgentCredentialState State,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>Resposta da criação: a única vez em que o token completo é devolvido (ADR-016).</summary>
public sealed record CreatedAgentCredential(AgentCredentialDetails Credential, string Token);

public sealed record AgentProjectAccessDetails(Guid ProjectId, string ProjectName, ProjectRole Role, DateTimeOffset GrantedAt);

public sealed class CreateAgentRequest
{
    [NotWhiteSpace(ErrorMessage = "Informe o nome do agente.")]
    [StringLength(Agent.NameMaxLength, MinimumLength = 1, ErrorMessage = "O nome deve ter de {2} a {1} caracteres.")]
    public required string Name { get; init; }

    [StringLength(Agent.DescriptionMaxLength, ErrorMessage = "A descrição deve ter no máximo {1} caracteres.")]
    public string? Description { get; init; }
}

public sealed class UpdateAgentRequest
{
    private readonly string? _description;

    [NotWhiteSpace(ErrorMessage = "Informe o nome do agente.")]
    [StringLength(Agent.NameMaxLength, MinimumLength = 1, ErrorMessage = "O nome deve ter de {2} a {1} caracteres.")]
    public string? Name { get; init; }

    [StringLength(Agent.DescriptionMaxLength, ErrorMessage = "A descrição deve ter no máximo {1} caracteres.")]
    public string? Description
    {
        get => _description;
        init
        {
            _description = value;
            HasDescription = true;
        }
    }

    /// <summary>A descrição veio no corpo (inclusive nula, para apagar).</summary>
    public bool HasDescription { get; private init; }
}

public sealed class CreateAgentCredentialRequest
{
    [NotWhiteSpace(ErrorMessage = "Informe o nome da credencial.")]
    [StringLength(AgentCredential.NameMaxLength, MinimumLength = 1, ErrorMessage = "O nome deve ter de {2} a {1} caracteres.")]
    public required string Name { get; init; }

    [Required(ErrorMessage = "Informe os escopos.")]
    public required IReadOnlyList<string> Scopes { get; init; }

    /// <summary>Validade em dias. Sem valor, vale o padrão configurado (90 dias).</summary>
    [Range(AgentCredential.MinLifetimeDays, AgentCredential.MaxLifetimeDays, ErrorMessage = "A validade deve ser de {1} a {2} dias.")]
    public int? ExpiresInDays { get; init; }
}

public sealed class SetAgentProjectAccessRequest
{
    [Required(ErrorMessage = "Informe o papel do agente no projeto.")]
    public required ProjectRole Role { get; init; }
}

/// <summary>
/// Gestão de agentes por pessoas com <c>agents.manage</c> (ADR-016). Todas as operações atuam sobre a organização
/// ativa; o acesso a um projeto exige também <c>project.members.manage</c> naquele projeto.
/// </summary>
public interface IAgentService
{
    Task<IReadOnlyList<AgentSummary>> ListAsync(CancellationToken cancellationToken);

    Task<Result<AgentDetails>> GetAsync(Guid agentId, CancellationToken cancellationToken);

    Task<Result<AgentDetails>> CreateAsync(ActiveMembership actor, CreateAgentRequest request, CancellationToken cancellationToken);

    Task<Result<AgentDetails>> UpdateAsync(Guid agentId, UpdateAgentRequest request, CancellationToken cancellationToken);

    Task<Result<AgentDetails>> DisableAsync(ActiveMembership actor, Guid agentId, CancellationToken cancellationToken);

    Task<Result<AgentDetails>> EnableAsync(Guid agentId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<AgentCredentialDetails>>> ListCredentialsAsync(Guid agentId, CancellationToken cancellationToken);

    Task<Result<CreatedAgentCredential>> CreateCredentialAsync(
        ActiveMembership actor,
        Guid agentId,
        CreateAgentCredentialRequest request,
        CancellationToken cancellationToken);

    Task<Result<AgentCredentialDetails>> RevokeCredentialAsync(
        ActiveMembership actor,
        Guid agentId,
        Guid credentialId,
        CancellationToken cancellationToken);

    Task<Result<AgentProjectAccessDetails>> SetProjectAccessAsync(
        ActiveMembership actor,
        Guid agentId,
        Guid projectId,
        SetAgentProjectAccessRequest request,
        CancellationToken cancellationToken);

    Task<Result> RemoveProjectAccessAsync(ActiveMembership actor, Guid agentId, Guid projectId, CancellationToken cancellationToken);
}
