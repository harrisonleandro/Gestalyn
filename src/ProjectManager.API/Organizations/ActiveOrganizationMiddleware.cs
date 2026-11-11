using ProjectManager.API.Authentication;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Infrastructure.MultiTenancy;

namespace ProjectManager.API.Organizations;

/// <summary>
/// Resolve a organização ativa a partir da rota (<c>{organizationId}</c>) e só a ativa depois de
/// confirmar que o usuário autenticado é membro (ADR-008). Sem associação, a organização ativa fica
/// vazia: os filtros não retornam dados e os endpoints respondem 404.
/// </summary>
internal sealed class ActiveOrganizationMiddleware(RequestDelegate next)
{
    public const string RouteValueName = "organizationId";

    public async Task InvokeAsync(HttpContext httpContext, TenantContext tenantContext, IMembershipDirectory membershipDirectory)
    {
        if (httpContext.GetRouteValue(RouteValueName) is string routeValue
            && Guid.TryParse(routeValue, out var organizationId)
            && httpContext.User.TryGetSession(out var userId, out _))
        {
            var membership = await membershipDirectory.FindMembershipAsync(userId, organizationId, httpContext.RequestAborted);
            if (membership is not null)
            {
                tenantContext.SetActiveMembership(membership);
            }
        }

        await next(httpContext);
    }
}
