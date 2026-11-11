using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectManager.Domain.Projects;

namespace ProjectManager.Infrastructure.Persistence.Configurations;

internal sealed class ProjectPhaseConfiguration : IEntityTypeConfiguration<ProjectPhase>
{
    public void Configure(EntityTypeBuilder<ProjectPhase> builder)
    {
        builder.ToTable("project_phases", table =>
            table.HasCheckConstraint("ck_project_phases_position_positive", "position > 0"));
        builder.ConfigureEntityBase();

        builder.Property(phase => phase.Name)
            .HasMaxLength(ProjectPhase.NameMaxLength)
            .IsRequired();

        builder.HasIndex(phase => new { phase.ProjectId, phase.Position })
            .IsUnique();
    }
}
