using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using ProjectManager.API.Agents;
using ProjectManager.Application.Abstractions.Authentication;

namespace ProjectManager.API.Authentication;

/// <summary>
/// Políticas de autenticação por esquema (ADR-016). Cada endpoint da API tem exatamente uma política explícita,
/// ou <c>[AllowAnonymous]</c>: nenhum esquema é escolhido por um seletor padrão, então uma credencial nunca vale
/// numa rota feita para outro tipo de ator.
/// </summary>
public static class ApiPolicies
{
    /// <summary>Pessoas: access token JWT com sessão (<c>sid</c>), conferida a cada requisição.</summary>
    public const string User = "User";

    /// <summary>Agentes: credencial própria (<c>AgentToken</c>), só nas rotas <c>/api/v1/agents/*</c>.</summary>
    public const string Agent = "Agent";

    /// <summary>Prefixo exclusivo das rotas de agentes.</summary>
    public const string AgentRoutePrefix = "api/v1/agents";

    internal static AuthorizationBuilder AddApiPolicies(this AuthorizationBuilder authorization) =>
        authorization
            .AddPolicy(User, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(AuthClaimTypes.SessionId))
            .AddPolicy(Agent, policy => policy
                .AddAuthenticationSchemes(AgentTokenAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(AuthClaimTypes.ActorKind, AuthClaimTypes.AgentActor));

    /// <summary>A rota (já combinada com a do controller) fica sob o prefixo de agentes.</summary>
    public static bool IsAgentRoute(string? template)
    {
        var normalized = (template ?? string.Empty).TrimStart('~', '/');
        return normalized.Equals(AgentRoutePrefix, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(AgentRoutePrefix + "/", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Aplica a política de cada controller na metadata dos endpoints: <see cref="ApiPolicies.Agent"/> nos controllers
/// marcados com <see cref="AgentApiAttribute"/>, com a guarda de organização, e <see cref="ApiPolicies.User"/> nos
/// demais. A marca é explícita e conferida contra a rota: um controller marcado fora de <c>/api/v1/agents</c>, ou uma
/// rota sob esse prefixo sem a marca, impede a API de iniciar. Assim, mover um controller de namespace ou criar uma
/// rota nova não muda a política em silêncio. <c>[AllowAnonymous]</c> continua valendo.
/// </summary>
internal sealed class ApiPolicyConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(application);

        foreach (var controller in application.Controllers)
        {
            var isAgentController = controller.Attributes.OfType<AgentApiAttribute>().Any();
            EnsureRoutesMatch(controller, isAgentController);

            if (isAgentController)
            {
                controller.Filters.Add(new AgentOrganizationFilter());
            }

            var policy = isAgentController ? ApiPolicies.Agent : ApiPolicies.User;
            foreach (var selector in controller.Actions.SelectMany(action => action.Selectors))
            {
                selector.EndpointMetadata.Add(new AuthorizeAttribute(policy));
            }
        }
    }

    private static void EnsureRoutesMatch(ControllerModel controller, bool isAgentController)
    {
        foreach (var route in RoutesOf(controller))
        {
            if (ApiPolicies.IsAgentRoute(route) != isAgentController)
            {
                throw new InvalidOperationException(isAgentController
                    ? $"O controller de agentes '{controller.ControllerType.FullName}' tem a rota '{route}' fora de '/{ApiPolicies.AgentRoutePrefix}'."
                    : $"A rota '{route}' de '{controller.ControllerType.FullName}' fica sob '/{ApiPolicies.AgentRoutePrefix}', mas o controller não tem [AgentApi].");
            }
        }
    }

    /// <summary>Rotas de atributo de cada ação, combinadas com as do controller.</summary>
    private static IEnumerable<string?> RoutesOf(ControllerModel controller)
    {
        var controllerRoutes = controller.Selectors.Select(selector => selector.AttributeRouteModel).DefaultIfEmpty(null).ToList();

        return
            from action in controller.Actions
            from actionSelector in action.Selectors.DefaultIfEmpty(null)
            from controllerRoute in controllerRoutes
            select AttributeRouteModel.CombineAttributeRouteModel(controllerRoute, actionSelector?.AttributeRouteModel)?.Template;
    }
}
