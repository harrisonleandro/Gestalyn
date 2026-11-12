using Microsoft.AspNetCore.Mvc;
using ProjectManager.API.Common;
using ProjectManager.API.Organizations;
using ProjectManager.Application.Planning;
using ProjectManager.Domain.Projects;

namespace ProjectManager.API.Controllers;

/// <summary>
/// Fases e marcos de um projeto. Exige o cabeçalho <c>X-Gestalyn-Organization</c>; alterar o planejamento
/// exige <c>project.plan</c> (ADR-011).
/// </summary>
[ApiController]
[Route("api/v1/projects/{projectId:guid}")]
public sealed class PlanningController(IPlanningService planning) : ControllerBase
{
    [HttpGet("phases")]
    [ProjectPermission(ProjectPermissions.Read)]
    [ProducesResponseType<IReadOnlyList<PhaseDetails>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListPhasesAsync(Guid projectId, CancellationToken cancellationToken) =>
        Ok(await planning.ListPhasesAsync(projectId, cancellationToken));

    /// <summary>Cria uma fase no fim da ordem atual.</summary>
    [HttpPost("phases")]
    [ProjectPermission(ProjectPermissions.Plan)]
    [ProducesResponseType<PhaseDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreatePhaseAsync(Guid projectId, CreatePhaseRequest request, CancellationToken cancellationToken)
    {
        var result = await planning.CreatePhaseAsync(projectId, request, cancellationToken);

        return result.Succeeded
            ? Created(new Uri($"/api/v1/projects/{projectId}/phases", UriKind.Relative), result.Value)
            : this.ToProblem(result.Error!);
    }

    [HttpPatch("phases/{phaseId:guid}")]
    [ProjectPermission(ProjectPermissions.Plan)]
    [ProducesResponseType<PhaseDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdatePhaseAsync(Guid projectId, Guid phaseId, UpdatePhaseRequest request, CancellationToken cancellationToken)
    {
        var result = await planning.UpdatePhaseAsync(projectId, phaseId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    /// <summary>Reordena as fases pela lista completa de IDs.</summary>
    [HttpPut("phases/order")]
    [ProjectPermission(ProjectPermissions.Plan)]
    [ProducesResponseType<IReadOnlyList<PhaseDetails>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReorderPhasesAsync(Guid projectId, ReorderPhasesRequest request, CancellationToken cancellationToken)
    {
        var result = await planning.ReorderPhasesAsync(projectId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    /// <summary>Exclui uma fase sem tarefas. Marcos ligados a ela ficam sem fase.</summary>
    [HttpDelete("phases/{phaseId:guid}")]
    [ProjectPermission(ProjectPermissions.Plan)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeletePhaseAsync(Guid projectId, Guid phaseId, CancellationToken cancellationToken)
    {
        var result = await planning.DeletePhaseAsync(projectId, phaseId, cancellationToken);

        return result.Succeeded ? NoContent() : this.ToProblem(result.Error!);
    }

    [HttpGet("milestones")]
    [ProjectPermission(ProjectPermissions.Read)]
    [ProducesResponseType<IReadOnlyList<MilestoneDetails>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListMilestonesAsync(Guid projectId, CancellationToken cancellationToken) =>
        Ok(await planning.ListMilestonesAsync(projectId, cancellationToken));

    [HttpPost("milestones")]
    [ProjectPermission(ProjectPermissions.Plan)]
    [ProducesResponseType<MilestoneDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateMilestoneAsync(Guid projectId, CreateMilestoneRequest request, CancellationToken cancellationToken)
    {
        var result = await planning.CreateMilestoneAsync(projectId, request, cancellationToken);

        return result.Succeeded
            ? Created(new Uri($"/api/v1/projects/{projectId}/milestones", UriKind.Relative), result.Value)
            : this.ToProblem(result.Error!);
    }

    /// <summary>Alteração parcial; <c>isReached</c> marca ou desmarca o marco como atingido.</summary>
    [HttpPatch("milestones/{milestoneId:guid}")]
    [ProjectPermission(ProjectPermissions.Plan)]
    [ProducesResponseType<MilestoneDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateMilestoneAsync(
        Guid projectId,
        Guid milestoneId,
        UpdateMilestoneRequest request,
        CancellationToken cancellationToken)
    {
        var result = await planning.UpdateMilestoneAsync(projectId, milestoneId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    /// <summary>Exclui um marco sem tarefas.</summary>
    [HttpDelete("milestones/{milestoneId:guid}")]
    [ProjectPermission(ProjectPermissions.Plan)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteMilestoneAsync(Guid projectId, Guid milestoneId, CancellationToken cancellationToken)
    {
        var result = await planning.DeleteMilestoneAsync(projectId, milestoneId, cancellationToken);

        return result.Succeeded ? NoContent() : this.ToProblem(result.Error!);
    }
}
