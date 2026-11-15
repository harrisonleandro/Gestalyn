using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Organizations;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Tasks;

namespace ProjectManager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Eventos de atividade (ADR-014). Sem FK para o autor, que pode ser um agente, e para a entidade, que pode ser
/// excluída: o evento guarda o ID e o resumo legível. Um gatilho no banco recusa alterações e exclusões.
/// </summary>
internal sealed class ActivityEventConfiguration : IEntityTypeConfiguration<ActivityEvent>
{
    public void Configure(EntityTypeBuilder<ActivityEvent> builder)
    {
        builder.ToTable("activity_events");
        builder.ConfigureEntityBase();

        builder.Property(activity => activity.ActorKind).HasConversion<string>().HasMaxLength(16);
        builder.Property(activity => activity.Origin).HasConversion<string>().HasMaxLength(16);
        builder.Property(activity => activity.RequestId).HasMaxLength(ActivityActor.RequestIdMaxLength);
        builder.Property(activity => activity.Action).HasMaxLength(ActivityEvent.ActionMaxLength).IsRequired();
        builder.Property(activity => activity.EntityType).HasMaxLength(ActivityEvent.EntityTypeMaxLength).IsRequired();
        builder.Property(activity => activity.Summary).HasMaxLength(ActivityEvent.SummaryMaxLength).IsRequired();
        builder.Property(activity => activity.Changes).HasColumnType("jsonb");

        builder.HasIndex(activity => new { activity.OrganizationId, activity.OccurredAt });
        builder.HasIndex(activity => new { activity.ProjectId, activity.OccurredAt });
        builder.HasIndex(activity => new { activity.TaskId, activity.OccurredAt });

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(activity => activity.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(activity => new { activity.OrganizationId, activity.ProjectId })
            .HasPrincipalKey(project => new { project.OrganizationId, project.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ProjectTask>()
            .WithMany()
            .HasForeignKey(activity => new { activity.OrganizationId, activity.ProjectId, activity.TaskId })
            .HasPrincipalKey(task => new { task.OrganizationId, task.ProjectId, task.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.ToTable("activity_logs");
        builder.ConfigureEntityBase();

        builder.Property(log => log.ActorKind).HasConversion<string>().HasMaxLength(16);
        builder.Property(log => log.Origin).HasConversion<string>().HasMaxLength(16);
        builder.Property(log => log.RequestId).HasMaxLength(ActivityActor.RequestIdMaxLength);
        builder.Property(log => log.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(log => log.Summary).HasMaxLength(ActivityLog.SummaryMaxLength).IsRequired();
        builder.Property(log => log.Details).HasMaxLength(ActivityLog.DetailsMaxLength);

        builder.HasIndex(log => new { log.ProjectId, log.OccurredAt });
        builder.HasIndex(log => new { log.TaskId, log.OccurredAt });

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(log => new { log.OrganizationId, log.ProjectId })
            .HasPrincipalKey(project => new { project.OrganizationId, project.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ProjectTask>()
            .WithMany()
            .HasForeignKey(log => new { log.OrganizationId, log.ProjectId, log.TaskId })
            .HasPrincipalKey(task => new { task.OrganizationId, task.ProjectId, task.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
