using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Tasks;
using ProjectManager.Domain.Workflows;

namespace ProjectManager.Infrastructure.Persistence.Configurations;

internal sealed class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.ToTable("workflows");
        builder.ConfigureEntityBase();
        builder.Ignore(workflow => workflow.InitialState);

        // Um workflow por projeto; (organization_id, project_id) é o alvo das FKs de estados e transições.
        builder.HasIndex(workflow => workflow.ProjectId).IsUnique();
        builder.HasAlternateKey(workflow => new { workflow.OrganizationId, workflow.ProjectId });

        builder.HasOne<Project>()
            .WithOne()
            .HasForeignKey<WorkflowDefinition>(workflow => new { workflow.OrganizationId, workflow.ProjectId })
            .HasPrincipalKey<Project>(project => new { project.OrganizationId, project.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(workflow => workflow.States)
            .WithOne()
            .HasForeignKey(state => new { state.OrganizationId, state.ProjectId })
            .HasPrincipalKey(workflow => new { workflow.OrganizationId, workflow.ProjectId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(workflow => workflow.States)
            .HasField("_states")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(workflow => workflow.Transitions)
            .WithOne()
            .HasForeignKey(transition => new { transition.OrganizationId, transition.ProjectId })
            .HasPrincipalKey(workflow => new { workflow.OrganizationId, workflow.ProjectId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(workflow => workflow.Transitions)
            .HasField("_transitions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class WorkflowStateConfiguration : IEntityTypeConfiguration<WorkflowState>
{
    public void Configure(EntityTypeBuilder<WorkflowState> builder)
    {
        builder.ToTable("workflow_states", table => table.HasCheckConstraint("ck_workflow_states_position_positive", "position > 0"));
        builder.ConfigureEntityBase();
        builder.Ignore(state => state.IsFinal);

        builder.Property(state => state.Name).HasMaxLength(WorkflowState.NameMaxLength).IsRequired();
        builder.Property(state => state.Category).HasConversion<string>().HasMaxLength(16);

        // Alvo das FKs de tarefas e transições: o estado precisa ser do mesmo projeto.
        builder.HasAlternateKey(state => new { state.ProjectId, state.Id });
        builder.HasIndex(state => new { state.ProjectId, state.Position });

        // "Um único estado inicial" fica no domínio, não num índice único parcial: trocar o estado inicial
        // desmarca um e marca outro na mesma gravação, e o índice seria violado no meio dela. As edições do
        // workflow travam a linha do workflow (xmin), o que serializa alterações simultâneas (ADR-013).
    }
}

internal sealed class WorkflowTransitionConfiguration : IEntityTypeConfiguration<WorkflowTransition>
{
    public void Configure(EntityTypeBuilder<WorkflowTransition> builder)
    {
        builder.ToTable("workflow_transitions", table =>
            table.HasCheckConstraint("ck_workflow_transitions_distinct_states", "from_state_id <> to_state_id"));
        builder.ConfigureEntityBase();

        builder.HasIndex(transition => new { transition.ProjectId, transition.FromStateId, transition.ToStateId }).IsUnique();

        builder.HasOne<WorkflowState>()
            .WithMany()
            .HasForeignKey(transition => new { transition.ProjectId, transition.FromStateId })
            .HasPrincipalKey(state => new { state.ProjectId, state.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WorkflowState>()
            .WithMany()
            .HasForeignKey(transition => new { transition.ProjectId, transition.ToStateId })
            .HasPrincipalKey(state => new { state.ProjectId, state.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TaskStatusHistoryConfiguration : IEntityTypeConfiguration<TaskStatusHistory>
{
    public void Configure(EntityTypeBuilder<TaskStatusHistory> builder)
    {
        builder.ToTable("task_status_history");
        builder.ConfigureEntityBase();

        builder.Property(entry => entry.FromStateName).HasMaxLength(WorkflowState.NameMaxLength);
        builder.Property(entry => entry.ToStateName).HasMaxLength(WorkflowState.NameMaxLength).IsRequired();
        builder.Property(entry => entry.ActorKind).HasConversion<string>().HasMaxLength(16);
        builder.Property(entry => entry.Comment).HasMaxLength(TaskStatusHistory.CommentMaxLength);

        builder.HasIndex(entry => new { entry.TaskId, entry.ChangedAt });

        // Os estados podem ser renomeados ou excluídos: o histórico guarda os nomes, sem FK para eles.
        builder.HasOne<ProjectTask>()
            .WithMany()
            .HasForeignKey(entry => new { entry.OrganizationId, entry.ProjectId, entry.TaskId })
            .HasPrincipalKey(task => new { task.OrganizationId, task.ProjectId, task.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
