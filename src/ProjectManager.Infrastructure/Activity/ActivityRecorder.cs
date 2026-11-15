using System.Text.Json;
using ProjectManager.Application.Abstractions.Activity;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Domain.Activity;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Activity;

/// <summary>
/// Registra eventos de atividade (ADR-014, D2). O evento entra no mesmo <c>SaveChanges</c> da alteração: ou os
/// dois são gravados, ou nenhum. Por isso o registro é feito depois de validar a operação e antes de gravar.
/// </summary>
internal sealed class ActivityRecorder(
    ProjectManagerDbContext dbContext,
    IActivityContext activityContext,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
{
    /// <summary>Campos técnicos, que não fazem parte do contrato nem interessam à auditoria.</summary>
    private static readonly HashSet<string> TechnicalProperties = new(StringComparer.Ordinal)
    {
        "CreatedAt", "UpdatedAt", "NextTaskNumber", "NormalizedName", "ArchivedByUserId", "EditedAt",
    };

    /// <summary>Textos longos: registra-se que mudaram, sem copiar o conteúdo.</summary>
    private static readonly HashSet<string> LongTextProperties = new(StringComparer.Ordinal)
    {
        "Description", "Body", "Details", "Context", "Outcome", "Alternatives", "Consequences",
    };

    public ActivityEvent Record(
        string action,
        string entityType,
        Guid entityId,
        string summary,
        Guid? projectId = null,
        Guid? taskId = null,
        ActivityChanges? changes = null)
    {
        var organizationId = tenantContext.OrganizationId
            ?? throw new InvalidOperationException("Um evento de atividade exige a organização ativa.");

        var activity = new ActivityEvent(organizationId, activityContext.Actor, action, entityType, entityId, summary, timeProvider.GetUtcNow());
        if (projectId is { } project)
        {
            activity.InProject(project, taskId);
        }

        activity.WithChanges(changes?.ToJson());
        dbContext.ActivityEvents.Add(activity);
        return activity;
    }

    /// <summary>
    /// Alterações das propriedades simples da entidade rastreada, comparando os valores originais com os atuais.
    /// Chaves, campos técnicos e propriedades sombra (como o <c>xmin</c>) ficam de fora.
    /// </summary>
    public ActivityChanges ChangesOf(object entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        dbContext.ChangeTracker.DetectChanges();
        var changes = new ActivityChanges();
        foreach (var property in dbContext.Entry(entity).Properties)
        {
            var metadata = property.Metadata;
            if (!property.IsModified || metadata.IsShadowProperty() || metadata.IsKey() || TechnicalProperties.Contains(metadata.Name))
            {
                continue;
            }

            if (Equals(property.OriginalValue, property.CurrentValue))
            {
                continue;
            }

            var field = JsonNamingPolicy.CamelCase.ConvertName(metadata.Name);
            if (LongTextProperties.Contains(metadata.Name))
            {
                changes.ContentChanged(field);
            }
            else
            {
                changes.Set(field, property.OriginalValue, property.CurrentValue);
            }
        }

        return changes;
    }
}
