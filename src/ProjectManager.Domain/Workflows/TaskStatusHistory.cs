using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Tasks;

namespace ProjectManager.Domain.Workflows;

/// <summary>
/// Registro imutável de uma mudança de estado da tarefa (ADR-013). Guarda os nomes dos estados na data,
/// para continuar legível se um estado for renomeado ou excluído.
/// </summary>
public sealed class TaskStatusHistory : Entity, IOrganizationOwned
{
    public const int CommentMaxLength = 1000;

    public TaskStatusHistory(
        ProjectTask task,
        WorkflowState? from,
        WorkflowState to,
        Guid changedById,
        ActorKind actorKind,
        string? comment,
        DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(to);

        OrganizationId = task.OrganizationId;
        ProjectId = task.ProjectId;
        TaskId = task.Id;
        FromStateId = from?.Id;
        FromStateName = from?.Name;
        ToStateId = to.Id;
        ToStateName = to.Name;
        ChangedById = Guard.RequiredId(changedById, nameof(changedById));
        ActorKind = actorKind;
        Comment = Guard.OptionalText(comment, CommentMaxLength, nameof(comment));
        ChangedAt = Timestamps.TruncateToMicroseconds(changedAt);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private TaskStatusHistory(string toStateName) => ToStateName = toStateName;

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid TaskId { get; private set; }

    /// <summary><c>null</c> na entrada inicial da tarefa.</summary>
    public Guid? FromStateId { get; private set; }

    public string? FromStateName { get; private set; }

    public Guid ToStateId { get; private set; }

    public string ToStateName { get; private set; }

    /// <summary>Pessoa ou agente que fez a mudança (o tipo está em <see cref="ActorKind"/>).</summary>
    public Guid ChangedById { get; private set; }

    public ActorKind ActorKind { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }
}
