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
}

/// <summary>
/// Aplica a política de cada controller na metadata dos endpoints: <see cref="ApiPolicies.Agent"/> nos controllers de
/// agentes (namespace <c>ProjectManager.API.Agents</c>), com a guarda de organização, e <see cref="ApiPolicies.User"/>
/// nos demais. <c>[AllowAnonymous]</c> continua valendo, porque a autorização dos endpoints o respeita.
/// </summary>
internal sealed class ApiPolicyConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(application);

        foreach (var controller in application.Controllers)
        {
            var isAgentController = controller.ControllerType.Namespace == typeof(AgentOrganizationFilter).Namespace;
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
}
