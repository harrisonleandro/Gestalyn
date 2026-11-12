using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectManager.Domain.Projects;

namespace ProjectManager.Infrastructure.Persistence.Configurations;

internal sealed class ProjectPhaseConfiguration : IEntityTypeConfiguration<ProjectPhase>
{
    public void Configure(EntityTypeBuilder<ProjectPhase> builder)
    {
        builder.ToTable("project_phases", table =>
        {
            table.HasCheckConstraint("ck_project_phases_position_positive", "position > 0");
            table.HasCheckConstraint("ck_project_phases_end_date_after_start", "end_date IS NULL OR start_date IS NULL OR end_date >= start_date");
        });
        builder.ConfigureEntityBase();

        builder.Property(phase => phase.Name)
            .HasMaxLength(ProjectPhase.NameMaxLength)
            .IsRequired();

        builder.Property(phase => phase.Description).HasMaxLength(ProjectPhase.DescriptionMaxLength);

        builder.HasIndex(phase => new { phase.ProjectId, phase.Position })
            .IsUnique();

        // Alvo das FKs compostas de marcos e tarefas: a fase precisa ser do mesmo projeto.
        builder.HasAlternateKey(phase => new { phase.ProjectId, phase.Id });
    }
}

internal sealed class MilestoneConfiguration : IEntityTypeConfiguration<Milestone>
{
    public void Configure(EntityTypeBuilder<Milestone> builder)
    {
        builder.ToTable("milestones");
        builder.ConfigureEntityBase();

        builder.Property(milestone => milestone.Name).HasMaxLength(Milestone.NameMaxLength).IsRequired();
        builder.Property(milestone => milestone.Description).HasMaxLength(Milestone.DescriptionMaxLength);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(milestone => new { milestone.OrganizationId, milestone.ProjectId })
            .HasPrincipalKey(project => new { project.OrganizationId, project.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Fase opcional, do mesmo projeto (FK composta; com phase_id nulo, não se aplica).
        builder.HasOne<ProjectPhase>()
            .WithMany()
            .HasForeignKey(milestone => new { milestone.ProjectId, milestone.PhaseId })
            .HasPrincipalKey(phase => new { phase.ProjectId, phase.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Alvo da FK composta das tarefas: o marco precisa ser do mesmo projeto.
        builder.HasAlternateKey(milestone => new { milestone.ProjectId, milestone.Id });
    }
}
