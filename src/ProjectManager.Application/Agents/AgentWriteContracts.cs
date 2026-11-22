using System.ComponentModel.DataAnnotations;
using ProjectManager.Application.Activity;
using ProjectManager.Application.Common;
using ProjectManager.Application.Tasks;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Tasks;

namespace ProjectManager.Application.Agents;

/// <summary>Criação de tarefa por agente (ADR-016, D7): o projeto vem no corpo; agentes não definem responsáveis.</summary>
public sealed class CreateAgentTaskRequest
{
    [Required(ErrorMessage = "Informe o projeto.")]
    public Guid? ProjectId { get; init; }

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

    public CreateTaskRequest ToTaskRequest() => new()
    {
        Title = Title,
        Description = Description,
        Priority = Priority,
        DueDate = DueDate,
        PhaseId = PhaseId,
        MilestoneId = MilestoneId,
        Labels = Labels,
    };
}

/// <summary>Registro manual de atividade por agente: o projeto vem no corpo; o autor é o agente da credencial.</summary>
public sealed class CreateAgentActivityRequest
{
    [Required(ErrorMessage = "Informe o projeto.")]
    public Guid? ProjectId { get; init; }

    [Required(ErrorMessage = "Informe o tipo do registro.")]
    public ActivityLogKind? Kind { get; init; }

    [NotWhiteSpace(ErrorMessage = "Informe o resumo da atividade.")]
    [StringLength(ActivityLog.SummaryMaxLength)]
    public string Summary { get; init; } = string.Empty;

    [StringLength(ActivityLog.DetailsMaxLength)]
    public string? Details { get; init; }

    public Guid? TaskId { get; init; }

    public DateTimeOffset? OccurredAt { get; init; }

    public CreateActivityLogRequest ToLogRequest() => new()
    {
        Kind = Kind,
        Summary = Summary,
        Details = Details,
        TaskId = TaskId,
        OccurredAt = OccurredAt,
    };
}
