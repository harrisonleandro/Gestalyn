using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ProjectManager.Web.Api;

namespace ProjectManager.Web.Authentication;

/// <summary>
/// Garante uma única renovação por refresh token nesta instância da Web: requisições paralelas do
/// mesmo navegador compartilham o resultado, em vez de reapresentar o token já trocado.
/// Em múltiplas instâncias, a janela de tolerância da API cobre o caso (Fase 14).
/// </summary>
internal sealed class TokenRefreshCoordinator(TimeProvider timeProvider)
{
    private static readonly TimeSpan ResultLifetime = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public Task<(RefreshOutcome Outcome, ApiTokens? Tokens)> RefreshAsync(
        string refreshToken,
        Func<Task<(RefreshOutcome Outcome, ApiTokens? Tokens)>> refresh)
    {
        RemoveExpired();

        // A chave é um hash: o token em si não fica guardado na memória compartilhada.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
        var entry = _entries.GetOrAdd(key, _ => new Entry(timeProvider.GetUtcNow(), new Lazy<Task<(RefreshOutcome, ApiTokens?)>>(refresh)));

        return entry.Result.Value;
    }

    private void RemoveExpired()
    {
        var limit = timeProvider.GetUtcNow() - ResultLifetime;
        foreach (var (key, entry) in _entries)
        {
            if (entry.CreatedAt < limit)
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    private sealed record Entry(DateTimeOffset CreatedAt, Lazy<Task<(RefreshOutcome, ApiTokens?)>> Result);
}
