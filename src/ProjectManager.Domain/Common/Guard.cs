namespace ProjectManager.Domain.Common;

internal static class Guard
{
    /// <summary>
    /// Garante um texto obrigatório com tamanho máximo e o retorna sem espaços nas extremidades.
    /// </summary>
    public static string RequiredText(string? value, int maxLength, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"O valor deve ter no máximo {maxLength} caracteres.", paramName);
        }

        return trimmed;
    }

    /// <summary>
    /// Garante um texto opcional com tamanho máximo. Valores vazios são normalizados para <c>null</c>.
    /// </summary>
    public static string? OptionalText(string? value, int maxLength, string paramName) =>
        string.IsNullOrWhiteSpace(value) ? null : RequiredText(value, maxLength, paramName);

    public static Guid RequiredId(Guid value, string paramName) =>
        value == Guid.Empty
            ? throw new ArgumentException("O identificador é obrigatório.", paramName)
            : value;
}
