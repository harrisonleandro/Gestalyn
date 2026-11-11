using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using ProjectManager.Web.Api;

namespace ProjectManager.Web.Authentication;

/// <summary>
/// Sessão da Web (BFF): identidade e tokens da API ficam no cookie de autenticação, criptografado
/// pelo Data Protection e marcado como HttpOnly. O navegador nunca recebe os tokens.
/// </summary>
internal static class AccountSession
{
    public const string AccessTokenName = "access_token";
    public const string RefreshTokenName = "refresh_token";
    public const string AccessTokenExpiresAtName = "access_token_expires_at";

    public static Task SignInAsync(HttpContext httpContext, ApiUserProfile profile, ApiTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(tokens);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, profile.Id.ToString()),
                new Claim(ClaimTypes.Name, profile.DisplayName),
                new Claim(ClaimTypes.Email, profile.Email),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = tokens.RefreshTokenExpiresAt,
        };
        StoreTokens(properties, tokens);

        return httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);
    }

    public static void StoreTokens(AuthenticationProperties properties, ApiTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(properties);

        properties.StoreTokens(
        [
            new AuthenticationToken { Name = AccessTokenName, Value = tokens.AccessToken },
            new AuthenticationToken { Name = RefreshTokenName, Value = tokens.RefreshToken },
            new AuthenticationToken
            {
                Name = AccessTokenExpiresAtName,
                Value = tokens.AccessTokenExpiresAt.ToString("O", CultureInfo.InvariantCulture),
            },
        ]);
    }

    public static Task<string?> GetAccessTokenAsync(HttpContext httpContext) =>
        httpContext.GetTokenAsync(AccessTokenName);
}
