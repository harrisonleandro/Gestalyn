namespace ProjectManager.Application.Abstractions.Authentication;

public enum AuthError
{
    /// <summary>E-mail ou senha incorretos, ou conta temporariamente bloqueada (resposta neutra).</summary>
    InvalidCredentials,

    /// <summary>Senha correta, mas e-mail ainda não confirmado.</summary>
    EmailNotConfirmed,

    /// <summary>Token (refresh, confirmação ou redefinição) inválido, expirado ou já usado.</summary>
    InvalidToken,

    /// <summary>Dados de entrada rejeitados (ex.: política de senha).</summary>
    ValidationFailed,
}

public class AuthResult
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    protected AuthResult(AuthError? error, IReadOnlyDictionary<string, string[]>? validationErrors)
    {
        Error = error;
        ValidationErrors = validationErrors ?? NoErrors;
    }

    public AuthError? Error { get; }

    public bool Succeeded => Error is null;

    public IReadOnlyDictionary<string, string[]> ValidationErrors { get; }

    public static AuthResult Success() => new(null, null);

    public static AuthResult Failure(AuthError error) => new(error, null);

    public static AuthResult Invalid(IReadOnlyDictionary<string, string[]> validationErrors) =>
        new(AuthError.ValidationFailed, validationErrors);

    public static AuthResult<T> Success<T>(T value) => new(value, null);

    public static AuthResult<T> Failure<T>(AuthError error) => new(default, error);
}

public sealed class AuthResult<T> : AuthResult
{
    internal AuthResult(T? value, AuthError? error)
        : base(error, validationErrors: null) => Value = value;

    public T? Value { get; }
}
