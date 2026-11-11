using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ProjectManager.Infrastructure.Identity;

/// <summary>
/// Emissão e validação dos access tokens (seção <c>Authentication:Jwt</c>). A chave de assinatura
/// nunca é versionada: user-secrets em desenvolvimento, variável de ambiente ou cofre nos demais ambientes.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Authentication:Jwt";

    public const int MinimumSigningKeyBytes = 32;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string SigningKey { get; set; } = string.Empty;

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(30);

    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    internal bool IsValid() =>
        !string.IsNullOrWhiteSpace(Issuer)
        && !string.IsNullOrWhiteSpace(Audience)
        && Encoding.UTF8.GetByteCount(SigningKey) >= MinimumSigningKeyBytes
        && AccessTokenLifetime > TimeSpan.Zero
        && SessionLifetime > AccessTokenLifetime;
}
