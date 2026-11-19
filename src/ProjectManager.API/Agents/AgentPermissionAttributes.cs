using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Projects;

namespace ProjectManager.API.Agents;

/// <summary>
/// Autorização das rotas de agentes por recurso (ADR-016, D4). O escopo indica a exigência da tabela única
/// (<see cref="AgentRequirement"/>), e a decisão vem só de <see cref="AgentAccessGrant.Compute"/>:
/// <list type="bullet">
/// <item>projeto invisível (sem concessão ou de outra organização): 404;</item>
/// <item>escopo ou permissão ausente: 403;</item>
/// <item>agente desativado: 401.</item>
/// </list>
/// A concessão fica em <c>HttpContext.Items</c>, como nas rotas de pessoas.
/// </summary>
public abstract class AgentResourcePermissionAttribute(string scope, string routeValueName, string notFoundTitle)
    : Attribute, IAsyncAuthorizationFilter
{
    public string Scope { get; } = scope;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Result is not null)
        {
            return;
        }

        var httpContext = context.HttpContext;
        var agent = httpContext.RequestServices.GetRequiredService<ITenantContext>().Agent;
        if (agent is null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var requirement = AgentRequirement.ForScope(Scope);
        var access = httpContext.RequestServices.GetRequiredService<IAgentProjectAccess>();
        var decision = Guid.TryParse(httpContext.GetRouteValue(routeValueName) as string, out var resourceId)
            ? await FindAsync(access, agent, resourceId, requirement, httpContext.RequestAborted)
            : new AgentAccessDecision(AgentAccessOutcome.ProjectNotVisible, Grant: null);

        context.Result = ToResult(httpContext, decision, requirement, notFoundTitle);
        if (decision.IsAllowed)
        {
            httpContext.Items[typeof(ProjectAccessGrant)] = decision.Grant;
        }
    }

    /// <summary>Resposta da recusa, ou <c>null</c> quando o acesso é permitido.</summary>
    public static IActionResult? ToResult(HttpContext httpContext, AgentAccessDecision decision, AgentRequirement requirement, string notFoundTitle)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(requirement);

        return decision.Outcome switch
        {
            AgentAccessOutcome.Allowed => null,
            AgentAccessOutcome.AgentDisabled => new UnauthorizedResult(),
            AgentAccessOutcome.ProjectNotVisible => Problem(httpContext, StatusCodes.Status404NotFound, notFoundTitle, detail: null),
            AgentAccessOutcome.ScopeMissing => Problem(
                httpContext,
                StatusCodes.Status403Forbidden,
                "Escopo insuficiente.",
                $"A credencial não tem o escopo '{requirement.Scope}'."),
            _ => Problem(
                httpContext,
                StatusCodes.Status403Forbidden,
                "Permissão insuficiente.",
                $"O acesso do agente a este projeto não concede a permissão '{requirement.Permission}'."),
        };
    }

    protected abstract Task<AgentAccessDecision> FindAsync(
        IAgentProjectAccess access,
        ActiveAgent agent,
        Guid resourceId,
        AgentRequirement requirement,
        CancellationToken cancellationToken);

    private static ObjectResult Problem(HttpContext httpContext, int statusCode, string title, string? detail)
    {
        var factory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        return new ObjectResult(factory.CreateProblemDetails(httpContext, statusCode, title, detail: detail)) { StatusCode = statusCode };
    }
}

/// <summary>Acesso do agente ao projeto da rota <c>{projectId}</c>.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AgentProjectPermissionAttribute(string scope)
    : AgentResourcePermissionAttribute(scope, "projectId", "Projeto não encontrado.")
{
    protected override Task<AgentAccessDecision> FindAsync(
        IAgentProjectAccess access,
        ActiveAgent agent,
        Guid resourceId,
        AgentRequirement requirement,
        CancellationToken cancellationToken) =>
        access.FindAsync(agent, resourceId, requirement, cancellationToken);
}

/// <summary>Acesso do agente ao projeto da tarefa da rota <c>{taskId}</c>.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AgentTaskPermissionAttribute(string scope)
    : AgentResourcePermissionAttribute(scope, "taskId", "Tarefa não encontrada.")
{
    protected override Task<AgentAccessDecision> FindAsync(
        IAgentProjectAccess access,
        ActiveAgent agent,
        Guid resourceId,
        AgentRequirement requirement,
        CancellationToken cancellationToken) =>
        access.FindByTaskAsync(agent, resourceId, requirement, cancellationToken);
}
