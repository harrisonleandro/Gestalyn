using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Tasks;

/// <summary>Comentário numa tarefa. Só o autor edita (ADR-011).</summary>
public sealed class TaskComment : Entity, IOrganizationOwned, IAuditable
{
    public const int BodyMaxLength = 10000;

    public TaskComment(ProjectTask task, Guid authorUserId, string body)
    {
        ArgumentNullException.ThrowIfNull(task);
        task.EnsureNotArchived();

        OrganizationId = task.OrganizationId;
        ProjectId = task.ProjectId;
        TaskId = task.Id;
        AuthorUserId = Guard.RequiredId(authorUserId, nameof(authorUserId));
        Body = Guard.RequiredText(body, BodyMaxLength, nameof(body));
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private TaskComment(string body) => Body = body;

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid TaskId { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public string Body { get; private set; }

    /// <summary>Quando o texto foi alterado pela última vez, ou <c>null</c> se nunca foi.</summary>
    public DateTimeOffset? EditedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsAuthor(Guid userId) => AuthorUserId == userId;

    public void Edit(string body, DateTimeOffset now)
    {
        var newBody = Guard.RequiredText(body, BodyMaxLength, nameof(body));
        if (newBody == Body)
        {
            return;
        }

        Body = newBody;
        EditedAt = Timestamps.TruncateToMicroseconds(now);
    }
}
