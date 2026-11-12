using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Tasks;

/// <summary>
/// Responsável por uma tarefa. O usuário precisa estar na equipe do projeto; ao sair dela, deixa de ser
/// responsável (FK composta com cascata, ADR-011).
/// </summary>
public sealed class TaskAssignment : IOrganizationOwned
{
    internal TaskAssignment(ProjectTask task, Guid userId, DateTimeOffset assignedAt)
    {
        OrganizationId = task.OrganizationId;
        ProjectId = task.ProjectId;
        TaskId = task.Id;
        UserId = userId;
        AssignedAt = Timestamps.TruncateToMicroseconds(assignedAt);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private TaskAssignment()
    {
    }

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid TaskId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }
}
