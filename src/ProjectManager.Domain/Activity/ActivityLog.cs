using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Workflows;

namespace ProjectManager.Domain.Activity;

/// <summary>Tipo de um registro manual de atividade.</summary>
public enum ActivityLogKind
{
    Note,
    Meeting,
    Other,
}

/// <summary>
/// Registro manual de uma atividade do projeto, como uma reunião ou uma nota de acompanhamento (ADR-014).
/// Imutável: uma correção é um novo registro. Fica separado dos eventos para distinguir o que alguém declarou
/// do que o sistema registrou.
/// </summary>
public sealed class ActivityLog : Entity, IOrganizationOwned
{
    public const int SummaryMaxLength = 500;
    public const int DetailsMaxLength = 10_000;
    public const string FutureDateMessage = "A data da atividade não pode estar no futuro.";

    public ActivityLog(
        Guid organizationId,
        Guid projectId,
        Guid? taskId,
        ActivityActor author,
        ActivityLogKind kind,
        string summary,
        string? details,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(author);

        if (occurredAt > recordedAt)
        {
            throw new ArgumentException(FutureDateMessage, nameof(occurredAt));
        }

        OrganizationId = Guard.RequiredId(organizationId, nameof(organizationId));
        ProjectId = Guard.RequiredId(projectId, nameof(projectId));
        TaskId = taskId;
        AuthorId = author.ActorId;
        ActorKind = author.Kind;
        Origin = author.Origin;
        RequestId = author.BoundedRequestId;
        Kind = kind;
        Summary = Guard.RequiredText(summary, SummaryMaxLength, nameof(summary));
        Details = Guard.OptionalText(details, DetailsMaxLength, nameof(details));
        OccurredAt = Timestamps.TruncateToMicroseconds(occurredAt);
        RecordedAt = Timestamps.TruncateToMicroseconds(recordedAt);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private ActivityLog(string summary) => Summary = summary;

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid? TaskId { get; private set; }

    public Guid? AuthorId { get; private set; }

    public ActorKind ActorKind { get; private set; }

    public ActivityOrigin Origin { get; private set; }

    public string? RequestId { get; private set; }

    public ActivityLogKind Kind { get; private set; }

    public string Summary { get; private set; }

    public string? Details { get; private set; }

    /// <summary>Quando a atividade aconteceu, segundo quem a registrou.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Quando o registro foi gravado.</summary>
    public DateTimeOffset RecordedAt { get; private set; }
}
