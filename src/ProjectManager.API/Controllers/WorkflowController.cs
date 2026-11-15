using Microsoft.AspNetCore.Mvc;
using ProjectManager.API.Common;
using ProjectManager.API.Organizations;
using ProjectManager.Application.Workflows;
using ProjectManager.Domain.Projects;

namespace ProjectManager.API.Controllers;

/// <summary>
/// Configuração do workflow de um projeto (ADR-013). Todas as rotas exigem o cabeçalho
/// <c>X-Gestalyn-Organization</c>; alterar o workflow exige <c>workflow.manage</c>.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class WorkflowController(IWorkflowService workflows) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/workflow")]
    [ProjectPermission(ProjectPermissions.Read)]
    [ProducesResponseType<WorkflowDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var result = await workflows.GetAsync(projectId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    /// <summary>Configurações do workflow: estado inicial e aprovação independente.</summary>
    [HttpPut("projects/{projectId:guid}/workflow")]
    [ProjectPermission(ProjectPermissions.WorkflowManage)]
    [ProducesResponseType<WorkflowDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateSettingsAsync(Guid projectId, UpdateWorkflowRequest request, CancellationToken cancellationToken)
    {
        var result = await workflows.UpdateSettingsAsync(projectId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("projects/{projectId:guid}/workflow/states")]
    [ProjectPermission(ProjectPermissions.WorkflowManage)]
    [ProducesResponseType<WorkflowStateDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddStateAsync(Guid projectId, CreateStateRequest request, CancellationToken cancellationToken)
    {
        var result = await workflows.AddStateAsync(projectId, request, cancellationToken);

        return result.Succeeded
            ? Created(new Uri($"/api/v1/projects/{projectId}/workflow", UriKind.Relative), result.Value)
            : this.ToProblem(result.Error!);
    }

    /// <summary>Renomeia o estado ou muda a categoria dele.</summary>
    [HttpPatch("workflow/states/{stateId:guid}")]
    [WorkflowStatePermission(ProjectPermissions.WorkflowManage)]
    [ProducesResponseType<WorkflowStateDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateStateAsync(Guid stateId, UpdateStateRequest request, CancellationToken cancellationToken)
    {
        var result = await workflows.UpdateStateAsync(stateId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("workflow/states/{stateId:guid}/move")]
    [WorkflowStatePermission(ProjectPermissions.WorkflowManage)]
    [ProducesResponseType<WorkflowDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MoveStateAsync(Guid stateId, MoveStateRequest request, CancellationToken cancellationToken)
    {
        var result = await workflows.MoveStateAsync(stateId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    /// <summary>Exclui um estado sem tarefas, com as transições ligadas a ele. O estado inicial não pode ser excluído.</summary>
    [HttpDelete("workflow/states/{stateId:guid}")]
    [WorkflowStatePermission(ProjectPermissions.WorkflowManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteStateAsync(Guid stateId, CancellationToken cancellationToken)
    {
        var result = await workflows.DeleteStateAsync(stateId, cancellationToken);

        return result.Succeeded ? NoContent() : this.ToProblem(result.Error!);
    }

    /// <summary>Cria uma transição. O projeto é o do estado de origem; o de destino precisa ser do mesmo projeto.</summary>
    [HttpPost("workflow/transitions")]
    [ProducesResponseType<WorkflowTransitionDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddTransitionAsync(CreateTransitionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = await ProjectResourcePermissionAttribute.AuthorizeAsync(
            HttpContext,
            ProjectPermissions.WorkflowManage,
            "Estado não encontrado.",
            (access, membership, ct) => access.FindByWorkflowStateAsync(membership, request.FromStateId, ct));
        if (denied is not null)
        {
            return denied;
        }

        var result = await workflows.AddTransitionAsync(request, cancellationToken);

        return result.Succeeded
            ? Created(new Uri($"/api/v1/workflow/transitions/{result.Value!.Id}", UriKind.Relative), result.Value)
            : this.ToProblem(result.Error!);
    }

    [HttpDelete("workflow/transitions/{transitionId:guid}")]
    [WorkflowTransitionPermission(ProjectPermissions.WorkflowManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteTransitionAsync(Guid transitionId, CancellationToken cancellationToken)
    {
        var result = await workflows.DeleteTransitionAsync(transitionId, cancellationToken);

        return result.Succeeded ? NoContent() : this.ToProblem(result.Error!);
    }
}
