using ProjectManager.Application.Common;

namespace ProjectManager.Application.Planning;

/// <summary>
/// Fases e marcos de um projeto da organização ativa. As operações pressupõem que o acesso ao projeto
/// já foi verificado (<c>IProjectAccess</c>).
/// </summary>
public interface IPlanningService
{
    Task<IReadOnlyList<PhaseDetails>> ListPhasesAsync(Guid projectId, CancellationToken cancellationToken);

    Task<Result<PhaseDetails>> CreatePhaseAsync(Guid projectId, CreatePhaseRequest request, CancellationToken cancellationToken);

    Task<Result<PhaseDetails>> UpdatePhaseAsync(Guid projectId, Guid phaseId, UpdatePhaseRequest request, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<PhaseDetails>>> ReorderPhasesAsync(Guid projectId, ReorderPhasesRequest request, CancellationToken cancellationToken);

    Task<Result> DeletePhaseAsync(Guid projectId, Guid phaseId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MilestoneDetails>> ListMilestonesAsync(Guid projectId, CancellationToken cancellationToken);

    Task<Result<MilestoneDetails>> CreateMilestoneAsync(Guid projectId, CreateMilestoneRequest request, CancellationToken cancellationToken);

    Task<Result<MilestoneDetails>> UpdateMilestoneAsync(
        Guid projectId,
        Guid milestoneId,
        UpdateMilestoneRequest request,
        CancellationToken cancellationToken);

    Task<Result> DeleteMilestoneAsync(Guid projectId, Guid milestoneId, CancellationToken cancellationToken);
}
