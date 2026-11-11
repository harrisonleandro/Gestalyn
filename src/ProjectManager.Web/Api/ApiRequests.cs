using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ProjectManager.Web.Api;

/// <summary>
/// Envio de requisições à API e conversão das respostas (incluindo Problem Details) em resultados para
/// a interface. Falhas de rede viram uma mensagem de indisponibilidade, registrada em log.
/// </summary>
internal static partial class ApiRequests
{
    private const string UnavailableMessage = "Não foi possível falar com o serviço agora. Tente novamente em instantes.";

    public static async Task<ApiResult<T>> SendAsync<T>(
        HttpClient httpClient,
        ILogger logger,
        HttpMethod method,
        string path,
        object? body,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
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
            LogApiUnavailable(logger, exception, path);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogApiUnavailable(logger, exception, path);
        }
        catch (JsonException exception)
        {
            LogApiUnavailable(logger, exception, path);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "API indisponível ao chamar {Path}.")]
    private static partial void LogApiUnavailable(ILogger logger, Exception exception, string path);
}
