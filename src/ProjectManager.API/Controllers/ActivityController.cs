using Microsoft.AspNetCore.Mvc;
using ProjectManager.API.Common;
using ProjectManager.API.Organizations;
using ProjectManager.Application.Activity;
using ProjectManager.Application.Common;
using ProjectManager.Domain.Organizations;
using ProjectManager.Domain.Projects;

namespace ProjectManager.API.Controllers;

/// <summary>
/// Atividades (ADR-014): eventos gerados pelo sistema e registros manuais, numa só lista, mais novos primeiro.
/// Não há rota de alteração nem de exclusão: os registros são imutáveis.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class ActivityController(IActivityService activities) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/activities")]
    [ProjectPermission(ProjectPermissions.Read)]
    [ProducesResponseType<PagedResult<ActivityItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListProjectAsync(Guid projectId, [FromQuery] ActivityListQuery query, CancellationToken cancellationToken) =>
        Ok(await activities.ListProjectAsync(projectId, query, cancellationToken));

    /// <summary>Registro manual de uma atividade do projeto (reunião, nota). Imutável depois de gravado.</summary>
    [HttpPost("projects/{projectId:guid}/activities")]
    [ProjectPermission(ProjectPermissions.DocumentsWrite)]
    [ProducesResponseType<ActivityItem>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateLogAsync(Guid projectId, CreateActivityLogRequest request, CancellationToken cancellationToken)
    {
        var result = await activities.CreateLogAsync(projectId, request, cancellationToken);

        return result.Succeeded
            ? Created(new Uri($"/api/v1/projects/{projectId}/activities", UriKind.Relative), result.Value)
            : this.ToProblem(result.Error!);
    }

    /// <summary>Atividades da organização (membros, convites, dados da organização) e de todos os projetos.</summary>
    [HttpGet("organizations/{organizationId:guid}/activities")]
    [OrganizationPermission(OrganizationPermissions.ActivityReadAll)]
    [ProducesResponseType<PagedResult<ActivityItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListOrganizationAsync([FromQuery] ActivityListQuery query, CancellationToken cancellationToken) =>
        Ok(await activities.ListOrganizationAsync(query, cancellationToken));
}
