using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ProjectManager.Application.Common;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Tasks;
using ProjectManager.Domain.Workflows;

namespace ProjectManager.Application.Tasks;

public sealed class CreateTaskRequest
{
    [Required(ErrorMessage = "Informe o título da tarefa.")]
    [StringLength(ProjectTask.TitleMaxLength, ErrorMessage = "O título deve ter no máximo {1} caracteres.")]
    public required string Title { get; init; }

    [StringLength(ProjectTask.DescriptionMaxLength, ErrorMessage = "A descrição deve ter no máximo {1} caracteres.")]
    public string? Description { get; init; }

    public Priority? Priority { get; init; }

    public DateOnly? DueDate { get; init; }

    public Guid? PhaseId { get; init; }

    public Guid? MilestoneId { get; init; }

    public IReadOnlyList<string>? Labels { get; init; }

    [MaxLength(ProjectTask.MaxAssignees, ErrorMessage = "Uma tarefa pode ter no máximo {1} responsáveis.")]
    public IReadOnlyList<Guid>? AssigneeIds { get; init; }
}

/// <summary>
/// Alteração parcial: só os campos enviados mudam. Em descrição, prazo, fase e marco, <c>null</c> limpa o
/// valor e a ausência o mantém. <c>labels</c> substitui o conjunto inteiro (<c>[]</c> limpa).
/// </summary>
public sealed class UpdateTaskRequest
{
    private readonly string? _description;
    private readonly DateOnly? _dueDate;
    private readonly Guid? _phaseId;
    private readonly Guid? _milestoneId;

    [NotWhiteSpace(ErrorMessage = "Informe o título da tarefa.")]
    [StringLength(ProjectTask.TitleMaxLength, MinimumLength = 1, ErrorMessage = "O título deve ter de {2} a {1} caracteres.")]
    public string? Title { get; init; }

    [StringLength(ProjectTask.DescriptionMaxLength, ErrorMessage = "A descrição deve ter no máximo {1} caracteres.")]
    public string? Description
    {
        get => _description;
        init
        {
            _description = value;
            HasDescription = true;
        }
    }

    public Priority? Priority { get; init; }

    public DateOnly? DueDate
    {
        get => _dueDate;
        init
        {
            _dueDate = value;
            HasDueDate = true;
        }
    }

    public Guid? PhaseId
    {
        get => _phaseId;
        init
        {
            _phaseId = value;
            HasPhaseId = true;
        }
    }

    public Guid? MilestoneId
    {
        get => _milestoneId;
        init
        {
            _milestoneId = value;
            HasMilestoneId = true;
        }
    }

    public IReadOnlyList<string>? Labels { get; init; }

    [JsonIgnore]
    public bool HasDescription { get; private init; }

    [JsonIgnore]
    public bool HasDueDate { get; private init; }

    [JsonIgnore]
    public bool HasPhaseId { get; private init; }

    [JsonIgnore]
    public bool HasMilestoneId { get; private init; }

    /// <summary>
    /// Versão em que o cliente viu a tarefa. Se ela tiver mudado, a edição é recusada (409) e nada é sobrescrito.
    /// Opcional para pessoas; obrigatória nas rotas de agentes (ADR-016).
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "A versão esperada deve ser positiva.")]
    public int? ExpectedVersion { get; init; }
}

public sealed class AddAssignmentRequest
{
    [Required]
    public required Guid UserId { get; init; }
}

public enum TaskSortField
{
    Number,
    DueDate,
    Priority,
    UpdatedAt,

    /// <summary>Pela posição do estado no workflow.</summary>
    State,
}

public enum SortDirection
{
    Asc,
    Desc,
}

public sealed class TaskListQuery : PageQuery
{
    public const int SearchMaxLength = 200;

    public Guid? PhaseId { get; init; }

    public Guid? MilestoneId { get; init; }

    public Guid? AssigneeId { get; init; }

    /// <summary>Só as tarefas em que o usuário autenticado é responsável.</summary>
    public bool AssignedToMe { get; init; }

    [StringLength(Domain.Common.Labels.NameMaxLength)]
    public string? Label { get; init; }

    public Priority? Priority { get; init; }

    public DateOnly? DueBefore { get; init; }

    public DateOnly? DueAfter { get; init; }

    /// <summary>Prazo anterior a hoje (UTC), fora dos estados finais.</summary>
    public bool Overdue { get; init; }

    public Guid? StateId { get; init; }

    public StateCategory? Category { get; init; }

    /// <summary>Só as tarefas fora dos estados finais.</summary>
    public bool Open { get; init; }

    /// <summary>Texto no título, sem diferenciar maiúsculas e minúsculas.</summary>
    [StringLength(SearchMaxLength)]
    public string? Search { get; init; }

    public bool IncludeArchived { get; init; }

    public TaskSortField SortBy { get; init; } = TaskSortField.Number;

    public SortDirection SortDirection { get; init; } = SortDirection.Asc;
}

/// <summary>Estado atual da tarefa no workflow do projeto.</summary>
public sealed record TaskState(Guid Id, string Name, StateCategory Category, bool IsFinal);

/// <summary>Transição que o usuário pode executar agora na tarefa.</summary>
public sealed record AvailableTransition(Guid TransitionId, Guid ToStateId, string ToStateName, StateCategory ToCategory, bool RequiresApproval);

/// <summary>Tarefa na listagem.</summary>
public sealed record TaskSummary(
    Guid Id,
    int Number,
    string Title,
    TaskState State,
    Priority Priority,
    DateOnly? DueDate,
    Guid? PhaseId,
    Guid? MilestoneId,
    IReadOnlyList<string> Labels,
    IReadOnlyList<Guid> AssigneeIds,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset UpdatedAt);

public sealed record TaskAssignee(Guid UserId, string DisplayName, DateTimeOffset AssignedAt);

/// <summary>Outra tarefa do projeto citada numa dependência.</summary>
public sealed record TaskReference(Guid Id, int Number, string Title, DateTimeOffset? ArchivedAt);

/// <summary>Tarefa completa, com as permissões efetivas do usuário no projeto.</summary>
public sealed record TaskDetails(
    Guid Id,
    Guid ProjectId,
    int Number,
    string Title,
    string? Description,
    Priority Priority,
    DateOnly? DueDate,
    Guid? PhaseId,
    Guid? MilestoneId,
    IReadOnlyList<string> Labels,
    IReadOnlyList<TaskAssignee> Assignees,
    IReadOnlyList<TaskReference> DependsOn,
    IReadOnlyList<TaskReference> Blocks,
    TaskState State,
    IReadOnlyList<AvailableTransition> AvailableTransitions,
    Guid? CreatedByUserId,
    DateTimeOffset? ArchivedAt,
    Guid? ArchivedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> MyPermissions,
    ActorReference CreatedBy,
    int Version);

public sealed class CommentRequest
{
    [Required(ErrorMessage = "Escreva o comentário.")]
    [StringLength(TaskComment.BodyMaxLength, MinimumLength = 1, ErrorMessage = "O comentário deve ter de {2} a {1} caracteres.")]
    public required string Body { get; init; }
}

public sealed record CommentDetails(
    Guid Id,
    Guid AuthorUserId,
    string AuthorDisplayName,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt);

public sealed class AddDependencyRequest
{
    [Required]
    public required Guid DependsOnTaskId { get; init; }
}

public sealed class TransitionTaskRequest
{
    [Required(ErrorMessage = "Informe o estado de destino.")]
    public required Guid ToStateId { get; init; }

    /// <summary>Estado em que o cliente viu a tarefa. Se ela tiver mudado, a transição é recusada (409).</summary>
    public Guid? ExpectedStateId { get; init; }

    /// <summary>Versão em que o cliente viu a tarefa (opcional para pessoas; obrigatória para agentes).</summary>
    [Range(1, int.MaxValue, ErrorMessage = "A versão esperada deve ser positiva.")]
    public int? ExpectedVersion { get; init; }

    [StringLength(TaskStatusHistory.CommentMaxLength, ErrorMessage = "O comentário deve ter no máximo {1} caracteres.")]
    public string? Comment { get; init; }
}

/// <summary>Mudança de estado registrada. Os nomes são os da data da mudança.</summary>
public sealed record TaskHistoryEntry(
    Guid Id,
    Guid? FromStateId,
    string? FromStateName,
    Guid ToStateId,
    string ToStateName,
    Guid ChangedById,
    string? ChangedByName,
    ActorKind ActorKind,
    string? Comment,
    DateTimeOffset ChangedAt);
