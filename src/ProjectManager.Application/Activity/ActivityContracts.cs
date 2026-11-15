using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ProjectManager.Application.Common;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Workflows;

namespace ProjectManager.Application.Activity;

/// <summary>De onde vem o item da lista de atividades: um evento do sistema ou um registro manual.</summary>
public enum ActivitySource
{
    Event,
    Log,
}

/// <summary>
/// Item da lista de atividades. Eventos trazem <c>action</c>, <c>entityType</c>, <c>entityId</c> e <c>changes</c>;
/// registros manuais trazem <c>kind</c>, <c>details</c> e <c>recordedAt</c>.
/// </summary>
public sealed record ActivityItem(
    Guid Id,
    ActivitySource Source,
    Guid? ProjectId,
    Guid? TaskId,
    Guid? ActorId,
    string? ActorName,
    ActorKind ActorKind,
    ActivityOrigin Origin,
    string? RequestId,
    string? Action,
    string? EntityType,
    Guid? EntityId,
    ActivityLogKind? Kind,
    string Summary,
    string? Details,
    JsonElement? Changes,
    DateTimeOffset OccurredAt,
    DateTimeOffset? RecordedAt);

/// <summary>Filtros da lista de atividades, mais novas primeiro.</summary>
public sealed class ActivityListQuery : PageQuery
{
    public Guid? ActorId { get; init; }

    public ActorKind? ActorKind { get; init; }

    public ActivityOrigin? Origin { get; init; }

    /// <summary>Ação do catálogo; só eventos têm ação.</summary>
    [StringLength(ActivityEvent.ActionMaxLength)]
    public string? Action { get; init; }

    /// <summary>Tipo de entidade; só eventos têm tipo de entidade.</summary>
    [StringLength(ActivityEvent.EntityTypeMaxLength)]
    public string? EntityType { get; init; }

    public Guid? TaskId { get; init; }

    /// <summary>Na lista da organização, só as atividades de um projeto.</summary>
    public Guid? ProjectId { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public ActivitySource? Source { get; init; }
}

public sealed class CreateActivityLogRequest
{
    [Required(ErrorMessage = "Informe o tipo do registro.")]
    public ActivityLogKind? Kind { get; init; }

    [NotWhiteSpace(ErrorMessage = "Informe o resumo da atividade.")]
    [StringLength(ActivityLog.SummaryMaxLength)]
    public string Summary { get; init; } = string.Empty;

    [StringLength(ActivityLog.DetailsMaxLength)]
    public string? Details { get; init; }

    public Guid? TaskId { get; init; }

    /// <summary>Quando a atividade aconteceu; sem o campo, agora. Não pode estar no futuro.</summary>
    public DateTimeOffset? OccurredAt { get; init; }
}

/// <summary>Leitura e registro manual de atividades. O acesso ao projeto já foi verificado.</summary>
public interface IActivityService
{
    Task<PagedResult<ActivityItem>> ListProjectAsync(Guid projectId, ActivityListQuery query, CancellationToken cancellationToken);

    /// <summary>Atividades da organização ativa: as da organização e as de todos os projetos.</summary>
    Task<PagedResult<ActivityItem>> ListOrganizationAsync(ActivityListQuery query, CancellationToken cancellationToken);

    Task<Result<ActivityItem>> CreateLogAsync(Guid projectId, CreateActivityLogRequest request, CancellationToken cancellationToken);
}
