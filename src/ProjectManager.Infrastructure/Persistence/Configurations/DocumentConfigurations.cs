using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectManager.Domain.Documents;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Tasks;
using ProjectManager.Infrastructure.Identity;

namespace ProjectManager.Infrastructure.Persistence.Configurations;

/// <summary>Documentos e versões (ADR-014, D5). As versões ficam presas ao documento e ao projeto por FKs compostas.</summary>
internal sealed class ProjectDocumentConfiguration : IEntityTypeConfiguration<ProjectDocument>
{
    public void Configure(EntityTypeBuilder<ProjectDocument> builder)
    {
        builder.ToTable("project_documents", table => table.HasCheckConstraint("ck_project_documents_version_positive", "current_version > 0"));
        builder.ConfigureEntityBase();
        builder.Ignore(document => document.FirstVersion);

        builder.Property(document => document.Title).HasMaxLength(ProjectDocument.TitleMaxLength).IsRequired();
        builder.Property(document => document.Body).HasMaxLength(ProjectDocument.BodyMaxLength).IsRequired();

        builder.HasAlternateKey(document => new { document.OrganizationId, document.ProjectId, document.Id });
        builder.HasIndex(document => new { document.ProjectId, document.Title });

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(document => new { document.OrganizationId, document.ProjectId })
            .HasPrincipalKey(project => new { project.OrganizationId, project.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(document => document.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProjectDocumentVersionConfiguration : IEntityTypeConfiguration<ProjectDocumentVersion>
{
    public void Configure(EntityTypeBuilder<ProjectDocumentVersion> builder)
    {
        builder.ToTable("project_document_versions", table => table.HasCheckConstraint("ck_project_document_versions_number_positive", "number > 0"));
        builder.ConfigureEntityBase();

        builder.Property(version => version.Title).HasMaxLength(ProjectDocument.TitleMaxLength).IsRequired();
        builder.Property(version => version.Body).HasMaxLength(ProjectDocument.BodyMaxLength).IsRequired();

        // Duas edições simultâneas a partir da mesma versão não criam duas versões com o mesmo número.
        builder.HasIndex(version => new { version.DocumentId, version.Number }).IsUnique();

        builder.HasOne<ProjectDocument>()
            .WithMany()
            .HasForeignKey(version => new { version.OrganizationId, version.ProjectId, version.DocumentId })
            .HasPrincipalKey(document => new { document.OrganizationId, document.ProjectId, document.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(version => version.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Decisões (ADR-014, D6). A substituta e a tarefa relacionada são do mesmo projeto, garantido pelo banco.</summary>
internal sealed class DecisionConfiguration : IEntityTypeConfiguration<Decision>
{
    public void Configure(EntityTypeBuilder<Decision> builder)
    {
        builder.ToTable("decisions", table => table.HasCheckConstraint("ck_decisions_not_self_superseded", "superseded_by_id IS NULL OR superseded_by_id <> id"));
        builder.ConfigureEntityBase();

        builder.Property(decision => decision.Title).HasMaxLength(Decision.TitleMaxLength).IsRequired();
        builder.Property(decision => decision.Context).HasMaxLength(Decision.TextMaxLength).IsRequired();
        builder.Property(decision => decision.Outcome).HasMaxLength(Decision.TextMaxLength).IsRequired();
        builder.Property(decision => decision.Alternatives).HasMaxLength(Decision.TextMaxLength);
        builder.Property(decision => decision.Consequences).HasMaxLength(Decision.TextMaxLength);
        builder.Property(decision => decision.Status).HasConversion<string>().HasMaxLength(16);

        builder.HasAlternateKey(decision => new { decision.OrganizationId, decision.ProjectId, decision.Id });
        builder.HasIndex(decision => new { decision.ProjectId, decision.Status });

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(decision => new { decision.OrganizationId, decision.ProjectId })
            .HasPrincipalKey(project => new { project.OrganizationId, project.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ProjectTask>()
            .WithMany()
            .HasForeignKey(decision => new { decision.OrganizationId, decision.ProjectId, decision.TaskId })
            .HasPrincipalKey(task => new { task.OrganizationId, task.ProjectId, task.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Decision>()
            .WithMany()
            .HasForeignKey(decision => new { decision.OrganizationId, decision.ProjectId, decision.SupersededById })
            .HasPrincipalKey(decision => new { decision.OrganizationId, decision.ProjectId, decision.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(decision => decision.ProposedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
