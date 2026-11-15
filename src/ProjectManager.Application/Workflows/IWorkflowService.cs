using ProjectManager.Application.Common;

namespace ProjectManager.Application.Workflows;

/// <summary>
/// Configuração do workflow de um projeto da organização ativa (ADR-013). As operações pressupõem que o
/// acesso ao projeto já foi verificado.
/// </summary>
public interface IWorkflowService
{
    Task<Result<WorkflowDetails>> GetAsync(Guid projectId, CancellationToken cancellationToken);

    Task<Result<WorkflowDetails>> UpdateSettingsAsync(Guid projectId, UpdateWorkflowRequest request, CancellationToken cancellationToken);

    Task<Result<WorkflowStateDetails>> AddStateAsync(Guid projectId, CreateStateRequest request, CancellationToken cancellationToken);

    Task<Result<WorkflowStateDetails>> UpdateStateAsync(Guid stateId, UpdateStateRequest request, CancellationToken cancellationToken);

    Task<Result<WorkflowDetails>> MoveStateAsync(Guid stateId, MoveStateRequest request, CancellationToken cancellationToken);

    Task<Result> DeleteStateAsync(Guid stateId, CancellationToken cancellationToken);

    Task<Result<WorkflowTransitionDetails>> AddTransitionAsync(CreateTransitionRequest request, CancellationToken cancellationToken);

    Task<Result> DeleteTransitionAsync(Guid transitionId, CancellationToken cancellationToken);
}
