using System.ComponentModel.DataAnnotations;
using ProjectManager.Application.Common;
using ProjectManager.Domain.Workflows;

namespace ProjectManager.Application.Workflows;

public sealed record WorkflowStateDetails(
    Guid Id,
    string Name,
    StateCategory Category,
    bool IsFinal,
    bool IsInitial,
    int Position,
    int TaskCount);

public sealed record WorkflowTransitionDetails(Guid Id, Guid FromStateId, Guid ToStateId, bool RequiresApproval);

/// <summary>Workflow de um projeto: estados em ordem, transições e configurações.</summary>
public sealed record WorkflowDetails(
    Guid ProjectId,
    bool RequireIndependentApproval,
    IReadOnlyList<WorkflowStateDetails> States,
    IReadOnlyList<WorkflowTransitionDetails> Transitions);

/// <summary>Configurações do workflow. Só os campos enviados mudam.</summary>
public sealed class UpdateWorkflowRequest
{
    public Guid? InitialStateId { get; init; }

    /// <summary>Quem é responsável pela tarefa não executa transições que exigem aprovação.</summary>
    public bool? RequireIndependentApproval { get; init; }
}

public sealed class CreateStateRequest
{
    [Required(ErrorMessage = "Informe o nome do estado.")]
    [StringLength(WorkflowState.NameMaxLength, ErrorMessage = "O nome deve ter no máximo {1} caracteres.")]
    public required string Name { get; init; }

    [Required(ErrorMessage = "Informe a categoria do estado.")]
    public required StateCategory? Category { get; init; }
}

public sealed class UpdateStateRequest
{
    [NotWhiteSpace(ErrorMessage = "Informe o nome do estado.")]
    [StringLength(WorkflowState.NameMaxLength, MinimumLength = 1, ErrorMessage = "O nome deve ter de {2} a {1} caracteres.")]
    public string? Name { get; init; }

    public StateCategory? Category { get; init; }
}

public sealed class MoveStateRequest
{
    [Range(1, WorkflowDefinition.MaxStates, ErrorMessage = "A posição deve ficar entre {1} e {2}.")]
    public required int Position { get; init; }
}

public sealed class CreateTransitionRequest
{
    [Required]
    public required Guid FromStateId { get; init; }

    [Required]
    public required Guid ToStateId { get; init; }

    public bool RequiresApproval { get; init; }
}
