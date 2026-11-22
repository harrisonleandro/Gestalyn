using Microsoft.AspNetCore.Mvc;
using ProjectManager.API.Common;
using ProjectManager.API.Organizations;
using ProjectManager.Application.Agents;
using ProjectManager.Application.Common;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Projects;

namespace ProjectManager.API.Controllers;

/// <summary>
/// Leitura dos relatórios de trabalho de agentes por pessoas com <c>project.read</c> (ADR-016, D8). O conteúdo é
/// declaração do agente; os níveis vêm separados e a aprovação humana é calculada pelo histórico da tarefa.
/// </summary>
[ApiController]
[Route("api/v1")]
[Tags("Relatórios de trabalho")]
public sealed class WorkReportsController(IWorkReportService reports) : ControllerBase
{
    private ProjectAccessGrant Access => HttpContext.Items[typeof(ProjectAccessGrant)] as ProjectAccessGrant
        ?? throw new InvalidOperationException("Acesso ao projeto não resolvido.");

    [HttpGet("projects/{projectId:guid}/reports")]
    [ProjectPermission(ProjectPermissions.Read)]
    [ProducesResponseType<PagedResult<WorkReportSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListByProjectAsync([FromQuery] WorkReportListQuery query, CancellationToken cancellationToken) =>
        Ok(await reports.ListByProjectAsync(Access, query, cancellationToken));

    [HttpGet("tasks/{taskId:guid}/reports")]
    [TaskPermission(ProjectPermissions.Read)]
    [ProducesResponseType<PagedResult<WorkReportSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListByTaskAsync(Guid taskId, [FromQuery] WorkReportListQuery query, CancellationToken cancellationToken)
    {
        var result = await reports.ListByTaskAsync(Access, taskId, query, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("reports/{reportId:guid}")]
    [ReportPermission(ProjectPermissions.Read)]
    [ProducesResponseType<WorkReportDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var result = await reports.GetAsync(Access, reportId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
