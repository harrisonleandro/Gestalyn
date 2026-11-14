using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ProjectManager.Infrastructure.Identity;

/// <summary>
/// Sucessor determinístico de um refresh token (ADR-012): <c>HMAC-SHA256(K, token)</c>, com <c>K</c>
/// derivada da chave de assinatura JWT por HKDF. Qualquer instância da API recalcula o mesmo sucessor a
/// partir do token apresentado, o que permite repetir a troca de forma idempotente sem guardar tokens em
/// texto. Sem conhecer <c>K</c>, ninguém calcula o sucessor de um token, mesmo de posse dele.
/// </summary>
internal sealed class RefreshTokenDerivation
{
    private static readonly byte[] Info = "gestalyn/refresh-token-successor/v1"u8.ToArray();

    private readonly byte[] _key;

    public RefreshTokenDerivation(IOptions<JwtOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var signingKey = Encoding.UTF8.GetBytes(options.Value.SigningKey);
        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, signingKey, outputLength: 32, salt: [], info: Info);
    }

    public string Successor(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);

        return Base64Url.EncodeToString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(refreshToken)));
    }
}
