using System.Net;
using ProjectManager.Application.Workflows;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Organizations;
using ProjectManager.IntegrationTests.Tasks;

namespace ProjectManager.IntegrationTests.Workflows;

/// <summary>Configuração do workflow por projeto (ADR-013): estados, transições, invariantes e permissões.</summary>
public sealed class WorkflowConfigurationTests : IDisposable
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly TaskTestKit _kit;

    public WorkflowConfigurationTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _client = _factory.CreateClient();
        _kit = new TaskTestKit(_client, mailpit);
    }

    [Fact]
    public async Task NewProject_StartsWithTheDefaultWorkflow_AndNewTasksEnterTheInitialState()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        await _kit.CreateTaskAsync(workspace, "Primeira");

        var workflow = await GetWorkflowAsync(workspace);

        Assert.Equal(["Backlog", "A fazer", "Em andamento", "Em revisão", "Bloqueada", "Concluída"], workflow.States.Select(s => s.Name));
        Assert.Equal("Backlog", Assert.Single(workflow.States, s => s.IsInitial).Name);
        Assert.Equal(1, workflow.States.Single(s => s.IsInitial).TaskCount);
        Assert.Equal(["Concluída"], workflow.States.Where(s => s.IsFinal).Select(s => s.Name));
        Assert.Equal(14, workflow.Transitions.Count);
        Assert.Equal(2, workflow.Transitions.Count(t => t.RequiresApproval));
        Assert.False(workflow.RequireIndependentApproval);
    }

    [Fact]
    public async Task Manager_ConfiguresStatesAndTransitions_WithinTheInvariants()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        await _kit.CreateTaskAsync(workspace, "Ocupa o Backlog");
        var initial = await GetWorkflowAsync(workspace);
        var backlog = State(initial, "Backlog");
        var todo = State(initial, "A fazer");

        using var add = await _kit.SendToProjectAsync(HttpMethod.Post, workspace, "/workflow/states", workspace.Owner, new { name = "Cancelada", category = "Canceled" });
        var canceled = await TaskTestKit.ReadAsync<WorkflowStateDetails>(add);
        using var duplicate = await _kit.SendToProjectAsync(HttpMethod.Post, workspace, "/workflow/states", workspace.Owner, new { name = "BACKLOG", category = "Todo" });
        using var noCategory = await _kit.SendToProjectAsync(HttpMethod.Post, workspace, "/workflow/states", workspace.Owner, new { name = "Sem categoria" });
        using var rename = await SendAsync(HttpMethod.Patch, workspace, $"/workflow/states/{canceled.Id}", new { name = "Descartada" });
        using var initialToFinal = await SendAsync(HttpMethod.Patch, workspace, $"/workflow/states/{backlog.Id}", new { category = "Done" });

        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        Assert.Equal((true, 7), (canceled.IsFinal, canceled.Position));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noCategory.StatusCode);
        Assert.Equal("Descartada", (await TaskTestKit.ReadAsync<WorkflowStateDetails>(rename)).Name);
        Assert.Equal(HttpStatusCode.Conflict, initialToFinal.StatusCode);

        using var transition = await SendAsync(HttpMethod.Post, workspace, "/workflow/transitions", new { fromStateId = todo.Id, toStateId = canceled.Id, requiresApproval = true });
        var created = await TaskTestKit.ReadAsync<WorkflowTransitionDetails>(transition);
        using var repeated = await SendAsync(HttpMethod.Post, workspace, "/workflow/transitions", new { fromStateId = todo.Id, toStateId = canceled.Id });
        using var loop = await SendAsync(HttpMethod.Post, workspace, "/workflow/transitions", new { fromStateId = todo.Id, toStateId = todo.Id });

        Assert.Equal(HttpStatusCode.Created, transition.StatusCode);
        Assert.True(created.RequiresApproval);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, loop.StatusCode);

        using var newInitial = await _kit.SendToProjectAsync(HttpMethod.Put, workspace, "/workflow", workspace.Owner, new { initialStateId = todo.Id, requireIndependentApproval = true });
        var settings = await TaskTestKit.ReadAsync<WorkflowDetails>(newInitial);
        using var deleteWithTasks = await SendAsync(HttpMethod.Delete, workspace, $"/workflow/states/{backlog.Id}");
        using var deleteInitial = await SendAsync(HttpMethod.Delete, workspace, $"/workflow/states/{todo.Id}");
        using var move = await SendAsync(HttpMethod.Post, workspace, $"/workflow/states/{canceled.Id}/move", new { position = 1 });
        using var deleteEmpty = await SendAsync(HttpMethod.Delete, workspace, $"/workflow/states/{canceled.Id}");

        Assert.Equal((todo.Id, true), (settings.States.Single(s => s.IsInitial).Id, settings.RequireIndependentApproval));
        Assert.Single(settings.States, s => s.IsInitial);
        Assert.Equal(HttpStatusCode.Conflict, deleteWithTasks.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deleteInitial.StatusCode);
        Assert.Equal("Descartada", (await TaskTestKit.ReadAsync<WorkflowDetails>(move)).States[0].Name);
        Assert.Equal(HttpStatusCode.NoContent, deleteEmpty.StatusCode);

        var final = await GetWorkflowAsync(workspace);
        Assert.DoesNotContain(final.States, s => s.Id == canceled.Id);
        Assert.DoesNotContain(final.Transitions, t => t.Id == created.Id);
        Assert.Equal(Enumerable.Range(1, 6), final.States.Select(s => s.Position));
    }

    [Fact]
    public async Task Projects_HaveIndependentWorkflows()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var otherProject = await _kit.CreateSecondProjectAsync(workspace);
        var otherWorkflow = await TaskTestKit.ReadAsync<WorkflowDetails>(
            await _kit.Projects.SendToProjectAsync(HttpMethod.Get, otherProject.Id, "/workflow", workspace.Owner, workspace.OrganizationId));
        var mine = await GetWorkflowAsync(workspace);

        using var renamed = await SendAsync(HttpMethod.Patch, workspace, $"/workflow/states/{State(mine, "Em revisão").Id}", new { name = "Homologação" });
        using var crossProject = await SendAsync(
            HttpMethod.Post, workspace, "/workflow/transitions", new { fromStateId = State(mine, "Backlog").Id, toStateId = State(otherWorkflow, "Concluída").Id });
        var otherAfter = await TaskTestKit.ReadAsync<WorkflowDetails>(
            await _kit.Projects.SendToProjectAsync(HttpMethod.Get, otherProject.Id, "/workflow", workspace.Owner, workspace.OrganizationId));

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossProject.StatusCode);
        Assert.Contains(otherAfter.States, s => s.Name == "Em revisão");
        Assert.DoesNotContain(otherAfter.States.Select(s => s.Id), id => mine.States.Any(s => s.Id == id));
    }

    [Fact]
    public async Task OnlyWorkflowManagers_ChangeTheWorkflow_AndOtherOrganizationsSeeNothing()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var contributor = await _kit.AddTeamMemberAsync(workspace, "Contributor");
        var viewer = await _kit.AddTeamMemberAsync(workspace, "Viewer");
        var intruder = await _kit.Organizations.CreateUserAsync();
        var intruderOrganization = await _kit.Organizations.CreateOrganizationAsync(intruder);
        var workflow = await GetWorkflowAsync(workspace);
        var stateId = State(workflow, "Em revisão").Id;
        var transitionId = workflow.Transitions[0].Id;

        foreach (var user in new[] { contributor, viewer })
        {
            using var read = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, "/workflow", user);
            using var add = await _kit.SendToProjectAsync(HttpMethod.Post, workspace, "/workflow/states", user, new { name = "Nova", category = "Todo" });
            using var patchState = await SendAsync(HttpMethod.Patch, workspace, $"/workflow/states/{stateId}", new { name = "x" }, user);
            using var deleteTransition = await SendAsync(HttpMethod.Delete, workspace, $"/workflow/transitions/{transitionId}", body: null, user);
            using var createTransition = await SendAsync(HttpMethod.Post, workspace, "/workflow/transitions", new { fromStateId = stateId, toStateId = State(workflow, "Backlog").Id }, user);

            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, patchState.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, deleteTransition.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, createTransition.StatusCode);
        }

        foreach (var organizationId in new[] { intruderOrganization.Id, workspace.OrganizationId })
        {
            (HttpMethod Method, string Path, object? Body)[] requests =
            [
                (HttpMethod.Get, $"/api/v1/projects/{workspace.ProjectId}/workflow", null),
                (HttpMethod.Put, $"/api/v1/projects/{workspace.ProjectId}/workflow", new { requireIndependentApproval = true }),
                (HttpMethod.Patch, $"/api/v1/workflow/states/{stateId}", new { name = "Invadido" }),
                (HttpMethod.Delete, $"/api/v1/workflow/states/{stateId}", null),
                (HttpMethod.Post, "/api/v1/workflow/transitions", new { fromStateId = stateId, toStateId = State(workflow, "Backlog").Id }),
                (HttpMethod.Delete, $"/api/v1/workflow/transitions/{transitionId}", null),
            ];

            foreach (var (method, path, body) in requests)
            {
                using var response = await _kit.Projects.SendAsync(method, path, intruder, body, organizationId.ToString());
                Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{method} {path}: esperado 404, obtido {response.StatusCode}.");
            }
        }

        using var archive = await _kit.SendToProjectAsync(HttpMethod.Delete, workspace, "", workspace.Owner);
        using var editArchived = await SendAsync(HttpMethod.Patch, workspace, $"/workflow/states/{stateId}", new { name = "Arquivado" });
        Assert.Equal(HttpStatusCode.Conflict, editArchived.StatusCode);
        Assert.Equal("Em revisão", State(await GetWorkflowAsync(workspace), "Em revisão").Name);
    }

    private async Task<WorkflowDetails> GetWorkflowAsync(Workspace workspace)
    {
        using var response = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, "/workflow", workspace.Owner);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await TaskTestKit.ReadAsync<WorkflowDetails>(response);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, Workspace workspace, string path, object? body = null, TestUser? user = null) =>
        _kit.Projects.SendAsync(method, $"/api/v1{path}", user ?? workspace.Owner, body, workspace.OrganizationId.ToString());

    private static WorkflowStateDetails State(WorkflowDetails workflow, string name) => workflow.States.Single(s => s.Name == name);

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
