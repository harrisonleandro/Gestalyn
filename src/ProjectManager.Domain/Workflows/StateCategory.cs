namespace ProjectManager.Domain.Workflows;

/// <summary>
/// Categoria fixa de um estado (ADR-013). Os estados são livres; a categoria é o que permite aplicar regras
/// e calcular progresso sem conhecer os nomes. Concluída e Cancelada são categorias finais.
/// </summary>
public enum StateCategory
{
    Todo,
    InProgress,
    Blocked,
    Done,
    Canceled,
}

/// <summary>Quem executou uma mudança de estado.</summary>
public enum ActorKind
{
    User,
    Agent,
}

public static class StateCategories
{
    public static bool IsFinal(this StateCategory category) => category is StateCategory.Done or StateCategory.Canceled;
}
