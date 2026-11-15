using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Workflows;

namespace ProjectManager.Domain.Activity;

/// <summary>
/// Registro automático e imutável de uma alteração, gerado pelo backend na mesma gravação da alteração (ADR-014).
/// As alterações campo a campo ficam em <see cref="Changes"/>, em JSON, sem o conteúdo dos textos longos.
/// </summary>
public sealed class ActivityEvent : Entity, IOrganizationOwned
{
    public const int ActionMaxLength = 64;
    public const int EntityTypeMaxLength = 40;
    public const int SummaryMaxLength = 500;

    public ActivityEvent(
        Guid organizationId,
        ActivityActor actor,
        string action,
        string entityType,
        Guid entityId,
        string summary,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        OrganizationId = Guard.RequiredId(organizationId, nameof(organizationId));
        ActorId = actor.ActorId;
        ActorKind = actor.Kind;
        Origin = actor.Origin;
        RequestId = actor.BoundedRequestId;
        Action = Guard.RequiredText(action, ActionMaxLength, nameof(action));
        EntityType = Guard.RequiredText(entityType, EntityTypeMaxLength, nameof(entityType));
        EntityId = Guard.RequiredId(entityId, nameof(entityId));

        // O resumo é montado pelo sistema a partir de nomes informados pelos usuários: é cortado, nunca recusado.
        var trimmed = summary.Trim();
        Summary = trimmed.Length <= SummaryMaxLength ? trimmed : trimmed[..SummaryMaxLength];
        OccurredAt = Timestamps.TruncateToMicroseconds(occurredAt);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private ActivityEvent(string action, string entityType, string summary)
    {
        Action = action;
        EntityType = entityType;
        Summary = summary;
    }

    public Guid OrganizationId { get; private set; }

    /// <summary><c>null</c> para eventos da organização (membros, convites, dados da organização).</summary>
    public Guid? ProjectId { get; private set; }

    public Guid? TaskId { get; private set; }

    /// <summary><c>null</c> só para operações do sistema.</summary>
    public Guid? ActorId { get; private set; }

    public ActorKind ActorKind { get; private set; }

    public ActivityOrigin Origin { get; private set; }

    public string? RequestId { get; private set; }

    /// <summary>Ação do catálogo <see cref="ActivityActions"/>.</summary>
    public string Action { get; private set; }

    public string EntityType { get; private set; }

    public Guid EntityId { get; private set; }

    public string Summary { get; private set; }

    /// <summary>Alterações campo a campo, em JSON (<c>{ "campo": { "from": …, "to": … } }</c>), ou <c>null</c>.</summary>
    public string? Changes { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public ActivityEvent InProject(Guid projectId, Guid? taskId = null)
    {
        ProjectId = Guard.RequiredId(projectId, nameof(projectId));
        TaskId = taskId;
        return this;
    }

    public ActivityEvent WithChanges(string? changes)
    {
        Changes = string.IsNullOrWhiteSpace(changes) ? null : changes;
        return this;
    }
}
