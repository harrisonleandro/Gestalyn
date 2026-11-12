using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManager.Application.Common;
using ProjectManager.Application.Tasks;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Organizations;

namespace ProjectManager.IntegrationTests.Tasks;

/// <summary>
/// Permissões de tarefa por papel de projeto e isolamento das entidades de tarefa entre organizações e
/// projetos: API, filtro de leitura e FKs compostas.
/// </summary>
public sealed class TaskAccessTests : IDisposable
{
    private readonly PostgresDatabaseFixture _database;
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly TaskTestKit _kit;

    public TaskAccessTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _database = database;
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _client = _factory.CreateClient();
        _kit = new TaskTestKit(_client, mailpit);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ProjectRoles_GrantTaskPermissions()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var viewer = await _kit.AddTeamMemberAsync(workspace, "Viewer");
        var contributor = await _kit.AddTeamMemberAsync(workspace, "Contributor");
        var outsideTheTeam = await _kit.Organizations.CreateUserAsync();
        await _kit.Organizations.AddMemberAsync(workspace.Owner, workspace.OrganizationId, outsideTheTeam, "Member");
        var task = await _kit.CreateTaskAsync(workspace, "Tarefa");

        // (usuário, criar, editar, arquivar): true = sucesso, false = 403.
        (TestUser User, bool Create, bool Edit, bool Archive)[] matrix =
        [
            (viewer, false, false, false),
            (contributor, true, true, false),
        ];

        foreach (var (user, canCreate, canEdit, canArchive) in matrix)
        {
            using var read = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, "", user);
            using var list = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, "/tasks", user);
            using var create = await _kit.SendToProjectAsync(HttpMethod.Post, workspace, "/tasks", user, new { title = "Nova" });
            using var edit = await _kit.SendToTaskAsync(HttpMethod.Patch, workspace, task.Id, "", user, new { priority = "High" });
            using var archive = await _kit.SendToTaskAsync(HttpMethod.Delete, workspace, task.Id, "", user);

            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            Assert.Equal(canCreate ? HttpStatusCode.Created : HttpStatusCode.Forbidden, create.StatusCode);
            Assert.Equal(canEdit ? HttpStatusCode.OK : HttpStatusCode.Forbidden, edit.StatusCode);
            Assert.Equal(canArchive ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden, archive.StatusCode);
        }

        using var outsiderReads = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, "", outsideTheTeam);
        using var outsiderLists = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, "/tasks", outsideTheTeam);
        using var managerArchives = await _kit.SendToTaskAsync(HttpMethod.Delete, workspace, task.Id, "", workspace.Owner);

        Assert.Equal(HttpStatusCode.NotFound, outsiderReads.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, outsiderLists.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, managerArchives.StatusCode);
    }

    [Fact]
    public async Task OwnerOfAnotherOrganization_GetsNotFoundOnEveryTaskEndpoint()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var intruder = await _kit.Organizations.CreateUserAsync();
        var intruderOrganization = await _kit.Organizations.CreateOrganizationAsync(intruder);
        var task = await _kit.CreateTaskAsync(workspace, "Privada");
        var other = await _kit.CreateTaskAsync(workspace, "Outra");
        using var commentResponse = await _kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/comments", workspace.Owner, new { body = "Interno" });
        var comment = await TaskTestKit.ReadAsync<CommentDetails>(commentResponse);

        (HttpMethod Method, string SubPath, object? Body)[] requests =
        [
            (HttpMethod.Get, "", null),
            (HttpMethod.Patch, "", new { title = "Invadida" }),
            (HttpMethod.Delete, "", null),
            (HttpMethod.Post, "/assignments", new { userId = intruder.Id }),
            (HttpMethod.Delete, $"/assignments/{workspace.Owner.Id}", null),
            (HttpMethod.Post, "/dependencies", new { dependsOnTaskId = other.Id }),
            (HttpMethod.Get, "/comments", null),
            (HttpMethod.Post, "/comments", new { body = "Invasão" }),
            (HttpMethod.Patch, $"/comments/{comment.Id}", new { body = "Invasão" }),
            (HttpMethod.Delete, $"/comments/{comment.Id}", null),
        ];

        foreach (var organizationId in new[] { intruderOrganization.Id, workspace.OrganizationId })
        {
            foreach (var (method, subPath, body) in requests)
            {
                using var response = await _kit.SendToTaskAsync(method, task.Id, subPath, intruder, organizationId, body);
                Assert.True(
                    response.StatusCode == HttpStatusCode.NotFound,
                    $"{method} {subPath} (organização {organizationId}): esperado 404, obtido {response.StatusCode}.");
            }
        }

        using var stillPrivate = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, "", workspace.Owner);
        Assert.Equal("Privada", (await TaskTestKit.ReadAsync<TaskDetails>(stillPrivate)).Title);
    }

    [Fact]
    public async Task ReadFilter_HidesTaskDataFromOtherOrganizations()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var otherOrganization = await _kit.Organizations.CreateOrganizationAsync(workspace.Owner);
        var blocker = await _kit.CreateTaskAsync(workspace, "Bloqueadora");
        var task = await _kit.CreateTaskAsync(workspace, workspace.Owner, new { title = "Completa", labels = new List<string> { "x" }, assigneeIds = new List<Guid> { workspace.Owner.Id } });
        (await _kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/comments", workspace.Owner, new { body = "c" })).Dispose();
        (await _kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/dependencies", workspace.Owner, new { dependsOnTaskId = blocker.Id })).Dispose();

        await using var own = _database.CreateContext(workspace.OrganizationId);
        await using var other = _database.CreateContext(otherOrganization.Id);

        Assert.True(await own.TaskAssignments.AnyAsync(a => a.TaskId == task.Id, CancellationToken));
        Assert.True(await own.TaskLabels.AnyAsync(l => l.TaskId == task.Id, CancellationToken));
        Assert.True(await own.TaskComments.AnyAsync(c => c.TaskId == task.Id, CancellationToken));
        Assert.True(await own.TaskDependencies.AnyAsync(d => d.TaskId == task.Id, CancellationToken));
        Assert.False(await other.Tasks.AnyAsync(t => t.ProjectId == workspace.ProjectId, CancellationToken));
        Assert.False(await other.TaskAssignments.AnyAsync(a => a.TaskId == task.Id, CancellationToken));
        Assert.False(await other.TaskLabels.AnyAsync(l => l.TaskId == task.Id, CancellationToken));
        Assert.False(await other.TaskComments.AnyAsync(c => c.TaskId == task.Id, CancellationToken));
        Assert.False(await other.TaskDependencies.AnyAsync(d => d.TaskId == task.Id, CancellationToken));
        Assert.False(await other.Milestones.AnyAsync(m => m.ProjectId == workspace.ProjectId, CancellationToken));
    }

    [Fact]
    public async Task CompositeForeignKeys_RejectLinksAcrossProjectsAndOrganizations()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var otherProject = await _kit.CreateSecondProjectAsync(workspace);
        var outsider = await _kit.Organizations.CreateUserAsync();
        await _kit.Organizations.AddMemberAsync(workspace.Owner, workspace.OrganizationId, outsider, "Member");
        var task = await _kit.CreateTaskAsync(workspace, "Tarefa");
        using var foreignTaskResponse = await _kit.Projects.SendToProjectAsync(
            HttpMethod.Post, otherProject.Id, "/tasks", workspace.Owner, workspace.OrganizationId, new { title = "Do outro projeto" });
        var foreignTask = await TaskTestKit.ReadAsync<TaskDetails>(foreignTaskResponse);
        using var foreignPhaseResponse = await _kit.Projects.SendToProjectAsync(
            HttpMethod.Post, otherProject.Id, "/phases", workspace.Owner, workspace.OrganizationId, new { name = "Fase do outro projeto" });
        var foreignPhase = await TaskTestKit.ReadAsync<Application.Planning.PhaseDetails>(foreignPhaseResponse);

        var phaseFromAnotherProject = await ExecuteAsync(
            "UPDATE tasks SET phase_id = @phase WHERE id = @task", ("phase", foreignPhase.Id), ("task", task.Id));
        var assigneeOutsideTheTeam = await ExecuteAsync(
            "INSERT INTO task_assignments (task_id, user_id, organization_id, project_id, assigned_at) VALUES (@task, @user, @organization, @project, now())",
            ("task", task.Id), ("user", outsider.Id), ("organization", workspace.OrganizationId), ("project", workspace.ProjectId));
        var dependencyAcrossProjects = await ExecuteAsync(
            "INSERT INTO task_dependencies (task_id, depends_on_task_id, organization_id, project_id, created_by_user_id, created_at) VALUES (@task, @other, @organization, @project, @user, now())",
            ("task", task.Id), ("other", foreignTask.Id), ("organization", workspace.OrganizationId), ("project", workspace.ProjectId), ("user", workspace.Owner.Id));
        var commentWithWrongOrganization = await ExecuteAsync(
            "INSERT INTO task_comments (id, organization_id, project_id, task_id, author_user_id, body, created_at, updated_at) VALUES (gen_random_uuid(), gen_random_uuid(), @project, @task, @user, 'x', now(), now())",
            ("project", workspace.ProjectId), ("task", task.Id), ("user", workspace.Owner.Id));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, phaseFromAnotherProject);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, assigneeOutsideTheTeam);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, dependencyAcrossProjects);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, commentWithWrongOrganization);

        using var list = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, "/tasks", workspace.Owner);
        Assert.Null(Assert.Single((await TaskTestKit.ReadAsync<PagedResult<TaskSummary>>(list)).Items).PhaseId);
    }

    /// <summary>SQL direto, sem a validação da aplicação: só o banco decide. Retorna o SQLSTATE do erro.</summary>
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
