using ProjectManager.API.Authentication;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Infrastructure.MultiTenancy;

namespace ProjectManager.API.Organizations;

/// <summary>
/// Resolve a organização ativa a partir da rota (<c>{organizationId}</c>) ou, nas rotas por ID de
/// recurso, do cabeçalho <c>X-Gestalyn-Organization</c>, e só a ativa depois de confirmar que o usuário
/// autenticado é membro (ADR-008). Sem associação, a organização ativa fica vazia: os filtros não
/// retornam dados e os endpoints respondem 404.
/// </summary>
internal sealed class ActiveOrganizationMiddleware(RequestDelegate next)
{
    public const string RouteValueName = "organizationId";
    public const string HeaderName = "X-Gestalyn-Organization";

    public async Task InvokeAsync(HttpContext httpContext, TenantContext tenantContext, IMembershipDirectory membershipDirectory)
    {
        if (TryReadOrganizationId(httpContext, out var organizationId)
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

    /// <summary>A rota prevalece; o cabeçalho só é lido quando a rota não traz a organização.</summary>
    private static bool TryReadOrganizationId(HttpContext httpContext, out Guid organizationId)
    {
        if (httpContext.GetRouteValue(RouteValueName) is string routeValue)
        {
            return Guid.TryParse(routeValue, out organizationId);
        }

        return TryReadHeader(httpContext.Request, out organizationId);
    }

    public static bool TryReadHeader(HttpRequest request, out Guid organizationId)
    {
        ArgumentNullException.ThrowIfNull(request);

        organizationId = Guid.Empty;
        var values = request.Headers[HeaderName];

        return values.Count == 1 && Guid.TryParse(values[0], out organizationId) && organizationId != Guid.Empty;
    }
}
