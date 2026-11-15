using System.Net;
using System.Text.Json;
using Npgsql;
using ProjectManager.Application.Activity;
using ProjectManager.Application.Tasks;
using ProjectManager.Application.Workflows;
using ProjectManager.Domain.Activity;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Projects;
using ProjectManager.IntegrationTests.Tasks;

namespace ProjectManager.IntegrationTests.Activity;

/// <summary>Eventos e registros manuais (ADR-014): autor, origem, alterações, imutabilidade, filtros e permissões.</summary>
public sealed class ActivityTests : IDisposable
{
    private readonly PostgresDatabaseFixture _database;
    private readonly ApiFactory _factory;
    private readonly ActivityTestKit _kit;

    public ActivityTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _database = database;
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _kit = new ActivityTestKit(_factory.CreateClient(), mailpit);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private TaskTestKit Tasks => _kit.Tasks;

    [Fact]
    public async Task Events_IdentifyTheAuthorFromTheToken_TheDeclaredOrigin_AndTheChangedFields()
    {
        var workspace = await Tasks.CreateWorkspaceAsync();
        var other = await Tasks.AddTeamMemberAsync(workspace, "Contributor");
        var task = await Tasks.CreateTaskAsync(workspace, "Primeira versão");
        var path = $"/api/v1/tasks/{task.Id}";

        // O corpo tenta se passar por outra pessoa: o autor continua sendo o do token.
        foreach (var (client, title) in new[] { ("web", "Web"), ("CLI", "Cli"), (null, "Api"), ("navegador-x", "Desconhecido") })
        {
            using var response = await _kit.SendWithClientAsync(
                HttpMethod.Patch, path, workspace.Owner, workspace.OrganizationId, new { title, createdByUserId = other.Id, actorId = other.Id }, client);
            response.EnsureSuccessStatusCode();
        }

        using (var description = await _kit.SendWithClientAsync(
            HttpMethod.Patch, path, workspace.Owner, workspace.OrganizationId, new { description = "Texto confidencial do cliente" }, "web"))
        {
            description.EnsureSuccessStatusCode();
        }

        var updates = (await _kit.ListProjectAsync(workspace.Owner, workspace.OrganizationId, workspace.ProjectId, $"?action=task.updated&taskId={task.Id}")).Items;

        Assert.Equal(5, updates.Count);
        Assert.All(updates, item => Assert.Equal((workspace.Owner.Id, "task", task.Id), (item.ActorId!.Value, item.EntityType!, item.EntityId!.Value)));
        Assert.All(updates, item => Assert.False(string.IsNullOrEmpty(item.RequestId)));
        Assert.Equal(
            [ActivityOrigin.Web, ActivityOrigin.Api, ActivityOrigin.Api, ActivityOrigin.Cli, ActivityOrigin.Web],
            updates.Select(item => item.Origin));

        var renamed = updates[^1].Changes!.Value.GetProperty("title");
        Assert.Equal(("Primeira versão", "Web"), (renamed.GetProperty("from").GetString(), renamed.GetProperty("to").GetString()));

        // A descrição mudou, mas o conteúdo não vai para a auditoria.
        var descriptionChange = updates[0].Changes!.Value.GetProperty("description");
        Assert.True(descriptionChange.GetProperty("changed").GetBoolean());
        Assert.DoesNotContain("confidencial", updates[0].Changes!.Value.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectedOperations_LeaveNoEvent()
    {
        var workspace = await Tasks.CreateWorkspaceAsync();
        var viewer = await Tasks.AddTeamMemberAsync(workspace, "Viewer");
        var task = await Tasks.CreateTaskAsync(workspace, "Tarefa");
        var before = await CountAsync(workspace);

        using var forbidden = await Tasks.SendToTaskAsync(HttpMethod.Patch, workspace, task.Id, "", viewer, new { title = "Mudou" });
        using var notFound = await Tasks.SendToTaskAsync(HttpMethod.Patch, workspace, Guid.NewGuid(), "", workspace.Owner, new { title = "Mudou" });
        var done = (await ReadWorkflowAsync(workspace)).States.Single(s => s.Name == "Concluída");
        using var invalidTransition = await Tasks.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/transitions", workspace.Owner, new { toStateId = done.Id });
        using var invalidBody = await Tasks.SendToTaskAsync(HttpMethod.Patch, workspace, task.Id, "", workspace.Owner, new { title = " " });

        Assert.Equal(
            [HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.Conflict, HttpStatusCode.BadRequest],
            new[] { forbidden.StatusCode, notFound.StatusCode, invalidTransition.StatusCode, invalidBody.StatusCode });
        Assert.Equal(before, await CountAsync(workspace));
    }

    [Fact]
    public async Task ActivityRecords_CannotBeChangedOrDeleted_EvenDirectlyInTheDatabase()
    {
        var workspace = await Tasks.CreateWorkspaceAsync();
        using (var log = await Tasks.SendToProjectAsync(HttpMethod.Post, workspace, "/activities", workspace.Owner, new { kind = "Note", summary = "Reunião de abertura" }))
        {
            log.EnsureSuccessStatusCode();
        }

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        foreach (var sql in new[]
        {
            "UPDATE activity_events SET summary = 'adulterado' WHERE organization_id = @organization",
            "DELETE FROM activity_events WHERE organization_id = @organization",
            "UPDATE activity_logs SET summary = 'adulterado' WHERE organization_id = @organization",
            "DELETE FROM activity_logs WHERE organization_id = @organization",
            "TRUNCATE activity_events CASCADE",
            "TRUNCATE activity_logs",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("organization", workspace.OrganizationId);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(CancellationToken));
            Assert.Contains("imutáveis", exception.MessageText, StringComparison.Ordinal);
        }

        var items = (await _kit.ListProjectAsync(workspace.Owner, workspace.OrganizationId, workspace.ProjectId)).Items;
        Assert.DoesNotContain(items, item => item.Summary == "adulterado");
        Assert.Contains(items, item => item.Source == ActivitySource.Log);
    }

    [Fact]
    public async Task List_CombinesEventsAndManualLogs_WithFiltersAndPagination()
    {
        var workspace = await Tasks.CreateWorkspaceAsync();
        var contributor = await Tasks.AddTeamMemberAsync(workspace, "Contributor");
        var first = await Tasks.CreateTaskAsync(workspace, "Primeira");
        var second = await Tasks.CreateTaskAsync(workspace, contributor, new { title = "Segunda" });
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1);

        using (var meeting = await Tasks.SendToProjectAsync(HttpMethod.Post, workspace, "/activities", contributor,
            new { kind = "Meeting", summary = "Revisão com o cliente", details = "Ata **resumida**", taskId = first.Id, occurredAt = yesterday }))
        {
            Assert.Equal(HttpStatusCode.Created, meeting.StatusCode);
            var created = await ProjectTestKit.ReadAsync<ActivityItem>(meeting);
            Assert.Equal((ActivitySource.Log, ActivityLogKind.Meeting, contributor.Id), (created.Source, created.Kind!.Value, created.ActorId!.Value));
            Assert.NotNull(created.ActorName);
        }

        var all = await _kit.ListProjectAsync(workspace.Owner, workspace.OrganizationId, workspace.ProjectId);
        var project = await _kit.ListProjectAsync(workspace.Owner, workspace.OrganizationId, workspace.ProjectId, "?source=Event");
        var logs = await _kit.ListProjectAsync(workspace.Owner, workspace.OrganizationId, workspace.ProjectId, "?source=Log");
        var byTask = await _kit.ListProjectAsync(workspace.Owner, workspace.OrganizationId, workspace.ProjectId, $"?taskId={first.Id}");
        var byActor = await _kit.ListProjectAsync(workspace.Owner, workspace.OrganizationId, workspace.ProjectId, $"?actorId={contributor.Id}");
        var byAction = await _kit.ListProjectAsync(workspace.Owner, workspace.OrganizationId, workspace.ProjectId, "?action=task.created");
        var byEntity = await _kit.ListProjectAsync(workspace.Owner, workspace.OrganizationId, workspace.ProjectId, "?entityType=project");
        var recent = await _kit.ListProjectAsync(
            workspace.Owner, workspace.OrganizationId, workspace.ProjectId, $"?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"))}");

        // Eventos: projeto criado, membro adicionado e duas tarefas; mais o registro manual, de ontem, por último.
        Assert.Equal(5, all.TotalCount);
        Assert.Equal(ActivitySource.Log, all.Items[^1].Source);
        Assert.True(all.Items.Zip(all.Items.Skip(1)).All(pair => pair.First.OccurredAt >= pair.Second.OccurredAt));
        Assert.Equal((4, 1), (project.TotalCount, logs.TotalCount));
        Assert.Equal(["task.created", null], byTask.Items.Select(item => item.Action));
        Assert.Equal([("task.created", second.Id), (null, first.Id)], byActor.Items.Select(item => (item.Action, item.TaskId!.Value)));
        Assert.Equal(2, byAction.TotalCount);
        Assert.Equal(["project.created"], byEntity.Items.Select(item => item.Action));
        Assert.Equal(4, recent.TotalCount);

        var page1 = await _kit.ListProjectAsync(workspace.Owner, workspace.OrganizationId, workspace.ProjectId, "?pageSize=2&page=1");
        var page3 = await _kit.ListProjectAsync(workspace.Owner, workspace.OrganizationId, workspace.ProjectId, "?pageSize=2&page=3");
        Assert.Equal(all.Items.Take(2).Select(item => item.Id), page1.Items.Select(item => item.Id));
        Assert.Equal([all.Items[^1].Id], page3.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task ManualLogs_AreValidated_AndFollowProjectPermissions()
    {
        var workspace = await Tasks.CreateWorkspaceAsync();
        var viewer = await Tasks.AddTeamMemberAsync(workspace, "Viewer");
        var otherProject = await Tasks.CreateSecondProjectAsync(workspace);
        var foreignTask = await Tasks.CreateTaskAsync(new Workspace(workspace.Owner, workspace.OrganizationId, otherProject.Id), "Fora");

        using var future = await Tasks.SendToProjectAsync(HttpMethod.Post, workspace, "/activities", workspace.Owner,
            new { kind = "Note", summary = "Futuro", occurredAt = DateTimeOffset.UtcNow.AddHours(1) });
        using var missingKind = await Tasks.SendToProjectAsync(HttpMethod.Post, workspace, "/activities", workspace.Owner, new { summary = "Sem tipo" });
        using var taskOfOtherProject = await Tasks.SendToProjectAsync(HttpMethod.Post, workspace, "/activities", workspace.Owner,
            new { kind = "Note", summary = "Tarefa alheia", taskId = foreignTask.Id });
        using var byViewer = await Tasks.SendToProjectAsync(HttpMethod.Post, workspace, "/activities", viewer, new { kind = "Note", summary = "Observador" });
        using var viewerReads = await Tasks.SendToProjectAsync(HttpMethod.Get, workspace, "/activities", viewer);
        using (var archive = await Tasks.SendToProjectAsync(HttpMethod.Delete, workspace, "", workspace.Owner))
        {
            archive.EnsureSuccessStatusCode();
        }

        using var archived = await Tasks.SendToProjectAsync(HttpMethod.Post, workspace, "/activities", workspace.Owner, new { kind = "Note", summary = "Depois" });

        Assert.Equal(
            [HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.OK, HttpStatusCode.Conflict],
            new[] { future.StatusCode, missingKind.StatusCode, taskOfOtherProject.StatusCode, byViewer.StatusCode, viewerReads.StatusCode, archived.StatusCode });
    }

    [Fact]
    public async Task OrganizationActivities_RequireThePermission_AreIsolated_AndCarryNoEmails()
    {
        var workspace = await Tasks.CreateWorkspaceAsync();
        var member = await Tasks.Organizations.CreateUserAsync();
        await Tasks.Organizations.AddMemberAsync(workspace.Owner, workspace.OrganizationId, member, "Member");
        var administrator = await Tasks.Organizations.CreateUserAsync();
        await Tasks.Organizations.AddMemberAsync(workspace.Owner, workspace.OrganizationId, administrator, "Administrator");
        var outsider = await Tasks.CreateWorkspaceAsync("Projeto alheio");

        using var asMember = await Tasks.Projects.SendAsync(HttpMethod.Get, $"/api/v1/organizations/{workspace.OrganizationId}/activities", member);
        using var asOutsider = await Tasks.Projects.SendAsync(HttpMethod.Get, $"/api/v1/organizations/{workspace.OrganizationId}/activities", outsider.Owner);
        using var projectAsOutsider = await Tasks.Projects.SendToProjectAsync(
            HttpMethod.Get, workspace.ProjectId, "/activities", outsider.Owner, outsider.OrganizationId);
        using var raw = await Tasks.Projects.SendAsync(HttpMethod.Get, $"/api/v1/organizations/{workspace.OrganizationId}/activities?pageSize=100", administrator);
        var text = await raw.Content.ReadAsStringAsync(CancellationToken);
        var items = JsonSerializer.Deserialize<Application.Common.PagedResult<ActivityItem>>(text, ProjectTestKit.Json)!.Items;
        var outsiderItems = (await _kit.ListOrganizationAsync(outsider.Owner, outsider.OrganizationId)).Items;
        var byProject = await _kit.ListOrganizationAsync(administrator, workspace.OrganizationId, $"?projectId={workspace.ProjectId}");

        Assert.Equal(
            [HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.OK],
            new[] { asMember.StatusCode, asOutsider.StatusCode, projectAsOutsider.StatusCode, raw.StatusCode });
        Assert.Contains(items, item => item.Action == "member.invited");
        Assert.Contains(items, item => item.Action == "invitation.accepted");
        Assert.Contains(items, item => item.ProjectId == workspace.ProjectId);
        Assert.All(byProject.Items, item => Assert.Equal(workspace.ProjectId, item.ProjectId));
        Assert.DoesNotContain(member.Email, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(administrator.Email, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(outsiderItems, item => items.Any(own => own.Id == item.Id));
        Assert.All(outsiderItems, item => Assert.NotEqual(workspace.ProjectId, item.ProjectId));
    }

    public void Dispose() => _factory.Dispose();

    private async Task<int> CountAsync(Workspace workspace) =>
        (await _kit.ListOrganizationAsync(workspace.Owner, workspace.OrganizationId, "?pageSize=1")).TotalCount;

    private async Task<WorkflowDetails> ReadWorkflowAsync(Workspace workspace)
    {
        using var response = await Tasks.SendToProjectAsync(HttpMethod.Get, workspace, "/workflow", workspace.Owner);
        return await ProjectTestKit.ReadAsync<WorkflowDetails>(response);
    }
}
