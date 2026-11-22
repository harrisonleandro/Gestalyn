using ProjectManager.Application.Abstractions.MultiTenancy;

namespace ProjectManager.Application.Projects;

/// <summary>
/// Resolve o acesso do agente a um projeto (ADR-016): lê a concessão e o estado do agente na organização da
/// credencial e decide só por <see cref="AgentAccessGrant.Compute"/>.
/// </summary>
public interface IAgentProjectAccess
{
    Task<AgentAccessDecision> FindAsync(ActiveAgent agent, Guid projectId, AgentRequirement requirement, CancellationToken cancellationToken);

    /// <summary>Acesso ao projeto da tarefa. Tarefa inexistente, ou de projeto invisível, é <see cref="AgentAccessOutcome.ProjectNotVisible"/>.</summary>
    Task<AgentAccessDecision> FindByTaskAsync(ActiveAgent agent, Guid taskId, AgentRequirement requirement, CancellationToken cancellationToken);
}
