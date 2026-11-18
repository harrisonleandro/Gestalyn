namespace ProjectManager.Domain.Agents;

/// <summary>
/// Escopos de uma credencial de agente (ADR-016). Limitam o que a credencial pode fazer, somados ao papel do
/// agente em cada projeto; nenhum escopo concede aprovação.
/// </summary>
public static class AgentScopes
{
    public const string ContextRead = "context:read";
    public const string TasksRead = "tasks:read";
    public const string TasksWrite = "tasks:write";
    public const string ActivitiesWrite = "activities:write";
    public const string ReportsWrite = "reports:write";

    public const int MaxLength = 32;

    public static IReadOnlyList<string> All { get; } = [ContextRead, TasksRead, TasksWrite, ActivitiesWrite, ReportsWrite];

    /// <summary>
    /// Valida e ordena os escopos pedidos, sem repetições. Retorna <c>null</c> e a mensagem quando o conjunto é
    /// vazio ou tem um escopo desconhecido.
    /// </summary>
    public static IReadOnlyList<string>? Normalize(IEnumerable<string>? scopes, out string? error)
    {
        var requested = (scopes ?? []).Select(scope => scope?.Trim() ?? string.Empty).ToHashSet(StringComparer.Ordinal);

        if (requested.Count == 0)
        {
            error = "Informe pelo menos um escopo.";
            return null;
        }

        var unknown = requested.Where(scope => !All.Contains(scope)).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            error = $"Escopo desconhecido: {string.Join(", ", unknown)}. Use {string.Join(", ", All)}.";
            return null;
        }

        error = null;
        return All.Where(requested.Contains).ToList();
    }
}
