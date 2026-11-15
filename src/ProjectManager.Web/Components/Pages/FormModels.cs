using System.ComponentModel.DataAnnotations;

namespace ProjectManager.Web.Components.Pages;

/// <summary>
/// Campos genéricos de formulários de uma linha (trocar papel, mover tarefa, editar comentário). Os campos HTML usam o
/// prefixo <c>F.</c>; a validação de verdade é da API, que devolve a mensagem exibida.
/// </summary>
public sealed class RowForm
{
    public const string Prefix = "F";

    public string? Value { get; set; }

    public string? Text { get; set; }

    public bool Flag { get; set; }
}

public sealed class NameForm
{
    [Required(ErrorMessage = "Informe o nome.")]
    [StringLength(200, ErrorMessage = "O nome deve ter no máximo {1} caracteres.")]
    public string Name { get; set; } = string.Empty;
}

public sealed class ProjectForm
{
    [Required(ErrorMessage = "Informe o nome do projeto.")]
    [StringLength(200, ErrorMessage = "O nome deve ter no máximo {1} caracteres.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(4000, ErrorMessage = "A descrição deve ter no máximo {1} caracteres.")]
    public string? Description { get; set; }

    public Api.Priority Priority { get; set; } = Api.Priority.Medium;

    public DateOnly? StartDate { get; set; }

    public DateOnly? DueDate { get; set; }

    /// <summary>Etiquetas separadas por vírgula.</summary>
    public string? Tags { get; set; }

    public IReadOnlyList<string> TagList => (Tags ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();
}

public sealed class TaskForm
{
    [Required(ErrorMessage = "Informe o título da tarefa.")]
    [StringLength(300, ErrorMessage = "O título deve ter no máximo {1} caracteres.")]
    public string Title { get; set; } = string.Empty;

    [StringLength(20_000, ErrorMessage = "A descrição deve ter no máximo {1} caracteres.")]
    public string? Description { get; set; }

    public Api.Priority Priority { get; set; } = Api.Priority.Medium;

    public DateOnly? DueDate { get; set; }

    public Guid? PhaseId { get; set; }

    public Guid? MilestoneId { get; set; }

    /// <summary>Etiquetas separadas por vírgula.</summary>
    public string? Labels { get; set; }

    public IReadOnlyList<string> LabelList => (Labels ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();
}

public sealed class InvitationForm
{
    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Escolha o papel.")]
    public Guid? RoleId { get; set; }
}

public sealed class MarkdownForm
{
    [Required(ErrorMessage = "Informe o título.")]
    [StringLength(200, ErrorMessage = "O título deve ter no máximo {1} caracteres.")]
    public string Title { get; set; } = string.Empty;

    [StringLength(200_000, ErrorMessage = "O texto deve ter no máximo {1} caracteres.")]
    public string? Body { get; set; }

    public int ExpectedVersion { get; set; }
}

public sealed class DecisionForm
{
    [Required(ErrorMessage = "Informe o título.")]
    [StringLength(200, ErrorMessage = "O título deve ter no máximo {1} caracteres.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Descreva o contexto.")]
    [StringLength(20_000)]
    public string Context { get; set; } = string.Empty;

    [Required(ErrorMessage = "Descreva o que foi decidido.")]
    [StringLength(20_000)]
    public string Outcome { get; set; } = string.Empty;

    [StringLength(20_000)]
    public string? Alternatives { get; set; }

    [StringLength(20_000)]
    public string? Consequences { get; set; }
}
