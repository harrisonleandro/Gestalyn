using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ProjectManager.Web.Api;

/// <summary>
/// Cliente HTTP tipado para <c>/api/v1/auth</c>. Converte respostas da API (incluindo Problem Details)
/// em resultados para a interface; falhas de rede viram uma mensagem de indisponibilidade.
/// </summary>
public sealed partial class ApiAccountClient(HttpClient httpClient, ILogger<ApiAccountClient> logger)
{
    private const string UnavailableMessage = "Não foi possível falar com o serviço agora. Tente novamente em instantes.";

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

    private async Task<ApiResult<T>> SendForValueAsync<T>(
        HttpMethod method,
        string action,
        object? body,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri($"api/v1/auth/{action}", UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new ApiResult<T>(await ReadFailureAsync(response, cancellationToken), default);
            }

            var value = response.StatusCode == HttpStatusCode.OK && typeof(T) != typeof(object)
                ? await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                : default;

            return new ApiResult<T>(ApiResult.Success(response.StatusCode), value);
        }
        catch (HttpRequestException exception)
        {
            LogApiUnavailable(logger, exception, action);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogApiUnavailable(logger, exception, action);
        }
        catch (JsonException exception)
        {
            LogApiUnavailable(logger, exception, action);
        }

        return new ApiResult<T>(ApiResult.Failure(null, UnavailableMessage), default);
    }

    private static async Task<ApiResult> ReadFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        HttpValidationProblemDetails? problem = null;
        if (response.Content.Headers.ContentType?.MediaType is "application/problem+json" or "application/json")
        {
            problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(cancellationToken);
        }

        var message = response.StatusCode == HttpStatusCode.TooManyRequests
            ? "Muitas tentativas. Aguarde um minuto e tente novamente."
            : problem?.Detail ?? problem?.Title ?? "Não foi possível concluir a operação.";

        return ApiResult.Failure(response.StatusCode, message, problem?.Errors as IReadOnlyDictionary<string, string[]>);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "API indisponível ao chamar auth/{Action}.")]
    private static partial void LogApiUnavailable(ILogger logger, Exception exception, string action);
}
