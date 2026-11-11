using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectManager.Domain.Organizations;
using ProjectManager.Domain.Projects;

namespace ProjectManager.Infrastructure.Persistence.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects", table =>
            table.HasCheckConstraint("ck_projects_due_date_after_start", "due_date IS NULL OR start_date IS NULL OR due_date >= start_date"));
        builder.ConfigureEntityBase();
        builder.Ignore(project => project.IsArchived);

        builder.Property(project => project.Name)
            .HasMaxLength(Project.NameMaxLength)
            .IsRequired();

        builder.Property(project => project.Description)
            .HasMaxLength(Project.DescriptionMaxLength);

        builder.Property(project => project.Priority)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(project => project.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chave (organization_id, id): alvo das FKs compostas das entidades filhas. Garante no banco
        // que um filho só pode apontar para um projeto da mesma organização.
        builder.HasAlternateKey(project => new { project.OrganizationId, project.Id });

        // Listagem por organização, ordenada por nome.
        builder.HasIndex(project => new { project.OrganizationId, project.Name });

        builder.HasMany(project => project.Phases)
            .WithOne()
            .HasForeignKey(phase => new { phase.OrganizationId, phase.ProjectId })
            .HasPrincipalKey(project => new { project.OrganizationId, project.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(project => project.Phases)
            .HasField("_phases")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(project => project.Tags)
            .WithOne()
            .HasForeignKey(tag => new { tag.OrganizationId, tag.ProjectId })
            .HasPrincipalKey(project => new { project.OrganizationId, project.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(project => project.Tags)
            .HasField("_tags")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(project => project.Repositories)
            .WithOne()
            .HasForeignKey(repository => new { repository.OrganizationId, repository.ProjectId })
            .HasPrincipalKey(project => new { project.OrganizationId, project.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(project => project.Repositories)
            .HasField("_repositories")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ProjectTagConfiguration : IEntityTypeConfiguration<ProjectTag>
{
    public void Configure(EntityTypeBuilder<ProjectTag> builder)
    {
        builder.ToTable("project_tags");
        builder.HasKey(tag => new { tag.ProjectId, tag.NormalizedName });

        builder.Property(tag => tag.Name).HasMaxLength(ProjectTag.NameMaxLength).IsRequired();
        builder.Property(tag => tag.NormalizedName).HasMaxLength(ProjectTag.NameMaxLength).IsRequired();

        // Filtro da listagem de projetos por etiqueta.
        builder.HasIndex(tag => new { tag.OrganizationId, tag.NormalizedName });
    }
}

internal sealed class ProjectRepositoryConfiguration : IEntityTypeConfiguration<ProjectRepository>
{
    public void Configure(EntityTypeBuilder<ProjectRepository> builder)
    {
        builder.ToTable("project_repositories");
        builder.ConfigureEntityBase();

        builder.Property(repository => repository.Url).HasMaxLength(ProjectRepository.UrlMaxLength).IsRequired();
        builder.Property(repository => repository.Name).HasMaxLength(ProjectRepository.NameMaxLength);
        builder.Property(repository => repository.Provider).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(repository => new { repository.ProjectId, repository.Url }).IsUnique();
    }
}

internal sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.ToTable("project_members");
        builder.ConfigureEntityBase();

        builder.Ignore(member => member.Permissions);
        builder.Property(member => member.Role).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(member => new { member.ProjectId, member.UserId }).IsUnique();

        // Visibilidade: projetos em que o usuário está na equipe.
        builder.HasIndex(member => new { member.OrganizationId, member.UserId });

        // No máximo um responsável por projeto.
        builder.HasIndex(member => member.ProjectId)
            .IsUnique()
            .HasFilter("is_responsible")
            .HasDatabaseName("ix_project_members_responsible");

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(member => new { member.OrganizationId, member.ProjectId })
            .HasPrincipalKey(project => new { project.OrganizationId, project.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Só membros da organização entram na equipe; quem sai da organização sai das equipes.
        builder.HasOne<OrganizationMember>()
            .WithMany()
            .HasForeignKey(member => new { member.OrganizationId, member.UserId })
            .HasPrincipalKey(organizationMember => new { organizationMember.OrganizationId, organizationMember.UserId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
