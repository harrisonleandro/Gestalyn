using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Common;
using ProjectManager.Application.Documents;
using ProjectManager.Application.Projects;
using ProjectManager.Application.Tasks;
using ProjectManager.Domain.Agents;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Workflows;
using ProjectManager.Infrastructure.MultiTenancy;
using ProjectManager.Infrastructure.Persistence;
using ProjectManager.IntegrationTests.Agents.Probes;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Tasks;

namespace ProjectManager.IntegrationTests.Agents;

/// <summary>
/// Etapa 6 (ADR-016, D6): ator nos serviços, autoria de agentes nas tarefas (exatamente um autor, imutável) e versão
/// das tarefas como token de concorrência, com a migração preservando as tarefas existentes.
/// </summary>
public sealed class TaskAuthorshipAndVersionTests(PostgresDatabaseFixture database, MailpitFixture mailpit) : IDisposable
{
    private const string MigrationBeforeAuthorship = "20261118231535_AddAgentCredentialScopesCheck";

    private static readonly string[] AllScopes = [.. AgentScopes.All];

    private static readonly string[] LabelOnly = ["só-etiqueta"];

    private readonly ApiFactory _factory = new(database.ConnectionString, mailpit);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migration_PreservesExistingTasks_WithTheirAuthorAndVersionOne()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(database.ConnectionString)
        {
            Database = $"migration_{Guid.NewGuid():N}",
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<ProjectManagerDbContext>();
        ProjectManagerDatabase.Configure(options, connectionString);
        await using var context = new ProjectManagerDbContext(options.Options, new TestTenantContext(organizationId: null), TimeProvider.System);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeAuthorship, cancellationToken: CancellationToken);

        var userId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(CancellationToken);
            await using var seed = new NpgsqlCommand(
                """
                INSERT INTO organizations (id, name, created_at, updated_at) VALUES (@organization, 'Antiga', now(), now());
                INSERT INTO users (id, display_name, email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count, created_at, updated_at)
                    VALUES (@user, 'Pessoa', true, false, false, true, 0, now(), now());
                INSERT INTO projects (id, organization_id, name, priority, next_task_number, created_at, updated_at)
                    VALUES (@project, @organization, 'Projeto antigo', 'Medium', 2, now(), now());
                INSERT INTO workflows (id, organization_id, project_id, require_independent_approval, created_at, updated_at)
                    VALUES (gen_random_uuid(), @organization, @project, false, now(), now());
                INSERT INTO workflow_states (id, organization_id, project_id, name, category, position, is_initial, created_at, updated_at)
                    VALUES (@state, @organization, @project, 'Backlog', 'Todo', 1, true, now(), now());
                INSERT INTO tasks (id, organization_id, project_id, number, title, priority, created_by_user_id, state_id, created_at, updated_at)
                    VALUES (@task, @organization, @project, 1, 'Tarefa antiga', 'Medium', @user, @state, now(), now());
                """,
                connection);
            seed.Parameters.AddWithValue("organization", Guid.NewGuid());
            seed.Parameters.AddWithValue("user", userId);
            seed.Parameters.AddWithValue("project", Guid.NewGuid());
            seed.Parameters.AddWithValue("state", Guid.NewGuid());
            seed.Parameters.AddWithValue("task", taskId);
            await seed.ExecuteNonQueryAsync(CancellationToken);
        }

        await migrator.MigrateAsync(cancellationToken: CancellationToken);

        var task = await context.Tasks.IgnoreQueryFilters().SingleAsync(t => t.Id == taskId, CancellationToken);
        Assert.Equal((userId, (Guid?)null, 1, "Tarefa antiga"), (task.CreatedByUserId!.Value, task.CreatedByAgentId, task.Revision, task.Title));

        await context.Database.EnsureDeletedAsync(CancellationToken);
    }

    [Fact]
    public async Task Database_RequiresExactlyOneAuthor_KeepsItImmutable_AndTheAgentInTheSameOrganization()
    {
        var (kit, workspace) = await CreateWorkspaceAsync();
        var agentId = await CreateAgentAsync(workspace, "Agente autor");
        var foreign = await CreateWorkspaceAsync();
        var foreignAgentId = await CreateAgentAsync(foreign.Workspace, "Agente de fora");
        var task = await kit.CreateTaskAsync(workspace, "Tarefa de pessoa");

        Assert.Equal(PostgresErrorCodes.CheckViolation, await ExecuteAsync(
            "UPDATE tasks SET created_by_agent_id = @agent WHERE id = @task", ("agent", agentId), ("task", task.Id)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, await ExecuteAsync(
            "UPDATE tasks SET created_by_user_id = NULL WHERE id = @task", ("task", task.Id)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, await ExecuteAsync(
            "UPDATE tasks SET created_by_user_id = NULL, created_by_agent_id = @agent WHERE id = @task", ("agent", agentId), ("task", task.Id)));
        // Trocar o autor por outra pessoa: o gatilho recusa antes de qualquer outra checagem.
        Assert.Equal(PostgresErrorCodes.CheckViolation, await ExecuteAsync(
            "UPDATE tasks SET created_by_user_id = @user WHERE id = @task", ("user", Guid.NewGuid()), ("task", task.Id)));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, await ExecuteAsync(
            """
            INSERT INTO tasks (id, organization_id, project_id, number, title, priority, created_by_agent_id, state_id, created_at, updated_at)
            SELECT gen_random_uuid(), organization_id, project_id, 999, 'Autor de fora', 'Medium', @agent, state_id, now(), now() FROM tasks WHERE id = @task
            """,
            ("agent", foreignAgentId), ("task", task.Id)));
        Assert.Null(await ExecuteAsync("UPDATE tasks SET title = 'Só o título' WHERE id = @task", ("task", task.Id)));
    }

    [Fact]
    public async Task AgentGrant_CreatesTheTask_AsTheAgent_WithHistoryAndAuthorName()
    {
        var (kit, workspace) = await CreateWorkspaceAsync();
        var agentId = await CreateAgentAsync(workspace, "Agente criador");

        using var scope = _factory.Services.CreateScope();
        var grant = AgentGrant(scope, workspace, agentId, AgentRequirement.TasksWrite);
        var tasks = scope.ServiceProvider.GetRequiredService<ITaskService>();
        var created = await tasks.CreateAsync(grant, new CreateTaskRequest { Title = "Criada pelo agente" }, CancellationToken);

        Assert.True(created.Succeeded, created.Error?.Message);
        var details = created.Value!;
        Assert.Null(details.CreatedByUserId);
        Assert.Equal(new ActorReference(agentId, ActorKind.Agent, "Agente criador"), details.CreatedBy);
        Assert.Equal(1, details.Version);

        var history = await tasks.ListHistoryAsync(details.Id, CancellationToken);
        var first = Assert.Single(history);
        Assert.Equal((agentId, ActorKind.Agent, "Agente criador"), (first.ChangedById, first.ActorKind, first.ChangedByName));

        // Pessoas leem a autoria do agente pela API.
        using var asPerson = await kit.SendToTaskAsync(HttpMethod.Get, workspace, details.Id, string.Empty, workspace.Owner);
        var read = await TaskTestKit.ReadAsync<TaskDetails>(asPerson);
        Assert.Equal(new ActorReference(agentId, ActorKind.Agent, "Agente criador"), read.CreatedBy);

        await using var context = database.CreateContext(workspace.OrganizationId);
        var stored = await context.Tasks.SingleAsync(t => t.Id == details.Id, CancellationToken);
        Assert.Equal(((Guid?)null, (Guid?)agentId), (stored.CreatedByUserId, stored.CreatedByAgentId));
    }

    [Fact]
    public async Task PeopleOnlyOperations_RefuseAnAgentGrant_WithoutWritingAnything()
    {
        var (kit, workspace) = await CreateWorkspaceAsync();
        var agentId = await CreateAgentAsync(workspace, "Agente barrado");
        var task = await kit.CreateTaskAsync(workspace, "Tarefa");

        using var scope = _factory.Services.CreateScope();
        var grant = AgentGrant(scope, workspace, agentId, AgentRequirement.TasksWrite);
        var services = scope.ServiceProvider;

        Result[] results =
        [
            await services.GetRequiredService<IDocumentService>().CreateAsync(grant, new CreateDocumentRequest { Title = "Doc", Body = "# Doc" }, CancellationToken),
            await services.GetRequiredService<IDecisionService>().CreateAsync(grant, new CreateDecisionRequest { Title = "D", Context = "C", Outcome = "O" }, CancellationToken),
            await services.GetRequiredService<ITaskService>().AddAssignmentAsync(grant, task.Id, new AddAssignmentRequest { UserId = workspace.Owner.Id }, CancellationToken),
        ];

        Assert.All(results, result => Assert.Equal((ErrorKind.Forbidden, ProjectAccessGrant.PeopleOnlyMessage), (result.Error!.Kind, result.Error.Message)));

        await using var context = database.CreateContext(workspace.OrganizationId);
        Assert.False(await context.ProjectDocuments.AnyAsync(d => d.ProjectId == workspace.ProjectId, CancellationToken));
        Assert.False(await context.Decisions.AnyAsync(d => d.ProjectId == workspace.ProjectId, CancellationToken));
        Assert.False(await context.TaskAssignments.AnyAsync(a => a.TaskId == task.Id, CancellationToken));
    }

    [Fact]
    public async Task AgentRequest_IsRecordedAsTheAgent_InTheAuditActor()
    {
        using var probes = _factory.WithProbes(typeof(AgentIsolationProbeController));
        var agents = new AgentTestKit(probes.CreateClient(), mailpit);
        var (owner, organizationId) = await agents.CreateOwnerAsync();
        var agent = await agents.CreateAgentAsync(owner, organizationId, "Agente auditado");
        var token = (await agents.CreateCredentialAsync(owner, organizationId, agent.Id, AllScopes)).Token;

        using var asAgent = await agents.SendAsAgentAsync(token, HttpMethod.Get, "/api/v1/agents/__probe/actor", headers: new Dictionary<string, string> { ["X-Gestalyn-Client"] = "mcp" });
        var actor = await TaskTestKit.ReadAsync<ProbeActor>(asAgent);

        Assert.Equal(new ProbeActor(agent.Id, nameof(ActorKind.Agent), "Mcp"), actor);
    }

    [Fact]
    public async Task ActivityNames_IncludeAgents()
    {
        var (kit, workspace) = await CreateWorkspaceAsync();
        var agentId = await CreateAgentAsync(workspace, "Agente nomeado");
        Assert.Null(await ExecuteAsync(
            """
            INSERT INTO activity_events (id, organization_id, project_id, actor_id, actor_kind, origin, action, entity_type, entity_id, summary, occurred_at)
            VALUES (gen_random_uuid(), @organization, @project, @agent, 'Agent', 'Mcp', 'task.updated', 'task', gen_random_uuid(), 'Feito pelo agente', now())
            """,
            ("organization", workspace.OrganizationId), ("project", workspace.ProjectId), ("agent", agentId)));

        using var response = await kit.SendToProjectAsync(HttpMethod.Get, workspace, "/activities", workspace.Owner);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"actorName\":\"Agente nomeado\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Version_GrowsOnEveryTaskChange_AndAStaleVersionNeverOverwrites()
    {
        var (kit, workspace) = await CreateWorkspaceAsync();
        var task = await kit.CreateTaskAsync(workspace, "Versionada");
        Assert.Equal(1, task.Version);

        var renamed = await PatchAsync(kit, workspace, task.Id, new { title = "Versão 2" });
        var labeled = await PatchAsync(kit, workspace, task.Id, new { labels = LabelOnly });
        Assert.Equal((2, 3), (renamed.Version, labeled.Version));

        using (var stale = await kit.SendToTaskAsync(HttpMethod.Patch, workspace, task.Id, string.Empty, workspace.Owner, new { title = "Sobrescrita", expectedVersion = 2 }))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }

        var current = await PatchAsync(kit, workspace, task.Id, new { description = "Na versão certa", expectedVersion = 3 });
        Assert.Equal(("Versão 2", 4), (current.Title, current.Version));

        // Transição também muda a versão; versão desatualizada na transição dá 409.
        var todo = current.AvailableTransitions.Single(t => t.ToStateName == "A fazer").ToStateId;
        using (var staleMove = await kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/transitions", workspace.Owner, new { toStateId = todo, expectedVersion = 3 }))
        {
            Assert.Equal(HttpStatusCode.Conflict, staleMove.StatusCode);
        }

        using (var move = await kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/transitions", workspace.Owner, new { toStateId = todo, expectedVersion = 4 }))
        {
            Assert.Equal(5, (await TaskTestKit.ReadAsync<TaskDetails>(move)).Version);
        }

        // Recursos próprios da tarefa (responsáveis, comentários) não mudam a versão.
        using (var assign = await kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/assignments", workspace.Owner, new { userId = workspace.Owner.Id }))
        using (var comment = await kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/comments", workspace.Owner, new { body = "Oi" }))
        {
            assign.EnsureSuccessStatusCode();
            comment.EnsureSuccessStatusCode();
        }

        using (var afterChildren = await kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, string.Empty, workspace.Owner))
        {
            Assert.Equal(5, (await TaskTestKit.ReadAsync<TaskDetails>(afterChildren)).Version);
        }

        using (var archive = await kit.SendToTaskAsync(HttpMethod.Delete, workspace, task.Id, string.Empty, workspace.Owner))
        {
            archive.EnsureSuccessStatusCode();
        }

        await using var context = database.CreateContext(workspace.OrganizationId);
        Assert.Equal(6, await context.Tasks.Where(t => t.Id == task.Id).Select(t => t.Revision).SingleAsync(CancellationToken));
    }

    [Fact]
    public async Task ConcurrentEdits_WithTheSameVersion_ExactlyOneWins()
    {
        var (kit, workspace) = await CreateWorkspaceAsync();
        var task = await kit.CreateTaskAsync(workspace, "Disputada");

        var responses = await Task.WhenAll(Enumerable.Range(1, 5).Select(i =>
            kit.SendToTaskAsync(HttpMethod.Patch, workspace, task.Id, string.Empty, workspace.Owner, new { title = $"Edição {i}", expectedVersion = 1 })));
        try
        {
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        using var read = await kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, string.Empty, workspace.Owner);
        Assert.Equal(2, (await TaskTestKit.ReadAsync<TaskDetails>(read)).Version);
    }

    public void Dispose() => _factory.Dispose();

    private static ProjectAccessGrant AgentGrant(IServiceScope scope, Workspace workspace, Guid agentId, AgentRequirement requirement)
    {
        var agent = new ActiveAgent(workspace.OrganizationId, agentId, Guid.CreateVersion7(), AllScopes.ToHashSet(StringComparer.Ordinal));
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetActiveAgent(agent);
        return AgentAccessGrant.Compute(agent, agentIsActive: true, workspace.ProjectId, ProjectRole.Contributor, requirement).Grant!;
    }

    private static async Task<TaskDetails> PatchAsync(TaskTestKit kit, Workspace workspace, Guid taskId, object body)
    {
        using var response = await kit.SendToTaskAsync(HttpMethod.Patch, workspace, taskId, string.Empty, workspace.Owner, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await TaskTestKit.ReadAsync<TaskDetails>(response);
    }

    private async Task<(TaskTestKit Kit, Workspace Workspace)> CreateWorkspaceAsync()
    {
        var kit = new TaskTestKit(_factory.CreateClient(), mailpit);
        return (kit, await kit.CreateWorkspaceAsync());
    }

    private async Task<Guid> CreateAgentAsync(Workspace workspace, string name)
    {
        var agents = new AgentTestKit(_factory.CreateClient(), mailpit);
        return (await agents.CreateAgentAsync(workspace.Owner, workspace.OrganizationId, name)).Id;
    }

    private async Task<string?> ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        try
        {
            await command.ExecuteNonQueryAsync(CancellationToken);
            return null;
        }
        catch (PostgresException exception)
        {
            return exception.SqlState;
        }
    }
}
