using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Workflows;
using ProjectManager.Infrastructure.Persistence;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Workflows;

/// <summary>
/// Migração de dados da Fase 07 (ADR-013, D3): projetos e tarefas que já existiam recebem o workflow da
/// configuração inicial, igual ao de projetos novos, e as tarefas entram no estado inicial com histórico.
/// </summary>
public sealed class WorkflowMigrationTests(PostgresDatabaseFixture database)
{
    private const string MigrationBeforeWorkflows = "20261114191830_AddRefreshTokenSuccessor";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExistingProjectsAndTasks_ReceiveTheDefaultWorkflow_AndTheInitialState()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(database.ConnectionString)
        {
            Database = $"migration_{Guid.NewGuid():N}",
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<ProjectManagerDbContext>();
        ProjectManagerDatabase.Configure(options, connectionString);
        await using var context = new ProjectManagerDbContext(options.Options, new TestTenantContext(organizationId: null), TimeProvider.System);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeWorkflows, cancellationToken: CancellationToken);

        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        Guid[] projectIds = [Guid.NewGuid(), Guid.NewGuid()];
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(CancellationToken);
            await using var seed = new NpgsqlCommand(
                """
                INSERT INTO organizations (id, name, created_at, updated_at) VALUES (@organization, 'Antiga', now(), now());
                INSERT INTO users (id, display_name, email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count, created_at, updated_at)
                    VALUES (@user, 'Pessoa', true, false, false, true, 0, now(), now());
                INSERT INTO projects (id, organization_id, name, priority, next_task_number, created_at, updated_at) VALUES
                    (@first, @organization, 'Com tarefas', 'Medium', 3, now(), now()),
                    (@second, @organization, 'Sem tarefas', 'Medium', 1, now(), now());
                INSERT INTO tasks (id, organization_id, project_id, number, title, priority, created_by_user_id, created_at, updated_at) VALUES
                    (gen_random_uuid(), @organization, @first, 1, 'Primeira', 'Medium', @user, now(), now()),
                    (gen_random_uuid(), @organization, @first, 2, 'Segunda', 'High', @user, now(), now());
                """,
                connection);
            seed.Parameters.AddWithValue("organization", organizationId);
            seed.Parameters.AddWithValue("user", userId);
            seed.Parameters.AddWithValue("first", projectIds[0]);
            seed.Parameters.AddWithValue("second", projectIds[1]);
            await seed.ExecuteNonQueryAsync(CancellationToken);
        }

        await migrator.MigrateAsync(cancellationToken: CancellationToken);

        // O workflow migrado é igual ao de um projeto novo: mesmos estados, categorias, ordem e transições.
        var expected = WorkflowDefinition.CreateDefault(new Project(organizationId, "Referência"));
        var expectedStates = expected.States.Select(s => (s.Name, s.Category, s.Position, s.IsInitial)).ToList();
        var expectedTransitions = Describe(expected.Transitions, expected.States);

        foreach (var projectId in projectIds)
        {
            Assert.Equal(1, await context.Workflows.IgnoreQueryFilters().CountAsync(w => w.ProjectId == projectId, CancellationToken));

            var states = await context.WorkflowStates.IgnoreQueryFilters().Where(s => s.ProjectId == projectId).OrderBy(s => s.Position).ToListAsync(CancellationToken);
            var transitions = await context.WorkflowTransitions.IgnoreQueryFilters().Where(t => t.ProjectId == projectId).ToListAsync(CancellationToken);

            Assert.Equal(expectedStates, states.Select(s => (s.Name, s.Category, s.Position, s.IsInitial)));
            Assert.Equal(expectedTransitions, Describe(transitions, states));
        }

        var tasks = await context.Tasks.IgnoreQueryFilters().Where(t => t.ProjectId == projectIds[0]).ToListAsync(CancellationToken);
        var backlogId = await context.WorkflowStates.IgnoreQueryFilters()
            .Where(s => s.ProjectId == projectIds[0] && s.IsInitial)
            .Select(s => s.Id)
            .SingleAsync(CancellationToken);
        var history = await context.TaskStatusHistory.IgnoreQueryFilters().Where(h => h.ProjectId == projectIds[0]).ToListAsync(CancellationToken);

        Assert.All(tasks, task => Assert.Equal(backlogId, task.StateId));
        Assert.Equal(tasks.Select(t => t.Id).Order(), history.Select(h => h.TaskId).Order());
        Assert.All(history, entry =>
        {
            Assert.Null(entry.FromStateId);
            Assert.Equal(("Backlog", userId, ActorKind.User), (entry.ToStateName, entry.ChangedById, entry.ActorKind));
        });

        await context.Database.EnsureDeletedAsync(CancellationToken);
    }

    private static List<string> Describe(IEnumerable<WorkflowTransition> transitions, IReadOnlyCollection<WorkflowState> states)
    {
        var names = states.ToDictionary(s => s.Id, s => s.Name);
        return transitions
            .Select(t => $"{names[t.FromStateId]} -> {names[t.ToStateId]}{(t.RequiresApproval ? " (aprovação)" : "")}")
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
