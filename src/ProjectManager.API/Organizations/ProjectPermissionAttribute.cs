using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Projects;

namespace ProjectManager.API.Organizations;

/// <summary>
/// Exige uma permissão de projeto no recurso da rota (ADR-010, ADR-011 e ADR-013). A organização vem do
/// cabeçalho <c>X-Gestalyn-Organization</c>: ausente ou inválido, 400. Sem acesso ao projeto (não membro
/// da organização, recurso de outra organização ou fora da equipe sem <c>projects.read_all</c>), 404. Com
/// acesso, mas sem a permissão, 403. O acesso resolvido fica em <c>HttpContext.Items</c>.
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

        var hasId = Guid.TryParse(context.HttpContext.GetRouteValue(routeValueName) as string, out var resourceId);
        context.Result = await AuthorizeAsync(
            context.HttpContext,
            Permission,
            notFoundTitle,
            (access, membership, cancellationToken) => hasId
                ? FindAccessAsync(access, membership, resourceId, cancellationToken)
                : Task.FromResult<ProjectAccessGrant?>(null));
    }

    /// <summary>
    /// Mesma verificação dos atributos, para recursos identificados no corpo da requisição (ex.: a transição
    /// de workflow, cujo projeto vem do estado de origem). Retorna <c>null</c> quando o acesso é permitido.
    /// </summary>
    public static async Task<ObjectResult?> AuthorizeAsync(
        HttpContext httpContext,
        string permission,
        string notFoundTitle,
        Func<IProjectAccess, ActiveMembership, CancellationToken, Task<ProjectAccessGrant?>> findAccess)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(findAccess);

        if (!ActiveOrganizationMiddleware.TryReadHeader(httpContext.Request, out _))
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status400BadRequest,
                "Organização não informada.",
                $"Informe o identificador da organização no cabeçalho {ActiveOrganizationMiddleware.HeaderName}.");
        }

        var membership = httpContext.RequestServices.GetRequiredService<ITenantContext>().Membership;
        var access = membership is null
            ? null
            : await findAccess(httpContext.RequestServices.GetRequiredService<IProjectAccess>(), membership, httpContext.RequestAborted);

        if (access is null)
        {
            return CreateProblem(httpContext, StatusCodes.Status404NotFound, notFoundTitle, detail: null);
        }

        if (!access.HasPermission(permission))
        {
            return CreateProblem(
                httpContext,
                StatusCodes.Status403Forbidden,
                "Permissão insuficiente.",
                $"Seu acesso a este projeto não concede a permissão '{permission}'.");
        }

        httpContext.Items[typeof(ProjectAccessGrant)] = access;
        return null;
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

/// <summary>Permissão de projeto na rota <c>{stateId}</c>: o acesso é o do projeto do estado.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class WorkflowStatePermissionAttribute(string permission)
    : ProjectResourcePermissionAttribute(permission, RouteValueName, "Estado não encontrado.")
{
    public const string RouteValueName = "stateId";

    protected override Task<ProjectAccessGrant?> FindAccessAsync(
        IProjectAccess projectAccess,
        ActiveMembership membership,
        Guid resourceId,
        CancellationToken cancellationToken) =>
        projectAccess.FindByWorkflowStateAsync(membership, resourceId, cancellationToken);
}

/// <summary>Permissão de projeto na rota <c>{transitionId}</c>: o acesso é o do projeto da transição.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class WorkflowTransitionPermissionAttribute(string permission)
    : ProjectResourcePermissionAttribute(permission, RouteValueName, "Transição não encontrada.")
{
    public const string RouteValueName = "transitionId";

    protected override Task<ProjectAccessGrant?> FindAccessAsync(
        IProjectAccess projectAccess,
        ActiveMembership membership,
        Guid resourceId,
        CancellationToken cancellationToken) =>
        projectAccess.FindByWorkflowTransitionAsync(membership, resourceId, cancellationToken);
}

/// <summary>Permissão de projeto na rota <c>{documentId}</c>: o acesso é o do projeto do documento (ADR-014).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DocumentPermissionAttribute(string permission)
    : ProjectResourcePermissionAttribute(permission, RouteValueName, "Documento não encontrado.")
{
    public const string RouteValueName = "documentId";

    protected override Task<ProjectAccessGrant?> FindAccessAsync(
        IProjectAccess projectAccess,
        ActiveMembership membership,
        Guid resourceId,
        CancellationToken cancellationToken) =>
        projectAccess.FindByDocumentAsync(membership, resourceId, cancellationToken);
}

/// <summary>Permissão de projeto na rota <c>{decisionId}</c>: o acesso é o do projeto da decisão (ADR-014).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DecisionPermissionAttribute(string permission)
    : ProjectResourcePermissionAttribute(permission, RouteValueName, "Decisão não encontrada.")
{
    public const string RouteValueName = "decisionId";

    protected override Task<ProjectAccessGrant?> FindAccessAsync(
        IProjectAccess projectAccess,
        ActiveMembership membership,
        Guid resourceId,
        CancellationToken cancellationToken) =>
        projectAccess.FindByDecisionAsync(membership, resourceId, cancellationToken);
}

/// <summary>Permissão de projeto na rota <c>{reportId}</c>: o acesso é o do projeto do relatório (ADR-016, D8).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ReportPermissionAttribute(string permission)
    : ProjectResourcePermissionAttribute(permission, RouteValueName, "Relatório não encontrado.")
{
    public const string RouteValueName = "reportId";

    protected override Task<ProjectAccessGrant?> FindAccessAsync(
        IProjectAccess projectAccess,
        ActiveMembership membership,
        Guid resourceId,
        CancellationToken cancellationToken) =>
        projectAccess.FindByWorkReportAsync(membership, resourceId, cancellationToken);
}
