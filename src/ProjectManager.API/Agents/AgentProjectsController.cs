using Microsoft.AspNetCore.Mvc;
using ProjectManager.API.Common;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Agents;
using ProjectManager.Application.Common;
using ProjectManager.Application.Projects;
using ProjectManager.Application.Tasks;
using ProjectManager.Domain.Agents;

namespace ProjectManager.API.Agents;

/// <summary>
/// Leitura de projetos e tarefas por agentes (ADR-016, D7): só os projetos concedidos, na organização da credencial, com
/// o escopo de cada rota. A autorização por projeto é sempre <see cref="AgentAccessGrant.Compute"/>.
/// </summary>
[ApiController]
[AgentApi]
[Route("api/v1/agents")]
[Tags("Agentes")]
public sealed class AgentProjectsController(IAgentWorkService work, ITaskService tasks, ITenantContext tenantContext) : ControllerBase
{
    private ActiveAgent Agent => tenantContext.Agent
        ?? throw new InvalidOperationException("Agente não resolvido.");

    private ProjectAccessGrant Access => HttpContext.Items[typeof(ProjectAccessGrant)] as ProjectAccessGrant
        ?? throw new InvalidOperationException("Acesso ao projeto não resolvido.");

    /// <summary>Projetos concedidos ao agente, com o papel. Exige o escopo <c>context:read</c>.</summary>
    [HttpGet("projects")]
    [ProducesResponseType<IReadOnlyList<AgentProjectSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListProjectsAsync(CancellationToken cancellationToken)
    {
        if (!Agent.HasScope(AgentScopes.ContextRead))
        {
            return AgentResourcePermissionAttribute.ToResult(
                HttpContext,
                new AgentAccessDecision(AgentAccessOutcome.ScopeMissing, Grant: null),
                AgentRequirement.ContextRead,
                notFoundTitle: string.Empty)!;
        }

        return Ok(await work.ListProjectsAsync(Agent, cancellationToken));
    }

    /// <summary>Contexto do projeto: workflow, progresso, decisões aceitas, documentos e atividades recentes.</summary>
    [HttpGet("projects/{projectId:guid}/context")]
    [AgentProjectPermission(AgentScopes.ContextRead)]
    [ProducesResponseType<AgentProjectContext>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetContextAsync(CancellationToken cancellationToken) =>
        Ok(await work.GetContextAsync(Access, cancellationToken));

    /// <summary>Tarefas do projeto, com os filtros e a paginação da lista de pessoas.</summary>
    [HttpGet("projects/{projectId:guid}/tasks")]
    [AgentProjectPermission(AgentScopes.TasksRead)]
    [ProducesResponseType<PagedResult<TaskSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListTasksAsync([FromQuery] TaskListQuery query, CancellationToken cancellationToken) =>
        Ok(await tasks.ListAsync(Access, query, cancellationToken));

    /// <summary>Detalhes da tarefa, com a versão e as transições que este agente pode executar.</summary>
    [HttpGet("tasks/{taskId:guid}")]
    [AgentTaskPermission(AgentScopes.TasksRead)]
    [ProducesResponseType<TaskDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var result = await tasks.GetAsync(Access, taskId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
