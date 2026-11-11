using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace ProjectManager.Infrastructure.Identity;

/// <summary>
/// Geração e hash de refresh tokens. Os tokens têm 256 bits aleatórios; por isso um hash rápido
/// (SHA-256) é adequado — não há o que proteger contra força bruta como em senhas.
/// </summary>
internal static class RefreshTokens
{
    private const int TokenBytes = 32;

    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
