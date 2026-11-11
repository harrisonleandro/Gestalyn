using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ProjectManager.Application.Abstractions.Authentication;

namespace ProjectManager.Infrastructure.Identity;

/// <summary>
/// Emite access tokens JWT (HMAC-SHA256) com o usuário (<c>sub</c>) e a sessão (<c>sid</c>).
/// </summary>
internal sealed class AccessTokenIssuer(IOptions<JwtOptions> options, TimeProvider timeProvider)
{
    private static readonly JsonWebTokenHandler TokenHandler = new();

    public (string Token, DateTimeOffset ExpiresAt) Issue(Guid userId, Guid sessionId)
    {
        var jwt = options.Value;
        var now = timeProvider.GetUtcNow();
        var expiresAt = now + jwt.AccessTokenLifetime;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [AuthClaimTypes.Subject] = userId.ToString(),
                [AuthClaimTypes.SessionId] = sessionId.ToString(),
                [AuthClaimTypes.TokenId] = Guid.NewGuid().ToString(),
            },
            SigningCredentials = new SigningCredentials(jwt.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
        };

        return (TokenHandler.CreateToken(descriptor), expiresAt);
    }
}
