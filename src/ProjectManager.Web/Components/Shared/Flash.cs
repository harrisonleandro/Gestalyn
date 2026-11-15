using Microsoft.AspNetCore.WebUtilities;

namespace ProjectManager.Web;

/// <summary>
/// Aviso de sucesso depois do redirecionamento (post-redirect-get). A URL leva só uma chave conhecida, nunca um texto
/// livre: uma URL não consegue fazer a página exibir uma mensagem inventada.
/// </summary>
public static class Flash
{
    public const string QueryName = "aviso";

    private static readonly Dictionary<string, string> Messages = new(StringComparer.Ordinal)
    {
        ["salvo"] = "Alterações salvas.",
        ["criado"] = "Criado com sucesso.",
        ["excluido"] = "Excluído.",
        ["arquivado"] = "Arquivado.",
        ["restaurado"] = "Restaurado.",
        ["movida"] = "Tarefa movida.",
        ["enviado"] = "Arquivo enviado.",
        ["comentado"] = "Comentário publicado.",
        ["convidado"] = "Convite enviado.",
        ["revogado"] = "Convite revogado.",
        ["removido"] = "Removido.",
        ["registrado"] = "Atividade registrada.",
        ["decidido"] = "Decisão registrada.",
    };

    public static string With(string uri, string key)
    {
        var withoutFlash = Without(uri);
        return QueryHelpers.AddQueryString(withoutFlash, QueryName, key);
    }

    public static string? MessageFor(string? key) => key is not null && Messages.TryGetValue(key, out var message) ? message : null;

    private static string Without(string uri)
    {
        var questionMark = uri.IndexOf('?', StringComparison.Ordinal);
        if (questionMark < 0)
        {
            return uri;
        }

        var query = QueryHelpers.ParseQuery(uri[questionMark..]);
        query.Remove(QueryName);
        var path = uri[..questionMark];
        return query.Count == 0 ? path : QueryHelpers.AddQueryString(path, query.SelectMany(pair => pair.Value.Select(value => new KeyValuePair<string, string?>(pair.Key, value))));
    }
}
