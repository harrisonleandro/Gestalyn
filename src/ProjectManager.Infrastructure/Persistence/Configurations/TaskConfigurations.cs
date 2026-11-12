using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Tasks;
using ProjectManager.Infrastructure.Identity;

namespace ProjectManager.Infrastructure.Persistence.Configurations;

internal sealed class ProjectTaskConfiguration : IEntityTypeConfiguration<ProjectTask>
{
    public void Configure(EntityTypeBuilder<ProjectTask> builder)
    {
        builder.ToTable("tasks", table => table.HasCheckConstraint("ck_tasks_number_positive", "number > 0"));
        builder.ConfigureEntityBase();
        builder.Ignore(task => task.IsArchived);

        builder.Property(task => task.Title).HasMaxLength(ProjectTask.TitleMaxLength).IsRequired();
        builder.Property(task => task.Description).HasMaxLength(ProjectTask.DescriptionMaxLength);
        builder.Property(task => task.Priority).HasConversion<string>().HasMaxLength(16);

        // Número único e estável no projeto (ADR-011).
        builder.HasIndex(task => new { task.ProjectId, task.Number }).IsUnique();
        builder.HasIndex(task => new { task.ProjectId, task.DueDate });

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(task => new { task.OrganizationId, task.ProjectId })
            .HasPrincipalKey(project => new { project.OrganizationId, project.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Fase e marco opcionais, sempre do mesmo projeto (com o valor nulo, a FK não se aplica).
        builder.HasOne<ProjectPhase>()
            .WithMany()
            .HasForeignKey(task => new { task.ProjectId, task.PhaseId })
            .HasPrincipalKey(phase => new { phase.ProjectId, phase.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Milestone>()
            .WithMany()
            .HasForeignKey(task => new { task.ProjectId, task.MilestoneId })
            .HasPrincipalKey(milestone => new { milestone.ProjectId, milestone.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(task => task.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Alvo das FKs compostas das entidades da tarefa: organização, projeto e tarefa coerentes.
        builder.HasAlternateKey(task => new { task.OrganizationId, task.ProjectId, task.Id });

        builder.HasMany(task => task.Assignments)
            .WithOne()
            .HasForeignKey(assignment => new { assignment.OrganizationId, assignment.ProjectId, assignment.TaskId })
            .HasPrincipalKey(task => new { task.OrganizationId, task.ProjectId, task.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(task => task.Assignments)
            .HasField("_assignments")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(task => task.Labels)
            .WithOne()
            .HasForeignKey(label => new { label.OrganizationId, label.ProjectId, label.TaskId })
            .HasPrincipalKey(task => new { task.OrganizationId, task.ProjectId, task.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(task => task.Labels)
            .HasField("_labels")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TaskAssignmentConfiguration : IEntityTypeConfiguration<TaskAssignment>
{
    public void Configure(EntityTypeBuilder<TaskAssignment> builder)
    {
        builder.ToTable("task_assignments");
        builder.HasKey(assignment => new { assignment.TaskId, assignment.UserId });

        // Filtro "minhas tarefas".
        builder.HasIndex(assignment => new { assignment.ProjectId, assignment.UserId });

        // Só membros da equipe do projeto; quem sai da equipe deixa de ser responsável.
        builder.HasOne<ProjectMember>()
            .WithMany()
            .HasForeignKey(assignment => new { assignment.OrganizationId, assignment.ProjectId, assignment.UserId })
            .HasPrincipalKey(member => new { member.OrganizationId, member.ProjectId, member.UserId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TaskLabelConfiguration : IEntityTypeConfiguration<TaskLabel>
{
    public void Configure(EntityTypeBuilder<TaskLabel> builder)
    {
        builder.ToTable("task_labels");
        builder.HasKey(label => new { label.TaskId, label.NormalizedName });

        builder.Property(label => label.Name).HasMaxLength(Domain.Common.Labels.NameMaxLength).IsRequired();
        builder.Property(label => label.NormalizedName).HasMaxLength(Domain.Common.Labels.NameMaxLength).IsRequired();

        // Filtro da listagem de tarefas por etiqueta.
        builder.HasIndex(label => new { label.ProjectId, label.NormalizedName });
    }
}

internal sealed class TaskCommentConfiguration : IEntityTypeConfiguration<TaskComment>
{
    public void Configure(EntityTypeBuilder<TaskComment> builder)
    {
        builder.ToTable("task_comments");
        builder.ConfigureEntityBase();

        builder.Property(comment => comment.Body).HasMaxLength(TaskComment.BodyMaxLength).IsRequired();
        builder.HasIndex(comment => new { comment.TaskId, comment.CreatedAt });

        builder.HasOne<ProjectTask>()
            .WithMany()
            .HasForeignKey(comment => new { comment.OrganizationId, comment.ProjectId, comment.TaskId })
            .HasPrincipalKey(task => new { task.OrganizationId, task.ProjectId, task.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(comment => comment.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> builder)
    {
        builder.ToTable("task_dependencies", table =>
            table.HasCheckConstraint("ck_task_dependencies_not_self", "task_id <> depends_on_task_id"));
        builder.HasKey(dependency => new { dependency.TaskId, dependency.DependsOnTaskId });

        // "Bloqueia": tarefas que dependem desta.
        builder.HasIndex(dependency => dependency.DependsOnTaskId);

        // As duas tarefas são da mesma organização e do mesmo projeto (FKs compostas).
        builder.HasOne<ProjectTask>()
            .WithMany()
            .HasForeignKey(dependency => new { dependency.OrganizationId, dependency.ProjectId, dependency.TaskId })
            .HasPrincipalKey(task => new { task.OrganizationId, task.ProjectId, task.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ProjectTask>()
            .WithMany()
            .HasForeignKey(dependency => new { dependency.OrganizationId, dependency.ProjectId, dependency.DependsOnTaskId })
            .HasPrincipalKey(task => new { task.OrganizationId, task.ProjectId, task.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(dependency => dependency.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
