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

    public static readonly TimeSpan MaxRefreshReplayWindow = TimeSpan.FromSeconds(30);

    public const int MaxRefreshReplayLimit = 10;

    /// <summary>
    /// Janela em que reapresentar um refresh token recém-trocado devolve o mesmo sucessor (rotação
    /// idempotente para requisições simultâneas), desde que o sucessor ainda não tenha sido usado.
    /// Fora dela, ou com o sucessor já usado, o reuso revoga a sessão (ADR-012). O padrão (10 s) é o
    /// timeout das chamadas da Web à API. Zero desliga a repetição: todo reuso revoga.
    /// </summary>
    public TimeSpan RefreshReplayWindow { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Quantas vezes um mesmo token pode ser repetido dentro da janela antes de revogar a sessão.</summary>
    public int MaxRefreshReplays { get; set; } = 5;

    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    internal bool IsValid() =>
        !string.IsNullOrWhiteSpace(Issuer)
        && !string.IsNullOrWhiteSpace(Audience)
        && Encoding.UTF8.GetByteCount(SigningKey) >= MinimumSigningKeyBytes
        && AccessTokenLifetime > TimeSpan.Zero
        && SessionLifetime > AccessTokenLifetime
        && RefreshReplayWindow >= TimeSpan.Zero
        && RefreshReplayWindow <= MaxRefreshReplayWindow
        && RefreshReplayWindow < AccessTokenLifetime
        && MaxRefreshReplays is >= 0 and <= MaxRefreshReplayLimit;
}
