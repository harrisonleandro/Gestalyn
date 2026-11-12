using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Projects;

namespace ProjectManager.API.Organizations;

/// <summary>
/// Exige uma permissão de projeto no recurso da rota (ADR-010 e ADR-011). A organização vem do cabeçalho
/// <c>X-Gestalyn-Organization</c>: ausente ou inválido, 400. Sem acesso ao projeto (não membro da
/// organização, recurso de outra organização ou fora da equipe sem <c>projects.read_all</c>), 404.
/// Com acesso, mas sem a permissão, 403. O acesso resolvido fica em <c>HttpContext.Items</c>.
/// </summary>
public abstract class ProjectResourcePermissionAttribute(string permission, string routeValueName, string notFoundTitle)
    : Attribute, IAsyncAuthorizationFilter
{
    public string Permission { get; } = permission;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Autenticação já recusada por outro filtro (ex.: 401).
        if (context.Result is not null)
        {
            return;
        }

        var httpContext = context.HttpContext;
        if (!ActiveOrganizationMiddleware.TryReadHeader(httpContext.Request, out _))
        {
            context.Result = CreateProblem(
                httpContext,
                StatusCodes.Status400BadRequest,
                "Organização não informada.",
                $"Informe o identificador da organização no cabeçalho {ActiveOrganizationMiddleware.HeaderName}.");
            return;
        }

        var membership = httpContext.RequestServices.GetRequiredService<ITenantContext>().Membership;
        var access = membership is not null && Guid.TryParse(httpContext.GetRouteValue(routeValueName) as string, out var resourceId)
            ? await FindAccessAsync(httpContext.RequestServices.GetRequiredService<IProjectAccess>(), membership, resourceId, httpContext.RequestAborted)
            : null;

        if (access is null)
        {
            context.Result = CreateProblem(httpContext, StatusCodes.Status404NotFound, notFoundTitle, detail: null);
        }
        else if (!access.HasPermission(Permission))
        {
            context.Result = CreateProblem(
                httpContext,
                StatusCodes.Status403Forbidden,
                "Permissão insuficiente.",
                $"Seu acesso a este projeto não concede a permissão '{Permission}'.");
        }
        else
        {
            httpContext.Items[typeof(ProjectAccessGrant)] = access;
        }
    }

    protected abstract Task<ProjectAccessGrant?> FindAccessAsync(
        IProjectAccess projectAccess,
        ActiveMembership membership,
        Guid resourceId,
        CancellationToken cancellationToken);

    private static ObjectResult CreateProblem(HttpContext httpContext, int statusCode, string title, string? detail)
    {
        var factory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateProblemDetails(httpContext, statusCode, title, detail: detail);

        return new ObjectResult(problem) { StatusCode = statusCode };
    }
}

/// <summary>Permissão de projeto na rota <c>{projectId}</c>.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ProjectPermissionAttribute(string permission)
    : ProjectResourcePermissionAttribute(permission, RouteValueName, "Projeto não encontrado.")
{
    public const string RouteValueName = "projectId";

    protected override Task<ProjectAccessGrant?> FindAccessAsync(
        IProjectAccess projectAccess,
        ActiveMembership membership,
        Guid resourceId,
        CancellationToken cancellationToken) =>
        projectAccess.FindAsync(membership, resourceId, cancellationToken);
}

/// <summary>Permissão de projeto na rota <c>{taskId}</c>: o acesso é o do projeto da tarefa.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class TaskPermissionAttribute(string permission)
    : ProjectResourcePermissionAttribute(permission, RouteValueName, "Tarefa não encontrada.")
{
    public const string RouteValueName = "taskId";

    protected override Task<ProjectAccessGrant?> FindAccessAsync(
        IProjectAccess projectAccess,
        ActiveMembership membership,
        Guid resourceId,
        CancellationToken cancellationToken) =>
        projectAccess.FindByTaskAsync(membership, resourceId, cancellationToken);
}
