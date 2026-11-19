using Microsoft.AspNetCore.Mvc;
using ProjectManager.API.Common;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Activity;
using ProjectManager.Application.Agents;
using ProjectManager.Application.Projects;
using ProjectManager.Application.Tasks;
using ProjectManager.Domain.Agents;

namespace ProjectManager.API.Agents;

/// <summary>
/// Escritas de agentes (ADR-016, D7 a D9). Os POSTs exigem <c>Idempotency-Key</c>; as alterações de tarefa exigem
/// <c>expectedVersion</c>. As regras são as mesmas das pessoas, nos mesmos serviços: o agente é o autor, nunca aprova
/// nem conclui tarefas.
/// </summary>
[ApiController]
[AgentApi]
[Route("api/v1/agents")]
[Tags("Agentes")]
public sealed class AgentWritesController(
    ITaskService tasks,
    IActivityService activities,
    IAgentProjectAccess agentAccess,
    ITenantContext tenantContext) : ControllerBase
{
    private const string ExpectedVersionTitle = "Informe a versão esperada da tarefa (expectedVersion).";

    private ActiveAgent Agent => tenantContext.Agent
        ?? throw new InvalidOperationException("Agente não resolvido.");

    private ProjectAccessGrant Access => HttpContext.Items[typeof(ProjectAccessGrant)] as ProjectAccessGrant
        ?? throw new InvalidOperationException("Acesso ao projeto não resolvido.");

    /// <summary>Cria uma tarefa no projeto indicado no corpo, com o agente como autor.</summary>
    [HttpPost("tasks")]
    [AgentIdempotent(AgentScopes.TasksWrite)]
    [ProducesResponseType<TaskDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateTaskAsync(CreateAgentTaskRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await AuthorizeProjectAsync(request.ProjectId!.Value, AgentRequirement.TasksWrite, cancellationToken) is { } denied)
        {
            return denied;
        }

        var result = await tasks.CreateAsync(Access, request.ToTaskRequest(), cancellationToken);
        return result.Succeeded
            ? Created(new Uri($"/api/v1/agents/tasks/{result.Value!.Id}", UriKind.Relative), result.Value)
            : this.ToProblem(result.Error!);
    }

    /// <summary>Altera a tarefa. Exige <c>expectedVersion</c>: uma versão desatualizada responde 409, sem sobrescrever.</summary>
    [HttpPatch("tasks/{taskId:guid}")]
    [AgentTaskPermission(AgentScopes.TasksWrite)]
    [ProducesResponseType<TaskDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateTaskAsync(Guid taskId, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ExpectedVersion is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ExpectedVersionTitle);
        }

        var result = await tasks.UpdateAsync(Access, taskId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    /// <summary>
    /// Move a tarefa pela regra única de transições: agentes nunca executam transições com aprovação nem entram em
    /// Concluída (403). Exige <c>expectedVersion</c> e <c>Idempotency-Key</c>.
    /// </summary>
    [HttpPost("tasks/{taskId:guid}/transitions")]
    [AgentTaskPermission(AgentScopes.TasksWrite)]
    [AgentIdempotent(AgentScopes.TasksWrite)]
    [ProducesResponseType<TaskDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> TransitionAsync(Guid taskId, TransitionTaskRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ExpectedVersion is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ExpectedVersionTitle);
        }

        var result = await tasks.TransitionAsync(Access, taskId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    /// <summary>Registra uma atividade manual no projeto indicado no corpo, com o agente como autor.</summary>
    [HttpPost("activities")]
    [AgentIdempotent(AgentScopes.ActivitiesWrite)]
    [ProducesResponseType<ActivityItem>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateActivityAsync(CreateAgentActivityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var projectId = request.ProjectId!.Value;
        if (await AuthorizeProjectAsync(projectId, AgentRequirement.ActivitiesWrite, cancellationToken) is { } denied)
        {
            return denied;
        }

        var result = await activities.CreateLogAsync(projectId, request.ToLogRequest(), cancellationToken);
        return result.Succeeded
            ? Created(new Uri($"/api/v1/agents/projects/{projectId}/context", UriKind.Relative), result.Value)
            : this.ToProblem(result.Error!);
    }

    /// <summary>Rotas com o projeto no corpo: mesma decisão de <see cref="AgentAccessGrant.Compute"/> dos atributos.</summary>
    private async Task<IActionResult?> AuthorizeProjectAsync(Guid projectId, AgentRequirement requirement, CancellationToken cancellationToken)
    {
        var decision = await agentAccess.FindAsync(Agent, projectId, requirement, cancellationToken);
        if (decision.IsAllowed)
        {
            HttpContext.Items[typeof(ProjectAccessGrant)] = decision.Grant;
        }

        return AgentResourcePermissionAttribute.ToResult(HttpContext, decision, requirement, "Projeto não encontrado.");
    }
}
