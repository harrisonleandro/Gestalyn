using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectManager.Web.Api;

/// <summary>
/// Envio de requisições à API e conversão das respostas (incluindo Problem Details) em resultados para
/// a interface. Falhas de rede viram uma mensagem de indisponibilidade, registrada em log.
/// </summary>
internal static partial class ApiRequests
{
    public const string OrganizationHeader = "X-Gestalyn-Organization";

    private const string UnavailableMessage = "Não foi possível falar com o serviço agora. Tente novamente em instantes.";

    /// <summary>Enumerações trafegam pelo nome, como a API as envia.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<ApiResult<T>> SendAsync<T>(
        HttpClient httpClient,
        ILogger logger,
        HttpMethod method,
        string path,
        object? body,
        string? accessToken,
        CancellationToken cancellationToken,
        Guid? organizationId = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is HttpContent content)
        {
            request.Content = content;
        }
        else if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        return await SendAsync<T>(httpClient, logger, request, accessToken, organizationId, cancellationToken);
    }

    /// <summary>Resposta crua (para downloads). Quem recebe descarta a resposta; <c>null</c> se a API estiver indisponível.</summary>
    public static async Task<HttpResponseMessage?> SendRawAsync(
        HttpClient httpClient,
        ILogger logger,
        string path,
        string? accessToken,
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        Authorize(request, accessToken, organizationId);
        try
        {
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            LogApiUnavailable(logger, exception, path);
            return null;
        }
    }

    private static async Task<ApiResult<T>> SendAsync<T>(
        HttpClient httpClient,
        ILogger logger,
        HttpRequestMessage request,
        string? accessToken,
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.OriginalString;
        Authorize(request, accessToken, organizationId);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new ApiResult<T>(await ReadFailureAsync(response, cancellationToken), default);
            }

            var value = response.StatusCode != HttpStatusCode.NoContent && typeof(T) != typeof(object)
                ? await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
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

    private static void Authorize(HttpRequestMessage request, string? accessToken, Guid? organizationId)
    {
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (organizationId is { } organization)
        {
            request.Headers.TryAddWithoutValidation(OrganizationHeader, organization.ToString());
        }
    }

    private static async Task<ApiResult> ReadFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        HttpValidationProblemDetails? problem = null;
        if (response.Content.Headers.ContentType?.MediaType is "application/problem+json" or "application/json")
        {
            problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(cancellationToken);
        }

        var message = response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => "Muitas tentativas. Aguarde um minuto e tente novamente.",
            HttpStatusCode.Unauthorized => "Sua sessão expirou. Entre novamente.",
            _ => problem?.Detail ?? problem?.Title ?? "Não foi possível concluir a operação.",
        };

        return ApiResult.Failure(response.StatusCode, message, problem?.Errors as IReadOnlyDictionary<string, string[]>);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "API indisponível ao chamar {Path}.")]
    private static partial void LogApiUnavailable(ILogger logger, Exception exception, string path);
}
