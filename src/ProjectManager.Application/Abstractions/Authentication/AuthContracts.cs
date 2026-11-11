using System.ComponentModel.DataAnnotations;

namespace ProjectManager.Application.Abstractions.Authentication;

/// <summary>
/// Limites de entrada comuns. O tamanho máximo da senha evita custo excessivo de hash.
/// </summary>
public static class AuthLimits
{
    public const int EmailMaxLength = 256;
    public const int PasswordMinLength = 12;
    public const int PasswordMaxLength = 128;
    public const int DisplayNameMaxLength = 100;
    public const int TokenMaxLength = 2048;
}

public sealed class RegisterRequest
{
    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    [StringLength(AuthLimits.EmailMaxLength, ErrorMessage = "O e-mail deve ter no máximo {1} caracteres.")]
    public required string Email { get; init; }

    [Required(ErrorMessage = "Informe a senha.")]
    [StringLength(AuthLimits.PasswordMaxLength, MinimumLength = AuthLimits.PasswordMinLength, ErrorMessage = "A senha deve ter entre {2} e {1} caracteres.")]
    public required string Password { get; init; }

    [Required(ErrorMessage = "Informe o nome de exibição.")]
    [StringLength(AuthLimits.DisplayNameMaxLength, ErrorMessage = "O nome deve ter no máximo {1} caracteres.")]
    public required string DisplayName { get; init; }
}

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Informe o e-mail.")]
    [StringLength(AuthLimits.EmailMaxLength)]
    public required string Email { get; init; }

    [Required(ErrorMessage = "Informe a senha.")]
    [StringLength(AuthLimits.PasswordMaxLength)]
    public required string Password { get; init; }
}

public sealed class RefreshRequest
{
    [Required]
    [StringLength(AuthLimits.TokenMaxLength)]
    public required string RefreshToken { get; init; }
}

public sealed class EmailRequest
{
    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    [StringLength(AuthLimits.EmailMaxLength)]
    public required string Email { get; init; }
}

public sealed class ConfirmEmailRequest
{
    [Required]
    public required Guid UserId { get; init; }

    [Required]
    [StringLength(AuthLimits.TokenMaxLength)]
    public required string Token { get; init; }
}

public sealed class ResetPasswordRequest
{
    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    [StringLength(AuthLimits.EmailMaxLength)]
    public required string Email { get; init; }

    [Required]
    [StringLength(AuthLimits.TokenMaxLength)]
    public required string Token { get; init; }

    [Required(ErrorMessage = "Informe a nova senha.")]
    [StringLength(AuthLimits.PasswordMaxLength, MinimumLength = AuthLimits.PasswordMinLength, ErrorMessage = "A senha deve ter entre {2} e {1} caracteres.")]
    public required string NewPassword { get; init; }
}

/// <summary>
/// Par de tokens de uma sessão. O refresh token é exibido somente aqui; o servidor guarda apenas seu hash.
/// </summary>
public sealed record AuthTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    string TokenType = "Bearer");

public sealed record UserProfile(
    Guid Id,
    string Email,
    string DisplayName,
    bool EmailConfirmed,
    DateTimeOffset CreatedAt);
