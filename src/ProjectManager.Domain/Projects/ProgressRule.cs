using ProjectManager.Domain.Workflows;

namespace ProjectManager.Domain.Projects;

/// <summary>Quantidade de tarefas não arquivadas em cada categoria de estado.</summary>
public sealed record CategoryCounts(int Todo, int InProgress, int Blocked, int Done, int Canceled)
{
    public static CategoryCounts Empty { get; } = new(0, 0, 0, 0, 0);

    public int Total => Todo + InProgress + Blocked + Done + Canceled;

    public static CategoryCounts From(IEnumerable<(StateCategory Category, int Count)> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var counts = rows.GroupBy(row => row.Category).ToDictionary(group => group.Key, group => group.Sum(row => row.Count));
        return new CategoryCounts(
            counts.GetValueOrDefault(StateCategory.Todo),
            counts.GetValueOrDefault(StateCategory.InProgress),
            counts.GetValueOrDefault(StateCategory.Blocked),
            counts.GetValueOrDefault(StateCategory.Done),
            counts.GetValueOrDefault(StateCategory.Canceled));
    }
}

/// <summary>Progresso calculado pela regra <see cref="ProgressRule"/>, sempre com as contagens que o explicam.</summary>
public sealed record Progress(CategoryCounts Counts, double? Percent, string Rule);

/// <summary>
/// Regra única de progresso (ADR-014, D7): tarefas concluídas ÷ (tarefas − tarefas canceladas), contando só as
/// não arquivadas. Concluída é a tarefa num estado da categoria Done; as canceladas saem da conta porque não serão
/// feitas; Blocked, InProgress e Todo contam como não concluídas. Sem tarefas na conta, o progresso é <c>null</c>
/// ("sem dados"), nunca 0% nem 100%. Não há pesos: o modelo não tem estimativas, e usar a prioridade seria arbitrário.
/// </summary>
public static class ProgressRule
{
    public const string Name = "doneOverNotCanceled";

    public static Progress Calculate(CategoryCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        var considered = counts.Total - counts.Canceled;
        double? percent = considered == 0 ? null : Math.Round(100.0 * counts.Done / considered, 1, MidpointRounding.AwayFromZero);
        return new Progress(counts, percent, Name);
    }
}
