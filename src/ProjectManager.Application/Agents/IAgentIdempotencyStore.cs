using ProjectManager.Application.Abstractions.MultiTenancy;

namespace ProjectManager.Application.Agents;

/// <summary>Uma requisição idempotente de agente: a chave vale por credencial, presa ao método, à rota e ao corpo.</summary>
public sealed record IdempotentRequest(ActiveAgent Agent, string Key, string Method, string RouteTemplate, string RequestHash);

/// <summary>Resposta guardada de uma requisição anterior com a mesma chave.</summary>
public sealed record StoredIdempotentResponse(
    string Method,
    string RouteTemplate,
    string RequestHash,
    int StatusCode,
    string Body,
    string? Location,
    Guid? ProjectId)
{
    /// <summary>Mesma requisição: mesmo método, mesmo modelo de rota e mesmo hash (caminho, query e corpo).</summary>
    public bool Matches(IdempotentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return string.Equals(Method, request.Method, StringComparison.Ordinal)
            && string.Equals(RouteTemplate, request.RouteTemplate, StringComparison.Ordinal)
            && string.Equals(RequestHash, request.RequestHash, StringComparison.Ordinal);
    }
}

/// <summary>
/// Idempotência transacional dos POSTs de agentes (ADR-016, D9). A sessão abre uma transação e trava a chave: a
/// operação, a gravação da resposta e o commit acontecem juntos, ou nada fica gravado.
/// </summary>
public interface IAgentIdempotencyStore
{
    /// <summary>Abre a sessão da chave, ou devolve <c>null</c> se outra requisição com a mesma chave está em processamento.</summary>
    Task<IIdempotencySession?> BeginAsync(IdempotentRequest request, CancellationToken cancellationToken);
}

public interface IIdempotencySession : IAsyncDisposable
{
    /// <summary>Resposta guardada e ainda válida para a mesma chave, se houver.</summary>
    StoredIdempotentResponse? Existing { get; }

    /// <summary>Desfaz o que a operação gravou, mantendo a sessão (para guardar uma resposta de erro, por exemplo).</summary>
    Task DiscardOperationAsync(CancellationToken cancellationToken);

    /// <summary>Guarda a resposta e confirma a operação e a resposta juntas.</summary>
    Task CompleteAsync(int statusCode, string body, string? location, Guid? projectId, CancellationToken cancellationToken);

    /// <summary>Encerra sem guardar nada (resposta que depende de autorização, limite ou falha); desfaz a operação.</summary>
    Task AbandonAsync(CancellationToken cancellationToken);
}
