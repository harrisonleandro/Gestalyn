using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManager.Application.Workflows;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Tasks;

namespace ProjectManager.IntegrationTests.Workflows;

/// <summary>
/// Isolamento das tabelas do workflow entre organizações e projetos: filtro de leitura e FKs compostas
/// recusando estado de outro projeto na tarefa e transição entre estados de projetos diferentes.
/// </summary>
public sealed class WorkflowIsolationTests : IDisposable
{
    private readonly PostgresDatabaseFixture _database;
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly TaskTestKit _kit;

    public WorkflowIsolationTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _database = database;
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _client = _factory.CreateClient();
        _kit = new TaskTestKit(_client, mailpit);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadFilter_HidesWorkflowsStatesTransitionsAndHistoryOfOtherOrganizations()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var otherOrganization = await _kit.Organizations.CreateOrganizationAsync(workspace.Owner);
        var task = await _kit.CreateTaskAsync(workspace, "Com histórico");

        await using var own = _database.CreateContext(workspace.OrganizationId);
        await using var other = _database.CreateContext(otherOrganization.Id);

        Assert.True(await own.Workflows.AnyAsync(w => w.ProjectId == workspace.ProjectId, CancellationToken));
        Assert.True(await own.TaskStatusHistory.AnyAsync(h => h.TaskId == task.Id, CancellationToken));
        Assert.False(await other.Workflows.AnyAsync(w => w.ProjectId == workspace.ProjectId, CancellationToken));
        Assert.False(await other.WorkflowStates.AnyAsync(s => s.ProjectId == workspace.ProjectId, CancellationToken));
        Assert.False(await other.WorkflowTransitions.AnyAsync(t => t.ProjectId == workspace.ProjectId, CancellationToken));
        Assert.False(await other.TaskStatusHistory.AnyAsync(h => h.TaskId == task.Id, CancellationToken));
    }

    [Fact]
    public async Task CompositeForeignKeys_KeepStatesAndTransitionsInsideTheirProject()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var otherProject = await _kit.CreateSecondProjectAsync(workspace);
        var task = await _kit.CreateTaskAsync(workspace, "Tarefa");
        var mine = await WorkflowAsync(workspace.ProjectId, workspace);
        var theirs = await WorkflowAsync(otherProject.Id, workspace);
        var foreignState = theirs.States.Single(s => s.Name == "Concluída").Id;

        var taskWithForeignState = await ExecuteAsync(
            "UPDATE tasks SET state_id = @state WHERE id = @task", ("state", foreignState), ("task", task.Id));
        var transitionAcrossProjects = await ExecuteAsync(
            """
            INSERT INTO workflow_transitions (id, organization_id, project_id, from_state_id, to_state_id, requires_approval, created_at, updated_at)
            VALUES (gen_random_uuid(), @organization, @project, @from, @to, false, now(), now())
            """,
            ("organization", workspace.OrganizationId), ("project", workspace.ProjectId), ("from", mine.States[0].Id), ("to", foreignState));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, taskWithForeignState);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, transitionAcrossProjects);
    }

    private async Task<WorkflowDetails> WorkflowAsync(Guid projectId, Workspace workspace)
    {
        using var response = await _kit.Projects.SendToProjectAsync(HttpMethod.Get, projectId, "/workflow", workspace.Owner, workspace.OrganizationId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await TaskTestKit.ReadAsync<WorkflowDetails>(response);
    }

    private async Task<string?> ExecuteAsync(string sql, params (string Name, Guid Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
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

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
