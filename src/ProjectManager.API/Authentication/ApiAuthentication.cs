using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProjectManager.Application.Abstractions.Authentication;
using ProjectManager.Infrastructure.Identity;

namespace ProjectManager.API.Authentication;

/// <summary>
/// Autenticação da API por access token JWT. Além da assinatura e da expiração, cada requisição
/// confirma que a sessão do token continua ativa — o logout vale imediatamente.
/// </summary>
internal static class ApiAuthentication
{
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = jwt.CreateSigningKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = AuthClaimTypes.Subject,
                };
                bearer.Events = new JwtBearerEvents { OnTokenValidated = EnsureSessionIsActiveAsync };
            });

        services.AddAuthorization();

        return services;
    }

    public static bool TryGetSession(this ClaimsPrincipal user, out Guid userId, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        return Guid.TryParse(user.FindFirstValue(AuthClaimTypes.Subject), out userId)
            && Guid.TryParse(user.FindFirstValue(AuthClaimTypes.SessionId), out sessionId);
    }

    private static async Task EnsureSessionIsActiveAsync(TokenValidatedContext context)
    {
        if (context.Principal is null || !context.Principal.TryGetSession(out var userId, out var sessionId))
        {
            context.Fail("Token sem usuário ou sessão.");
            return;
        }

        var authentication = context.HttpContext.RequestServices.GetRequiredService<IAuthenticationService>();
        if (!await authentication.IsSessionActiveAsync(userId, sessionId, context.HttpContext.RequestAborted))
        {
            context.Fail("Sessão encerrada ou expirada.");
        }
    }
}
