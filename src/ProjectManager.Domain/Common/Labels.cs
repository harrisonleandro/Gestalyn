namespace ProjectManager.Domain.Common;

/// <summary>
/// Regras comuns de etiquetas em texto livre (projetos e tarefas): de 1 a 50 caracteres, únicas sem
/// diferenciar maiúsculas e minúsculas e com um limite por item.
/// </summary>
public static class Labels
{
    public const int NameMaxLength = 50;
    public const int MaxPerItem = 20;

    /// <summary>Forma usada para unicidade e filtros.</summary>
    public static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Valida e unifica nomes de etiquetas, mantendo a primeira grafia informada. Retorna <c>null</c> e
    /// a mensagem de erro quando o conjunto é inválido.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? Normalize(IEnumerable<string> names, out string? error)
    {
        ArgumentNullException.ThrowIfNull(names);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in names)
        {
            var name = raw?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > NameMaxLength)
            {
                error = $"Cada etiqueta deve ter de 1 a {NameMaxLength} caracteres.";
                return null;
            }

            result.TryAdd(NormalizeName(name), name);
        }

        if (result.Count > MaxPerItem)
        {
            error = $"São permitidas no máximo {MaxPerItem} etiquetas.";
            return null;
        }

        error = null;
        return result;
    }
}
