using Microsoft.AspNetCore.Mvc;
using ProjectManager.API.Common;
using ProjectManager.API.Organizations;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Dashboards;
using ProjectManager.Domain.Organizations;
using ProjectManager.Domain.Projects;

namespace ProjectManager.API.Controllers;

/// <summary>
/// Dashboards, progresso e estatísticas (ADR-014, D7 e D8), calculados na hora sobre os dados reais. O progresso
/// segue a regra <c>doneOverNotCanceled</c> e vem sempre com as contagens por categoria.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class DashboardController(IDashboardService dashboards, ITenantContext tenantContext) : ControllerBase
{
    /// <summary>Visão geral da organização, só com os projetos ativos que o usuário enxerga.</summary>
    [HttpGet("organizations/{organizationId:guid}/dashboard")]
    [OrganizationPermission(OrganizationPermissions.OrganizationRead)]
    [ProducesResponseType<OrganizationDashboard>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrganizationAsync(CancellationToken cancellationToken)
    {
        var actor = tenantContext.Membership ?? throw new InvalidOperationException("Associação não resolvida.");
        return Ok(await dashboards.GetOrganizationAsync(actor, cancellationToken));
    }

    [HttpGet("projects/{projectId:guid}/dashboard")]
    [ProjectPermission(ProjectPermissions.Read)]
    [ProducesResponseType<ProjectDashboard>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var result = await dashboards.GetProjectAsync(projectId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("projects/{projectId:guid}/progress")]
    [ProjectPermission(ProjectPermissions.Read)]
    [ProducesResponseType<ProjectProgress>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProgressAsync(Guid projectId, CancellationToken cancellationToken) =>
        Ok(await dashboards.GetProgressAsync(projectId, cancellationToken));

    [HttpGet("projects/{projectId:guid}/statistics")]
    [ProjectPermission(ProjectPermissions.Read)]
    [ProducesResponseType<ProjectStatistics>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatisticsAsync(Guid projectId, [FromQuery] StatisticsQuery query, CancellationToken cancellationToken) =>
        Ok(await dashboards.GetStatisticsAsync(projectId, query, cancellationToken));
}
