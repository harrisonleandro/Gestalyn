using ProjectManager.Web.Authentication;

namespace ProjectManager.Web.Api;

/// <summary>Token da API e cancelamento da requisição em curso. No servidor, vêm do cookie e do <c>HttpContext</c>.</summary>
public interface IApiRequestContext
{
    Task<string?> GetAccessTokenAsync();

    CancellationToken RequestAborted { get; }
}

/// <summary>
/// Implementação padrão: a Web é BFF e renderiza no servidor (ADR-015, D2), então o token sai do cookie criptografado
/// da requisição atual e nunca vai para o navegador.
/// </summary>
internal sealed class HttpApiRequestContext(IHttpContextAccessor httpContextAccessor) : IApiRequestContext
{
    public CancellationToken RequestAborted => httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;

    public Task<string?> GetAccessTokenAsync() =>
        httpContextAccessor.HttpContext is { } httpContext ? AccountSession.GetAccessTokenAsync(httpContext) : Task.FromResult<string?>(null);
}

/// <summary>
/// Chamadas autenticadas à API em nome do usuário. As rotas por ID de recurso levam a organização ativa no cabeçalho
/// <c>X-Gestalyn-Organization</c>. A Web não decide permissões: só repassa o que a API devolve.
/// </summary>
public sealed class GestalynApi(HttpClient httpClient, IApiRequestContext requestContext, ILogger<GestalynApi> logger)
{
    private const string NoSessionMessage = "Sua sessão expirou. Entre novamente.";

    public Task<ApiResult<T>> GetAsync<T>(string path, Guid? organizationId = null) =>
        SendAsync<T>(HttpMethod.Get, path, body: null, organizationId);

    public async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, Guid? organizationId = null)
    {
        var accessToken = await requestContext.GetAccessTokenAsync();
        if (accessToken is null)
        {
            return new ApiResult<T>(ApiResult.Failure(System.Net.HttpStatusCode.Unauthorized, NoSessionMessage), default);
        }

        return await ApiRequests.SendAsync<T>(httpClient, logger, method, $"api/v1/{path}", body, accessToken, requestContext.RequestAborted, organizationId);
    }

    public async Task<ApiResult> SendAsync(HttpMethod method, string path, object? body = null, Guid? organizationId = null) =>
        (await SendAsync<object>(method, path, body, organizationId)).Result;

    /// <summary>Resposta crua para repassar um arquivo. Quem recebe descarta a resposta.</summary>
    public async Task<HttpResponseMessage?> OpenAsync(string path, Guid organizationId)
    {
        var accessToken = await requestContext.GetAccessTokenAsync();
        return accessToken is null
            ? null
            : await ApiRequests.SendRawAsync(httpClient, logger, $"api/v1/{path}", accessToken, organizationId, requestContext.RequestAborted);
    }
}
