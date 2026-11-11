using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using ProjectManager.Application.Abstractions.MultiTenancy;

namespace ProjectManager.API.Organizations;

/// <summary>
/// Exige associação à organização da rota e, opcionalmente, uma permissão do catálogo.
/// Não membro: 404 (não revela que a organização existe). Membro sem a permissão: 403.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class OrganizationPermissionAttribute(string? permission = null) : Attribute, IAuthorizationFilter
{
    public string? Permission { get; } = permission;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Autenticação já recusada por outro filtro (ex.: 401).
        if (context.Result is not null)
        {
            return;
        }

        var membership = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>().Membership;
        if (membership is null)
        {
            context.Result = CreateProblem(context.HttpContext, StatusCodes.Status404NotFound, "Organização não encontrada.", detail: null);
        }
        else if (Permission is not null && !membership.HasPermission(Permission))
        {
            context.Result = CreateProblem(
                context.HttpContext,
                StatusCodes.Status403Forbidden,
                "Permissão insuficiente.",
                $"Seu papel nesta organização não concede a permissão '{Permission}'.");
        }
    }

    private static ObjectResult CreateProblem(HttpContext httpContext, int statusCode, string title, string? detail)
    {
        var factory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateProblemDetails(httpContext, statusCode, title, detail: detail);

        return new ObjectResult(problem) { StatusCode = statusCode };
    }
}
