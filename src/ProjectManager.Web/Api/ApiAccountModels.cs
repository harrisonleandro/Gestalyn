using System.Net;

namespace ProjectManager.Web.Api;

/// <summary>Tokens devolvidos pela API. Ficam somente no servidor da Web, dentro do cookie criptografado.</summary>
public sealed record ApiTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record ApiUserProfile(Guid Id, string Email, string DisplayName, bool EmailConfirmed, DateTimeOffset CreatedAt);

/// <summary>
/// Resultado de uma chamada à API para exibição na interface: sucesso, mensagem geral e erros por campo.
/// </summary>
public sealed record ApiResult(
    bool Succeeded,
    HttpStatusCode? StatusCode,
    string? Message,
    IReadOnlyDictionary<string, string[]> FieldErrors)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    public static ApiResult Success(HttpStatusCode statusCode) => new(true, statusCode, null, NoErrors);

    public static ApiResult Failure(HttpStatusCode? statusCode, string message, IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        new(false, statusCode, message, fieldErrors ?? NoErrors);
}

public sealed record ApiResult<T>(ApiResult Result, T? Value);

public enum RefreshOutcome
{
    Refreshed,
    Rejected,
    Unavailable,
}
