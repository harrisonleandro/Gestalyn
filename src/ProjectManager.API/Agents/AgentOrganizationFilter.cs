using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using ProjectManager.API.Organizations;
using ProjectManager.Application.Abstractions.MultiTenancy;

namespace ProjectManager.API.Agents;

/// <summary>
/// Guarda das rotas de agentes (ADR-016): a organização vem só da credencial. Se a rota ou o cabeçalho
/// <c>X-Gestalyn-Organization</c> trouxerem outra organização (ou um valor inválido), a resposta é 404, como para
/// um recurso de outra organização.
/// </summary>
internal sealed class AgentOrganizationFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
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

        if (Mismatches(httpContext, agent.OrganizationId))
        {
            var factory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
            context.Result = new ObjectResult(factory.CreateProblemDetails(httpContext, StatusCodes.Status404NotFound, "Recurso não encontrado."))
            {
                StatusCode = StatusCodes.Status404NotFound,
            };
        }
    }

    private static bool Mismatches(HttpContext httpContext, Guid organizationId)
    {
        if (httpContext.GetRouteValue(ActiveOrganizationMiddleware.RouteValueName) is string routeValue
            && (!Guid.TryParse(routeValue, out var fromRoute) || fromRoute != organizationId))
        {
            return true;
        }

        return httpContext.Request.Headers.ContainsKey(ActiveOrganizationMiddleware.HeaderName)
            && (!ActiveOrganizationMiddleware.TryReadHeader(httpContext.Request, out var fromHeader) || fromHeader != organizationId);
    }
}
