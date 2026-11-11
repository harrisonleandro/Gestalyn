using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Endpoints;
using ProjectManager.Web.Api;

namespace ProjectManager.Web.Authentication;

/// <summary>
/// A cada navegação, renova o access token da API quando ele está para expirar. Se a API recusar o
/// refresh token (sessão revogada ou expirada), a sessão da Web também é encerrada.
/// </summary>
internal sealed partial class TokenRefreshCookieEvents(
    ApiAccountClient apiAccountClient,
    TokenRefreshCoordinator coordinator,
    TimeProvider timeProvider,
    ILogger<TokenRefreshCookieEvents> logger) : CookieAuthenticationEvents
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Arquivos estáticos e o hub do Blazor não precisam de token: renova só em páginas.
        if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<ComponentTypeMetadata>() is null)
        {
            return;
        }

        var refreshToken = context.Properties.GetTokenValue(AccountSession.RefreshTokenName);
        var expiresAtText = context.Properties.GetTokenValue(AccountSession.AccessTokenExpiresAtName);
        if (refreshToken is null
            || !DateTimeOffset.TryParse(expiresAtText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt))
        {
            await RejectAsync(context);
            return;
        }

        if (timeProvider.GetUtcNow() < expiresAt - RefreshMargin)
        {
            return;
        }

        // A renovação é compartilhada entre requisições: não depende do cancelamento de uma delas
        // (o tempo é limitado pelo timeout do HttpClient).
        var (outcome, tokens) = await coordinator.RefreshAsync(
            refreshToken,
            () => apiAccountClient.RefreshAsync(refreshToken, CancellationToken.None));

        switch (outcome)
        {
            case RefreshOutcome.Refreshed when tokens is not null:
                AccountSession.StoreTokens(context.Properties, tokens);
                context.ShouldRenew = true;
                break;

            case RefreshOutcome.Rejected:
                LogSessionEnded(logger);
                await RejectAsync(context);
                break;

            default:
                // API indisponível: mantém a sessão; as chamadas seguintes informarão a falha.
                LogRefreshUnavailable(logger);
                break;
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sessão da API encerrada ou expirada; sessão da Web removida.")]
    private static partial void LogSessionEnded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não foi possível renovar o access token: API indisponível.")]
    private static partial void LogRefreshUnavailable(ILogger logger);
}
