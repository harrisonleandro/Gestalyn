using System.Text.Json;

namespace ProjectManager.Web.Api;

/// <summary>
/// Cliente HTTP tipado para o endpoint <c>/health</c> da API.
/// </summary>
public sealed partial class ApiHealthClient(HttpClient httpClient, ILogger<ApiHealthClient> logger)
{
    private static readonly Uri HealthPath = new("health", UriKind.Relative);

    public async Task<ApiHealthResult> GetHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            // O endpoint responde com JSON tanto em 200 (Healthy/Degraded) quanto em 503 (Unhealthy).
            using var response = await httpClient.GetAsync(HealthPath, cancellationToken);
            var payload = await response.Content.ReadFromJsonAsync<HealthPayload>(cancellationToken);

            return payload?.Status is { Length: > 0 } status
                ? ApiHealthResult.Reachable(status, payload.Version)
                : ApiHealthResult.Unreachable($"Resposta inesperada da API (HTTP {(int)response.StatusCode}).");
        }
        catch (HttpRequestException exception)
        {
            LogApiUnreachable(logger, exception, httpClient.BaseAddress);
            return ApiHealthResult.Unreachable("Não foi possível conectar à API.");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogApiUnreachable(logger, exception, httpClient.BaseAddress);
            return ApiHealthResult.Unreachable("A API não respondeu dentro do tempo limite.");
        }
        catch (JsonException exception)
        {
            LogInvalidResponse(logger, exception, httpClient.BaseAddress);
            return ApiHealthResult.Unreachable("A API retornou uma resposta em formato inválido.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "API indisponível em {BaseAddress}.")]
    private static partial void LogApiUnreachable(ILogger logger, Exception exception, Uri? baseAddress);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Resposta inválida do endpoint de saúde da API em {BaseAddress}.")]
    private static partial void LogInvalidResponse(ILogger logger, Exception exception, Uri? baseAddress);

    private sealed record HealthPayload(string? Status, string? Version);
}
