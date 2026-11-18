using System.Buffers;

namespace ProjectManager.Domain.Agents;

/// <summary>
/// Formato dos tokens de agentes (ADR-016): <c>gsk_</c> seguido de 43 caracteres base64url, que codificam
/// 32 bytes aleatórios (256 bits). A geração e o hash ficam na infraestrutura.
/// </summary>
public static class AgentTokens
{
    public const string Marker = "gsk_";
    public const int RandomPartLength = 43;
    public const int TokenLength = 47;
    public const int PrefixLength = 8;
    public const int HashLength = 64;

    private static readonly SearchValues<char> Base64UrlCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    private static readonly SearchValues<char> HexCharacters = SearchValues.Create("0123456789ABCDEF");

    public static bool IsWellFormed(string? token) =>
        token is { Length: TokenLength }
        && token.StartsWith(Marker, StringComparison.Ordinal)
        && !token.AsSpan(Marker.Length).ContainsAnyExcept(Base64UrlCharacters);

    /// <summary>Prefixo de identificação: os primeiros caracteres depois de <c>gsk_</c>.</summary>
    public static string PrefixOf(string token) =>
        IsWellFormed(token)
            ? token.Substring(Marker.Length, PrefixLength)
            : throw new ArgumentException("Token de agente malformado.", nameof(token));

    public static bool IsPrefix(string? prefix) =>
        prefix is { Length: PrefixLength } && !prefix.AsSpan().ContainsAnyExcept(Base64UrlCharacters);

    /// <summary>SHA-256 em hexadecimal maiúsculo, como gerado por <c>Convert.ToHexString</c>.</summary>
    public static bool IsHash(string? hash) =>
        hash is { Length: HashLength } && !hash.AsSpan().ContainsAnyExcept(HexCharacters);
}
