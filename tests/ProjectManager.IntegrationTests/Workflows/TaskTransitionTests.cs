using System.Net;
using ProjectManager.Application.Common;
using ProjectManager.Application.Tasks;
using ProjectManager.Application.Workflows;
using ProjectManager.Domain.Workflows;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Organizations;
using ProjectManager.IntegrationTests.Tasks;

namespace ProjectManager.IntegrationTests.Workflows;

/// <summary>
/// Mudança de estado das tarefas (ADR-013): transições validadas no backend, aprovação, próprio trabalho,
/// dependências, estado final somente leitura, concorrência, histórico e filtros.
/// </summary>
public sealed class TaskTransitionTests : IDisposable
{
    private static readonly byte[] PdfBytes = "%PDF-1.4 anexo"u8.ToArray();

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly TaskTestKit _kit;

    public TaskTransitionTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _client = _factory.CreateClient();
        _kit = new TaskTestKit(_client, mailpit);
    }

    [Fact]
    public async Task Task_GoesFromBacklogToDone_WithApproval_AndTheHistoryRecordsEveryStep()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var workflow = await WorkflowAsync(workspace);
        var task = await _kit.CreateTaskAsync(workspace, "Entrega");

        Assert.Equal(("Backlog", StateCategory.Todo, false), (task.State.Name, task.State.Category, task.State.IsFinal));
        Assert.Equal(["A fazer", "Bloqueada"], task.AvailableTransitions.Select(t => t.ToStateName));

        foreach (var target in new[] { "A fazer", "Em andamento", "Em revisão" })
        {
            using var step = await MoveAsync(workspace, task.Id, State(workflow, target).Id, workspace.Owner);
            Assert.Equal(HttpStatusCode.OK, step.StatusCode);
        }

        using var approve = await MoveAsync(workspace, task.Id, State(workflow, "Concluída").Id, workspace.Owner, comment: "Aprovada na revisão");
        var done = await TaskTestKit.ReadAsync<TaskDetails>(approve);
        Assert.Equal(("Concluída", true), (done.State.Name, done.State.IsFinal));
        Assert.Equal(["Em andamento"], done.AvailableTransitions.Select(t => t.ToStateName));

        // Estado final: somente leitura, mas aceita comentário e reabertura.
        using var edit = await _kit.SendToTaskAsync(HttpMethod.Patch, workspace, task.Id, "", workspace.Owner, new { title = "Mudou" });
        using var assign = await _kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/assignments", workspace.Owner, new { userId = workspace.Owner.Id });
        using var attach = await UploadAsync(workspace, task.Id);
        using var comment = await _kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/comments", workspace.Owner, new { body = "Ficou ótimo" });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, assign.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, attach.StatusCode);
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);

        using var reopen = await MoveAsync(workspace, task.Id, State(workflow, "Em andamento").Id, workspace.Owner);
        using var editAfterReopen = await _kit.SendToTaskAsync(HttpMethod.Patch, workspace, task.Id, "", workspace.Owner, new { title = "Revisada" });
        Assert.Equal(HttpStatusCode.OK, reopen.StatusCode);
        Assert.Equal(HttpStatusCode.OK, editAfterReopen.StatusCode);

        var history = await HistoryAsync(workspace, task.Id);
        Assert.Equal(
            ["-> Backlog", "Backlog -> A fazer", "A fazer -> Em andamento", "Em andamento -> Em revisão", "Em revisão -> Concluída", "Concluída -> Em andamento"],
            history.Select(h => $"{h.FromStateName} -> {h.ToStateName}".TrimStart()));
        Assert.All(history, h => Assert.Equal((workspace.Owner.Id, ActorKind.User), (h.ChangedById, h.ActorKind)));
        Assert.Equal("Aprovada na revisão", history[4].Comment);
        Assert.False(string.IsNullOrEmpty(history[0].ChangedByName));
    }

    [Fact]
    public async Task InvalidTransitions_AreRejected_WithoutChangingTheTaskOrTheHistory()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var workflow = await WorkflowAsync(workspace);
        var otherProject = await _kit.CreateSecondProjectAsync(workspace);
        var otherWorkflow = await TaskTestKit.ReadAsync<WorkflowDetails>(
            await _kit.Projects.SendToProjectAsync(HttpMethod.Get, otherProject.Id, "/workflow", workspace.Owner, workspace.OrganizationId));
        var task = await _kit.CreateTaskAsync(workspace, "Parada");

        using var skip = await MoveAsync(workspace, task.Id, State(workflow, "Concluída").Id, workspace.Owner);
        using var same = await MoveAsync(workspace, task.Id, State(workflow, "Backlog").Id, workspace.Owner);
        using var foreign = await MoveAsync(workspace, task.Id, State(otherWorkflow, "A fazer").Id, workspace.Owner);
        using var unknown = await MoveAsync(workspace, task.Id, Guid.NewGuid(), workspace.Owner);
        using var stale = await MoveAsync(workspace, task.Id, State(workflow, "A fazer").Id, workspace.Owner, expectedStateId: State(workflow, "A fazer").Id);
        using var noTarget = await _kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/transitions", workspace.Owner, new { comment = "sem destino" });

        Assert.Equal(HttpStatusCode.Conflict, skip.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, same.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noTarget.StatusCode);

        using var details = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, "", workspace.Owner);
        Assert.Equal("Backlog", (await TaskTestKit.ReadAsync<TaskDetails>(details)).State.Name);
        Assert.Single(await HistoryAsync(workspace, task.Id));
    }

    [Fact]
    public async Task ProjectRoles_AndTheApprovalPermission_AreEnforced()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var contributor = await _kit.AddTeamMemberAsync(workspace, "Contributor");
        var viewer = await _kit.AddTeamMemberAsync(workspace, "Viewer");
        var outsideTheTeam = await _kit.Organizations.CreateUserAsync();
        await _kit.Organizations.AddMemberAsync(workspace.Owner, workspace.OrganizationId, outsideTheTeam, "Member");
        var intruder = await _kit.Organizations.CreateUserAsync();
        var intruderOrganization = await _kit.Organizations.CreateOrganizationAsync(intruder);
        var workflow = await WorkflowAsync(workspace);
        var task = await _kit.CreateTaskAsync(workspace, "Com papéis");

        using var viewerMoves = await MoveAsync(workspace, task.Id, State(workflow, "A fazer").Id, viewer);
        using var viewerReadsHistory = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, "/history", viewer);
        Assert.Equal(HttpStatusCode.Forbidden, viewerMoves.StatusCode);
        Assert.Equal(HttpStatusCode.OK, viewerReadsHistory.StatusCode);

        foreach (var target in new[] { "A fazer", "Em andamento", "Em revisão" })
        {
            using var step = await MoveAsync(workspace, task.Id, State(workflow, target).Id, contributor);
            Assert.Equal(HttpStatusCode.OK, step.StatusCode);
        }

        using var contributorView = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, "", contributor);
        using var contributorApproves = await MoveAsync(workspace, task.Id, State(workflow, "Concluída").Id, contributor);
        Assert.DoesNotContain((await TaskTestKit.ReadAsync<TaskDetails>(contributorView)).AvailableTransitions, t => t.RequiresApproval);
        Assert.Equal(HttpStatusCode.Forbidden, contributorApproves.StatusCode);

        foreach (var (user, organizationId) in new[] { (outsideTheTeam, workspace.OrganizationId), (intruder, intruderOrganization.Id), (intruder, workspace.OrganizationId) })
        {
            using var move = await _kit.SendToTaskAsync(HttpMethod.Post, task.Id, "/transitions", user, organizationId, new { toStateId = State(workflow, "Concluída").Id });
            using var history = await _kit.SendToTaskAsync(HttpMethod.Get, task.Id, "/history", user, organizationId);
            Assert.Equal(HttpStatusCode.NotFound, move.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);
        }

        using var managerApproves = await MoveAsync(workspace, task.Id, State(workflow, "Concluída").Id, workspace.Owner);
        Assert.Equal(HttpStatusCode.OK, managerApproves.StatusCode);
    }

    [Fact]
    public async Task IndependentApproval_StopsAssigneesFromApprovingTheirOwnWork()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var otherManager = await _kit.AddTeamMemberAsync(workspace, "Manager");
        var workflow = await WorkflowAsync(workspace);
        var own = await _kit.CreateTaskAsync(workspace, workspace.Owner, new { title = "Minha", assigneeIds = new List<Guid> { workspace.Owner.Id } });
        var ownToo = await _kit.CreateTaskAsync(workspace, workspace.Owner, new { title = "Minha também", assigneeIds = new List<Guid> { workspace.Owner.Id } });
        await BringToReviewAsync(workspace, workflow, own.Id);
        await BringToReviewAsync(workspace, workflow, ownToo.Id);

        // Desligada (padrão): o Gerente responsável aprova o próprio trabalho.
        using var allowed = await MoveAsync(workspace, own.Id, State(workflow, "Concluída").Id, workspace.Owner);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        using var turnOn = await _kit.SendToProjectAsync(HttpMethod.Put, workspace, "/workflow", workspace.Owner, new { requireIndependentApproval = true });
        using var ownView = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, ownToo.Id, "", workspace.Owner);
        using var selfApproval = await MoveAsync(workspace, ownToo.Id, State(workflow, "Concluída").Id, workspace.Owner);
        using var independent = await MoveAsync(workspace, ownToo.Id, State(workflow, "Concluída").Id, otherManager);

        Assert.Equal(HttpStatusCode.OK, turnOn.StatusCode);
        Assert.DoesNotContain((await TaskTestKit.ReadAsync<TaskDetails>(ownView)).AvailableTransitions, t => t.RequiresApproval);
        Assert.Equal(HttpStatusCode.Forbidden, selfApproval.StatusCode);
        Assert.Equal(HttpStatusCode.OK, independent.StatusCode);
    }

    [Fact]
    public async Task OpenDependencies_BlockCompletion_UntilTheyReachAFinalState()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var workflow = await WorkflowAsync(workspace);
        var blocked = await _kit.CreateTaskAsync(workspace, "Depende");
        var blocker = await _kit.CreateTaskAsync(workspace, "Bloqueadora");
        var archivedBlocker = await _kit.CreateTaskAsync(workspace, "Abandonada");
        (await _kit.SendToTaskAsync(HttpMethod.Post, workspace, blocked.Id, "/dependencies", workspace.Owner, new { dependsOnTaskId = blocker.Id })).Dispose();
        (await _kit.SendToTaskAsync(HttpMethod.Post, workspace, blocked.Id, "/dependencies", workspace.Owner, new { dependsOnTaskId = archivedBlocker.Id })).Dispose();
        (await _kit.SendToTaskAsync(HttpMethod.Delete, workspace, archivedBlocker.Id, "", workspace.Owner)).Dispose();
        await BringToReviewAsync(workspace, workflow, blocked.Id);

        using var tooEarly = await MoveAsync(workspace, blocked.Id, State(workflow, "Concluída").Id, workspace.Owner);
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);

        await BringToReviewAsync(workspace, workflow, blocker.Id);
        using var finishBlocker = await MoveAsync(workspace, blocker.Id, State(workflow, "Concluída").Id, workspace.Owner);
        using var nowItWorks = await MoveAsync(workspace, blocked.Id, State(workflow, "Concluída").Id, workspace.Owner);

        Assert.Equal(HttpStatusCode.OK, finishBlocker.StatusCode);
        Assert.Equal(HttpStatusCode.OK, nowItWorks.StatusCode);
    }

    [Fact]
    public async Task SimultaneousTransitions_LetOnlyOneWin()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var workflow = await WorkflowAsync(workspace);
        var task = await _kit.CreateTaskAsync(workspace, "Disputada");

        var responses = await Task.WhenAll(
            MoveAsync(workspace, task.Id, State(workflow, "A fazer").Id, workspace.Owner),
            MoveAsync(workspace, task.Id, State(workflow, "Bloqueada").Id, workspace.Owner));
        var statuses = responses.Select(r => r.StatusCode).ToList();
        responses.ToList().ForEach(r => r.Dispose());

        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.Equal(2, (await HistoryAsync(workspace, task.Id)).Count);
        Assert.Single(statuses, HttpStatusCode.OK);
    }

    [Fact]
    public async Task History_StaysReadable_AfterStatesAreRenamedOrDeleted()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var workflow = await WorkflowAsync(workspace);
        var task = await _kit.CreateTaskAsync(workspace, "Com passado");
        (await MoveAsync(workspace, task.Id, State(workflow, "Bloqueada").Id, workspace.Owner)).Dispose();
        (await MoveAsync(workspace, task.Id, State(workflow, "A fazer").Id, workspace.Owner)).Dispose();

        using var rename = await _kit.Projects.SendAsync(HttpMethod.Patch, $"/api/v1/workflow/states/{State(workflow, "A fazer").Id}", workspace.Owner, new { name = "Pronta" }, workspace.OrganizationId.ToString());
        using var delete = await _kit.Projects.SendAsync(HttpMethod.Delete, $"/api/v1/workflow/states/{State(workflow, "Bloqueada").Id}", workspace.Owner, null, workspace.OrganizationId.ToString());

        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(["Backlog", "Bloqueada", "A fazer"], (await HistoryAsync(workspace, task.Id)).Select(h => h.ToStateName));

        using var details = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, "", workspace.Owner);
        Assert.Equal("Pronta", (await TaskTestKit.ReadAsync<TaskDetails>(details)).State.Name);
    }

    [Fact]
    public async Task List_FiltersAndSortsByState()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var workflow = await WorkflowAsync(workspace);
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var finished = await _kit.CreateTaskAsync(workspace, workspace.Owner, new { title = "Concluída e atrasada", dueDate = yesterday });
        var doing = await _kit.CreateTaskAsync(workspace, workspace.Owner, new { title = "Em andamento e atrasada", dueDate = yesterday });
        var waiting = await _kit.CreateTaskAsync(workspace, "No backlog");
        await BringToReviewAsync(workspace, workflow, finished.Id);
        (await MoveAsync(workspace, finished.Id, State(workflow, "Concluída").Id, workspace.Owner)).Dispose();
        (await MoveAsync(workspace, doing.Id, State(workflow, "A fazer").Id, workspace.Owner)).Dispose();
        (await MoveAsync(workspace, doing.Id, State(workflow, "Em andamento").Id, workspace.Owner)).Dispose();

        async Task<List<int>> NumbersAsync(string query)
        {
            using var response = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, $"/tasks{query}", workspace.Owner);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await TaskTestKit.ReadAsync<PagedResult<TaskSummary>>(response)).Items.Select(t => t.Number).ToList();
        }

        Assert.Equal([doing.Number, waiting.Number], await NumbersAsync("?open=true"));
        Assert.Equal([finished.Number], await NumbersAsync("?category=Done"));
        Assert.Equal([doing.Number], await NumbersAsync($"?stateId={State(workflow, "Em andamento").Id}"));
        Assert.Equal([doing.Number], await NumbersAsync("?overdue=true"));
        Assert.Equal([waiting.Number, doing.Number, finished.Number], await NumbersAsync("?sortBy=State"));

        using var list = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, "/tasks", workspace.Owner);
        var summary = (await TaskTestKit.ReadAsync<PagedResult<TaskSummary>>(list)).Items.Single(t => t.Id == finished.Id);
        Assert.Equal(("Concluída", StateCategory.Done, true), (summary.State.Name, summary.State.Category, summary.State.IsFinal));
    }

    private async Task BringToReviewAsync(Workspace workspace, WorkflowDetails workflow, Guid taskId)
    {
        foreach (var target in new[] { "A fazer", "Em andamento", "Em revisão" })
        {
            using var step = await MoveAsync(workspace, taskId, State(workflow, target).Id, workspace.Owner);
            Assert.Equal(HttpStatusCode.OK, step.StatusCode);
        }
    }

    private Task<HttpResponseMessage> MoveAsync(
        Workspace workspace,
        Guid taskId,
        Guid toStateId,
        TestUser user,
        Guid? expectedStateId = null,
        string? comment = null) =>
        _kit.SendToTaskAsync(HttpMethod.Post, workspace, taskId, "/transitions", user, new { toStateId, expectedStateId, comment });

    private async Task<List<TaskHistoryEntry>> HistoryAsync(Workspace workspace, Guid taskId)
    {
        using var response = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, taskId, "/history", workspace.Owner);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await TaskTestKit.ReadAsync<List<TaskHistoryEntry>>(response);
    }

    private async Task<WorkflowDetails> WorkflowAsync(Workspace workspace)
    {
        using var response = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, "/workflow", workspace.Owner);
        return await TaskTestKit.ReadAsync<WorkflowDetails>(response);
    }

    private async Task<HttpResponseMessage> UploadAsync(Workspace workspace, Guid taskId)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(PdfBytes), "file", "anexo.pdf");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/tasks/{taskId}/attachments", UriKind.Relative)) { Content = form };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", workspace.Owner.AccessToken);
        request.Headers.TryAddWithoutValidation(Projects.ProjectTestKit.OrganizationHeader, workspace.OrganizationId.ToString());

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static WorkflowStateDetails State(WorkflowDetails workflow, string name) => workflow.States.Single(s => s.Name == name);

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
