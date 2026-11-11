using System.Net;

namespace ProjectManager.Web.Api;

/// <summary>
/// Cliente HTTP tipado para <c>/api/v1/auth</c>.
/// </summary>
public sealed class ApiAccountClient(HttpClient httpClient, ILogger<ApiAccountClient> logger)
{
    public Task<ApiResult> RegisterAsync(string email, string password, string displayName, CancellationToken cancellationToken) =>
        PostAsync("register", new { email, password, displayName }, cancellationToken);

    public Task<ApiResult> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken) =>
        PostAsync("confirm-email", new { userId, token }, cancellationToken);

    public Task<ApiResult> ResendConfirmationAsync(string email, CancellationToken cancellationToken) =>
        PostAsync("resend-confirmation", new { email }, cancellationToken);

    public Task<ApiResult> ForgotPasswordAsync(string email, CancellationToken cancellationToken) =>
        PostAsync("forgot-password", new { email }, cancellationToken);

    public Task<ApiResult> ResetPasswordAsync(string email, string token, string newPassword, CancellationToken cancellationToken) =>
        PostAsync("reset-password", new { email, token, newPassword }, cancellationToken);

    public async Task<ApiResult<ApiTokens>> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
        await SendForValueAsync<ApiTokens>(HttpMethod.Post, "login", new { email, password }, accessToken: null, cancellationToken);

    public async Task<ApiResult<ApiUserProfile>> GetProfileAsync(string accessToken, CancellationToken cancellationToken) =>
        await SendForValueAsync<ApiUserProfile>(HttpMethod.Get, "me", body: null, accessToken, cancellationToken);

    public async Task<(RefreshOutcome Outcome, ApiTokens? Tokens)> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var response = await SendForValueAsync<ApiTokens>(HttpMethod.Post, "refresh", new { refreshToken }, accessToken: null, cancellationToken);

        return response.Result switch
        {
            { Succeeded: true } => (RefreshOutcome.Refreshed, response.Value),
            { StatusCode: HttpStatusCode.Unauthorized } => (RefreshOutcome.Rejected, null),
            _ => (RefreshOutcome.Unavailable, null),
        };
    }

    public async Task<ApiResult> LogoutAsync(string accessToken, CancellationToken cancellationToken) =>
        (await SendForValueAsync<object>(HttpMethod.Post, "logout", body: null, accessToken, cancellationToken)).Result;

    private async Task<ApiResult> PostAsync(string action, object body, CancellationToken cancellationToken) =>
        (await SendForValueAsync<object>(HttpMethod.Post, action, body, accessToken: null, cancellationToken)).Result;

    private Task<ApiResult<T>> SendForValueAsync<T>(
        HttpMethod method,
        string action,
        object? body,
        string? accessToken,
        CancellationToken cancellationToken) =>
        ApiRequests.SendAsync<T>(httpClient, logger, method, $"api/v1/auth/{action}", body, accessToken, cancellationToken);
}
