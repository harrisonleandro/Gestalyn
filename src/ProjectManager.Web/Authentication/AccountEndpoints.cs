using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using ProjectManager.Web.Api;

namespace ProjectManager.Web.Authentication;

internal static class AccountEndpoints
{
    public const string LogoutPath = "/conta/sair";

    /// <summary>
    /// Logout por POST (com antiforgery, via <c>[FromForm]</c>): revoga a sessão na API e remove o cookie.
    /// </summary>
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(LogoutPath, async (
            HttpContext httpContext,
            ApiAccountClient apiAccountClient,
            [FromForm] string? returnUrl) =>
        {
            var accessToken = await AccountSession.GetAccessTokenAsync(httpContext);
            if (accessToken is not null)
            {
                // Mesmo se a API estiver indisponível, o cookie local é removido.
                await apiAccountClient.LogoutAsync(accessToken, httpContext.RequestAborted);
            }

            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return TypedResults.LocalRedirect(ReturnUrls.Sanitize(returnUrl));
        }).RequireAuthorization();

        return endpoints;
    }
}
