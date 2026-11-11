using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using ProjectManager.API.Authentication;
using ProjectManager.Application.Abstractions.Authentication;

namespace ProjectManager.API.Controllers;

/// <summary>
/// Cadastro, sessão e recuperação de senha. As respostas não revelam se um e-mail está cadastrado.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting(AuthRateLimiting.PolicyName)]
public sealed class AuthController(IAuthenticationService authentication) : ControllerBase
{
    /// <summary>Cadastra um usuário e envia o e-mail de confirmação.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await authentication.RegisterAsync(request, cancellationToken);

        return result.Succeeded ? Accepted() : ValidationFailure(result);
    }

    /// <summary>Confirma o e-mail com o token recebido no link.</summary>
    [HttpPost("confirm-email")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        var result = await authentication.ConfirmEmailAsync(request, cancellationToken);

        return result.Succeeded ? NoContent() : InvalidLink();
    }

    /// <summary>Reenvia o e-mail de confirmação, se houver uma conta pendente.</summary>
    [HttpPost("resend-confirmation")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ResendConfirmationAsync(EmailRequest request, CancellationToken cancellationToken)
    {
        await authentication.ResendConfirmationAsync(request, cancellationToken);

        return Accepted();
    }

    /// <summary>Abre uma sessão e retorna o access token e o refresh token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthTokens>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authentication.LoginAsync(request, cancellationToken);

        return result.Error switch
        {
            null => Ok(result.Value),
            AuthError.EmailNotConfirmed => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "E-mail não confirmado.",
                detail: "Confirme o e-mail pelo link enviado ou solicite um novo."),
            _ => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Não foi possível entrar.",
                detail: "E-mail ou senha incorretos, ou a conta está temporariamente bloqueada."),
        };
    }

    /// <summary>Troca o refresh token por um novo par de tokens (o refresh token anterior deixa de valer).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AuthTokens>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var result = await authentication.RefreshAsync(request, cancellationToken);

        return result.Succeeded
            ? Ok(result.Value)
            : Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sessão inválida ou expirada.", detail: "Entre novamente.");
    }

    /// <summary>Encerra a sessão atual. O access token e o refresh token deixam de valer imediatamente.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken)
    {
        if (!User.TryGetSession(out var userId, out var sessionId))
        {
            return Unauthorized();
        }

        await authentication.LogoutAsync(userId, sessionId, cancellationToken);

        return NoContent();
    }

    /// <summary>Dados do usuário autenticado.</summary>
    [HttpGet("me")]
    [ProducesResponseType<UserProfile>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMeAsync(CancellationToken cancellationToken)
    {
        if (!User.TryGetSession(out var userId, out _))
        {
            return Unauthorized();
        }

        var profile = await authentication.GetProfileAsync(userId, cancellationToken);

        return profile is null ? Unauthorized() : Ok(profile);
    }

    /// <summary>Envia o link de redefinição de senha, se houver uma conta com o e-mail.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPasswordAsync(EmailRequest request, CancellationToken cancellationToken)
    {
        await authentication.ForgotPasswordAsync(request, cancellationToken);

        return Accepted();
    }

    /// <summary>Redefine a senha com o token recebido no link e encerra todas as sessões.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await authentication.ResetPasswordAsync(request, cancellationToken);

        return result.Error switch
        {
            null => NoContent(),
            AuthError.InvalidToken => InvalidLink(),
            _ => ValidationFailure(result),
        };
    }

    private ObjectResult InvalidLink() =>
        Problem(statusCode: StatusCodes.Status400BadRequest, title: "Link inválido ou expirado.", detail: "Solicite um novo link.");

    private ActionResult ValidationFailure(AuthResult result)
    {
        var modelState = new ModelStateDictionary();
        foreach (var (field, messages) in result.ValidationErrors)
        {
            foreach (var message in messages)
            {
                modelState.AddModelError(field, message);
            }
        }

        return ValidationProblem(modelState);
    }
}
