using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using ProjectManager.Application.Abstractions.Authentication;
using ProjectManager.Application.Abstractions.Email;

namespace ProjectManager.Infrastructure.Identity;

/// <summary>
/// Implementação dos casos de uso de identidade sobre o ASP.NET Core Identity. Usa o
/// <see cref="UserManager{TUser}"/> diretamente (e não o SignInManager) para que bloqueio e e-mail
/// não confirmado só sejam informados depois da verificação da senha, sem revelar contas existentes.
/// </summary>
internal sealed partial class AuthenticationService(
    UserManager<ApplicationUser> userManager,
    SessionManager sessions,
    IEmailSender emailSender,
    AccountEmails accountEmails,
    ILogger<AuthenticationService> logger) : IAuthenticationService
{
    // Hash fictício verificado quando o e-mail não existe, para igualar o tempo de resposta do login.
    private static readonly Lazy<string> DummyPasswordHash = new(() =>
        new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), Guid.NewGuid().ToString()));

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = request.Email.Trim();
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            // Mesmo custo e mesma resposta de um cadastro novo; o titular é avisado por e-mail.
            _ = userManager.PasswordHasher.HashPassword(existing, request.Password);
            await TrySendAsync(accountEmails.AlreadyRegistered(existing.Email!), cancellationToken);
            return AuthResult.Success();
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return IsDuplicate(result) ? AuthResult.Success() : AuthResult.Invalid(ToValidationErrors(result, "Password"));
        }

        LogUserRegistered(logger, user.Id);
        await SendConfirmationAsync(user, cancellationToken);

        return AuthResult.Success();
    }

    public async Task<AuthResult> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null || !AccountEmails.TryDecodeToken(request.Token, out var token))
        {
            return AuthResult.Failure(AuthError.InvalidToken);
        }

        if (user.EmailConfirmed)
        {
            return AuthResult.Success();
        }

        var result = await userManager.ConfirmEmailAsync(user, token);
        return result.Succeeded ? AuthResult.Success() : AuthResult.Failure(AuthError.InvalidToken);
    }

    public async Task ResendConfirmationAsync(EmailRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is { EmailConfirmed: false })
        {
            await SendConfirmationAsync(user, cancellationToken);
        }
    }

    public async Task<AuthResult<AuthTokens>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            _ = userManager.PasswordHasher.VerifyHashedPassword(new ApplicationUser(), DummyPasswordHash.Value, request.Password);
            LogLoginRejected(logger, null, "conta inexistente");
            return AuthResult.Failure<AuthTokens>(AuthError.InvalidCredentials);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            LogLoginRejected(logger, user.Id, "conta bloqueada");
            return AuthResult.Failure<AuthTokens>(AuthError.InvalidCredentials);
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            LogLoginRejected(logger, user.Id, "senha incorreta");
            return AuthResult.Failure<AuthTokens>(AuthError.InvalidCredentials);
        }

        await userManager.ResetAccessFailedCountAsync(user);

        if (!user.EmailConfirmed)
        {
            LogLoginRejected(logger, user.Id, "e-mail não confirmado");
            return AuthResult.Failure<AuthTokens>(AuthError.EmailNotConfirmed);
        }

        var tokens = await sessions.CreateAsync(user.Id, cancellationToken);
        LogLoginSucceeded(logger, user.Id);

        return AuthResult.Success(tokens);
    }

    public async Task<AuthResult<AuthTokens>> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tokens = await sessions.RefreshAsync(request.RefreshToken, cancellationToken);
        return tokens is null
            ? AuthResult.Failure<AuthTokens>(AuthError.InvalidToken)
            : AuthResult.Success(tokens);
    }

    public Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        sessions.RevokeAsync(userId, sessionId, SessionRevocationReason.Logout, cancellationToken);

    public Task<bool> IsSessionActiveAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        sessions.IsActiveAsync(userId, sessionId, cancellationToken);

    public async Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());

        return user is null
            ? null
            : new UserProfile(user.Id, user.Email!, user.DisplayName, user.EmailConfirmed, user.CreatedAt);
    }

    public async Task ForgotPasswordAsync(EmailRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return;
        }

        if (!user.EmailConfirmed)
        {
            // Sem e-mail confirmado não há como provar a titularidade; reenvia a confirmação.
            await SendConfirmationAsync(user, cancellationToken);
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        await TrySendAsync(accountEmails.PasswordReset(user.Email!, token), cancellationToken);
    }

    public async Task<AuthResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !AccountEmails.TryDecodeToken(request.Token, out var token))
        {
            return AuthResult.Failure(AuthError.InvalidToken);
        }

        var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            return result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.InvalidToken))
                ? AuthResult.Failure(AuthError.InvalidToken)
                : AuthResult.Invalid(ToValidationErrors(result, "NewPassword"));
        }

        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        await sessions.RevokeAllAsync(user.Id, SessionRevocationReason.PasswordReset, cancellationToken);
        LogPasswordReset(logger, user.Id);

        return AuthResult.Success();
    }

    private async Task SendConfirmationAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        await TrySendAsync(accountEmails.Confirmation(user.Email!, user.DisplayName, user.Id, token), cancellationToken);
    }

    private async Task TrySendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(message, cancellationToken);
        }
        catch (EmailDeliveryException exception)
        {
            // A resposta ao cliente não muda: o usuário pode pedir o reenvio.
            LogEmailDeliveryFailed(logger, exception, message.Subject);
        }
    }

    private static bool IsDuplicate(IdentityResult result) =>
        result.Errors.Any(error => error.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName));

    private static Dictionary<string, string[]> ToValidationErrors(IdentityResult result, string passwordField) =>
        result.Errors
            .GroupBy(error => error.Code.StartsWith("Password", StringComparison.Ordinal) ? passwordField : "Email")
            .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray());

    [LoggerMessage(Level = LogLevel.Information, Message = "Usuário {UserId} cadastrado.")]
    private static partial void LogUserRegistered(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Login do usuário {UserId} concluído.")]
    private static partial void LogLoginSucceeded(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Login recusado para o usuário {UserId}: {Reason}.")]
    private static partial void LogLoginRejected(ILogger logger, Guid? userId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Senha do usuário {UserId} redefinida; sessões revogadas.")]
    private static partial void LogPasswordReset(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao enviar o e-mail '{Subject}'.")]
    private static partial void LogEmailDeliveryFailed(ILogger logger, Exception exception, string subject);
}
