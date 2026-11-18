using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
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

    internal static AuthorizationBuilder AddApiPolicies(this AuthorizationBuilder authorization) =>
        authorization.AddPolicy(User, policy => policy
            .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(AuthClaimTypes.SessionId));
}

/// <summary>
/// Aplica a política de cada controller na metadata dos endpoints. <c>[AllowAnonymous]</c> continua valendo,
/// porque a autorização dos endpoints o respeita.
/// </summary>
internal sealed class ApiPolicyConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(application);

        foreach (var action in application.Controllers.SelectMany(controller => controller.Actions))
        {
            foreach (var selector in action.Selectors)
            {
                selector.EndpointMetadata.Add(new AuthorizeAttribute(ApiPolicies.User));
            }
        }
    }
}
