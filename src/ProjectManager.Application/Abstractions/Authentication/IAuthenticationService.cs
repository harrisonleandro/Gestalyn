namespace ProjectManager.Application.Abstractions.Authentication;

/// <summary>
/// Casos de uso de identidade: cadastro, confirmação de e-mail, sessão (login, renovação, logout)
/// e recuperação de senha. As respostas não revelam se um e-mail está cadastrado.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Cadastra o usuário e envia o e-mail de confirmação. Se o e-mail já estiver cadastrado, envia
    /// um aviso ao titular e retorna o mesmo resultado de sucesso.
    /// </summary>
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<AuthResult> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken);

    Task ResendConfirmationAsync(EmailRequest request, CancellationToken cancellationToken);

    Task<AuthResult<AuthTokens>> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Troca um refresh token válido por um novo par de tokens (rotação). Reutilizar um refresh token
    /// já trocado revoga a sessão inteira.
    /// </summary>
    Task<AuthResult<AuthTokens>> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken);

    Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);

    Task<bool> IsSessionActiveAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);

    Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken);

    Task ForgotPasswordAsync(EmailRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Redefine a senha e revoga todas as sessões do usuário.
    /// </summary>
    Task<AuthResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);
}
