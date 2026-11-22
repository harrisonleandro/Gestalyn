using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Dashboards;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Workflows;

namespace ProjectManager.Application.Agents;

/// <summary>A credencial em uso, como o próprio agente a vê: nunca o token.</summary>
public sealed record AgentMeCredential(Guid Id, string Prefix, IReadOnlyList<string> Scopes, DateTimeOffset ExpiresAt);

public sealed record AgentMe(Guid AgentId, string Name, Guid OrganizationId, AgentMeCredential Credential);

/// <summary>Projeto concedido ao agente, com o papel dele.</summary>
public sealed record AgentProjectSummary(Guid Id, string Name, ProjectRole Role, bool IsArchived);

public sealed record AgentContextProject(Guid Id, string Name, string? Description, bool IsArchived);

public sealed record AgentContextState(Guid Id, string Name, StateCategory Category, int Position, bool IsInitial, bool IsFinal);

/// <summary>Transição do workflow; <c>AvailableToAgent</c> diz se este agente pode executá-la (ADR-016, D5).</summary>
public sealed record AgentContextTransition(Guid Id, Guid FromStateId, Guid ToStateId, bool RequiresApproval, bool AvailableToAgent);

/// <summary>Decisão aceita, com o resultado limitado a <see cref="AgentProjectContext.OutcomeMaxLength"/> caracteres.</summary>
public sealed record AgentContextDecision(Guid Id, string Title, string Outcome, Guid? TaskId, DateTimeOffset? DecidedAt);

/// <summary>Documento sem o conteúdo: título e versão atual.</summary>
public sealed record AgentContextDocument(Guid Id, string Title, int Version, DateTimeOffset UpdatedAt);

public sealed record AgentContextActivity(DateTimeOffset OccurredAt, ActorKind ActorKind, string? ActorName, string? Action, string Summary);

/// <summary>
/// Contexto de um projeto para um agente (ADR-016, D7), de tamanho limitado e sem dados pessoais além do nome de quem
/// aparece como autor de uma atividade: sem e-mails nem a lista de membros.
/// </summary>
public sealed record AgentProjectContext(
    AgentContextProject Project,
    ProjectRole Role,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<AgentContextState> States,
    IReadOnlyList<AgentContextTransition> Transitions,
    ProjectProgress Progress,
    IReadOnlyList<AgentContextDecision> AcceptedDecisions,
    IReadOnlyList<AgentContextDocument> Documents,
    IReadOnlyList<AgentContextActivity> RecentActivities)
{
    public const int MaxDecisions = 50;
    public const int MaxDocuments = 100;
    public const int MaxActivities = 20;
    public const int OutcomeMaxLength = 500;
}

/// <summary>Operações do próprio agente, sempre na organização da credencial (ADR-016).</summary>
public interface IAgentWorkService
{
    Task<AgentMe> GetMeAsync(ActiveAgent agent, CancellationToken cancellationToken);

    /// <summary>Projetos com concessão para o agente, em ordem de nome.</summary>
    Task<IReadOnlyList<AgentProjectSummary>> ListProjectsAsync(ActiveAgent agent, CancellationToken cancellationToken);

    /// <summary>Contexto do projeto da concessão, já autorizada por <see cref="AgentAccessGrant"/>.</summary>
    Task<AgentProjectContext> GetContextAsync(ProjectAccessGrant access, CancellationToken cancellationToken);
}
