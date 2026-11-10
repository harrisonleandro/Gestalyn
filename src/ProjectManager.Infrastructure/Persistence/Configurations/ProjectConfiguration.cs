using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectManager.Domain.Organizations;
using ProjectManager.Domain.Projects;

namespace ProjectManager.Infrastructure.Persistence.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.ConfigureEntityBase();

        builder.Property(project => project.Name)
            .HasMaxLength(Project.NameMaxLength)
            .IsRequired();

        builder.Property(project => project.Description)
            .HasMaxLength(Project.DescriptionMaxLength);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(project => project.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chave (organization_id, id): alvo das FKs compostas das entidades filhas. Garante no banco
        // que um filho só pode apontar para um projeto da mesma organização.
        builder.HasAlternateKey(project => new { project.OrganizationId, project.Id });

        builder.HasMany(project => project.Phases)
            .WithOne()
            .HasForeignKey(phase => new { phase.OrganizationId, phase.ProjectId })
            .HasPrincipalKey(project => new { project.OrganizationId, project.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(project => project.Phases)
            .HasField("_phases")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
